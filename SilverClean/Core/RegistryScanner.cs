using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace SilverClean.Core
{
    /// <summary>Um problema encontrado no Registro.</summary>
    public class RegIssue : Observable
    {
        public string Category { get; init; }
        public string Problem { get; init; }
        public string Data { get; init; }
        public RegistryHive HiveEnum { get; init; }
        public string KeyPath { get; init; }
        /// <summary>Valor a apagar. null = apagar a chave inteira.</summary>
        public string ValueName { get; init; }

        public string Hive => HiveEnum == RegistryHive.CurrentUser ? "HKEY_CURRENT_USER" : "HKEY_LOCAL_MACHINE";
        public string FullKey => $"{Hive}\\{KeyPath}";
        public string KeyText => ValueName == null ? FullKey : $"{FullKey}  »  {ValueName}";

        private bool _checked = true;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }

        private string _status = "";
        public string Status { get => _status; set => Set(ref _status, value); }
    }

    public class RegCategory : Observable
    {
        public string Name { get; init; }
        public string Description { get; init; }
        internal Action<List<RegIssue>, RegCategory> Scan { get; init; }

        private bool _checked = true;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }

        private int _count;
        public int Count { get => _count; set { if (Set(ref _count, value)) Raise(nameof(CountText)); } }
        public string CountText => _count > 0 ? _count.ToString() : "";
    }

    /// <summary>
    /// Verificação conservadora do Registro: só aponta entradas que se referem a arquivos/pastas
    /// que comprovadamente não existem num disco fixo. Nada de COM/ActiveX/drivers/serviços.
    /// </summary>
    public static class RegistryScanner
    {
        private const string CU_Classes = @"Software\Classes";
        private const string Uninst = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string Uninst32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

        public static string BackupFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SilverClean", "backups");

        // ================================================================ categorias
        public static List<RegCategory> Categories() => new()
        {
            new RegCategory
            {
                Name = "DLLs compartilhadas ausentes",
                Description = "Contadores de DLLs compartilhadas (SharedDLLs) cujo arquivo não existe mais.",
                Scan = ScanSharedDlls
            },
            new RegCategory
            {
                Name = "Caminhos de aplicativos",
                Description = "Atalhos do 'Executar' (App Paths) que apontam para programas removidos.",
                Scan = ScanAppPaths
            },
            new RegCategory
            {
                Name = "Desinstaladores inválidos",
                Description = "Programas que aparecem como instalados, mas cujo desinstalador e pasta não existem mais.",
                Scan = ScanUninstall
            },
            new RegCategory
            {
                Name = "Inicialização obsoleta",
                Description = "Itens que tentam abrir com o Windows um programa que não existe mais.",
                Scan = ScanRun
            },
            new RegCategory
            {
                Name = "Cache de nomes de programas (MuiCache)",
                Description = "Nomes guardados de programas que já foram apagados.",
                Scan = ScanMuiCache
            },
            new RegCategory
            {
                Name = "Pastas do instalador",
                Description = "Pastas registradas pelo Windows Installer que não existem mais.",
                Scan = ScanInstallerFolders
            },
            new RegCategory
            {
                Name = "Arquivos de ajuda ausentes",
                Description = "Arquivos de ajuda (.hlp/.chm) registrados que foram removidos.",
                Scan = ScanHelp
            },
            new RegCategory
            {
                Name = "Compatibilidade de programas",
                Description = "Configurações de compatibilidade de programas que não existem mais.",
                Scan = ScanAppCompat
            },
            new RegCategory
            {
                Name = "Extensões de arquivo inválidas",
                Description = "Extensões do seu usuário vazias ou ligadas a um tipo de arquivo que não existe.",
                Scan = ScanExtensions
            },
        };

        public static List<RegIssue> Scan(IEnumerable<RegCategory> cats, IProgress<string> progress, CancellationToken ct)
        {
            var all = new List<RegIssue>();
            foreach (var c in cats)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report("Verificando: " + c.Name);
                var found = new List<RegIssue>();
                try { c.Scan(found, c); }
                catch (Exception ex) { Log.Write($"Erro na verificação '{c.Name}': {ex.Message}"); }
                all.AddRange(found);
            }
            return all;
        }

        // ================================================================ helpers de Registro
        private static RegistryKey Open(RegistryHive hive, string path, bool write = false)
        {
            try
            {
                using var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                return b.OpenSubKey(path, write);
            }
            catch { return null; }
        }

        private static void ForEachValue(RegistryHive hive, string path, Action<RegistryKey, string> act)
        {
            using var k = Open(hive, path);
            if (k == null) return;
            string[] names;
            try { names = k.GetValueNames(); } catch { return; }
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                try { act(k, n); } catch { }
            }
        }

        private static void ForEachSubKey(RegistryHive hive, string path, Action<RegistryKey, string> act)
        {
            using var k = Open(hive, path);
            if (k == null) return;
            string[] names;
            try { names = k.GetSubKeyNames(); } catch { return; }
            foreach (var n in names)
            {
                try
                {
                    using var sk = k.OpenSubKey(n);
                    if (sk != null) act(sk, n);
                }
                catch { }
            }
        }

        private static RegIssue Issue(RegCategory c, string problem, string data, RegistryHive hive, string key, string value)
            => new() { Category = c.Name, Problem = problem, Data = data, HiveEnum = hive, KeyPath = key, ValueName = value };

        // ================================================================ verificação de caminhos
        private static readonly string[] ExeLike =
            { ".exe", ".dll", ".com", ".bat", ".cmd", ".lnk", ".ocx", ".cpl", ".msc", ".scr", ".vbs", ".ps1", ".chm", ".hlp" };

        /// <summary>
        /// true somente se for um caminho completo, num disco fixo presente, e o arquivo/pasta não existir.
        /// Em qualquer dúvida devolve false (não é problema).
        /// </summary>
        public static bool IsMissingPath(string raw)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(raw)) return false;
                string p = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"').Trim('\0')).Trim();

                // índice de ícone: "C:\x\app.exe,0"
                int comma = p.LastIndexOf(',');
                if (comma > 2 && int.TryParse(p.Substring(comma + 1), out _)) p = p.Substring(0, comma);

                if (p.StartsWith(@"\\") || p.Contains('%')) return false; // rede ou variável não resolvida
                if (!Path.IsPathFullyQualified(p)) return false;

                string root = Path.GetPathRoot(p);
                if (string.IsNullOrEmpty(root)) return false;
                var drive = new DriveInfo(root);
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) return false;

                if (File.Exists(p) || Directory.Exists(p)) return false;

                // Programas 32 bits gravam System32 querendo dizer SysWOW64 (e vice-versa)
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string sys32 = Path.Combine(win, "System32");
                string wow = Path.Combine(win, "SysWOW64");
                string native = Path.Combine(win, "Sysnative");
                foreach (var (from, to) in new[] { (sys32, wow), (wow, sys32), (native, sys32) })
                {
                    if (p.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        string alt = to + p.Substring(from.Length);
                        if (File.Exists(alt) || Directory.Exists(alt)) return false;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Para linhas de comando (ex.: "C:\App\app.exe" /min). Só acusa se achar um executável ausente.</summary>
        public static bool IsMissingCommand(string cmd, out string target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(cmd)) return false;
            string s = Environment.ExpandEnvironmentVariables(cmd.Trim());

            // O comando inteiro pode ser um caminho com espaços sem aspas
            string whole = s.Trim('"');
            if (File.Exists(whole)) return false;

            var (exe, args) = CommandLine.Split(s);
            exe = exe.Trim('"');

            // rundll32 arquivo.dll,Funcao → verifica a DLL
            string fn = Path.GetFileName(exe);
            if (fn.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) ||
                fn.Equals("rundll32", StringComparison.OrdinalIgnoreCase))
            {
                string a = args.Trim();
                if (a.StartsWith("\"")) { int e = a.IndexOf('"', 1); a = e > 0 ? a.Substring(1, e - 1) : a.Trim('"'); }
                else { int c = a.IndexOf(','); if (c > 0) a = a.Substring(0, c); }
                exe = a.Trim();
            }

            string ext = Path.GetExtension(exe);
            if (!ExeLike.Contains(ext, StringComparer.OrdinalIgnoreCase)) return false;

            target = exe;
            return IsMissingPath(exe);
        }

        // ================================================================ verificações
        private static void ScanSharedDlls(List<RegIssue> list, RegCategory c)
        {
            foreach (var key in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs",
                                        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\SharedDLLs" })
                ForEachValue(RegistryHive.LocalMachine, key, (k, name) =>
                {
                    if (IsMissingPath(name))
                        list.Add(Issue(c, "DLL compartilhada ausente", name, RegistryHive.LocalMachine, key, name));
                });
        }

        private static void ScanAppPaths(List<RegIssue> list, RegCategory c)
        {
            var sources = new[]
            {
                (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"),
                (RegistryHive.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\App Paths"),
            };
            foreach (var (hive, key) in sources)
                ForEachSubKey(hive, key, (sk, name) =>
                {
                    string target = sk.GetValue("")?.ToString();
                    if (IsMissingPath(target))
                        list.Add(Issue(c, $"Programa não encontrado ({name})", target, hive, $"{key}\\{name}", null));
                });
        }

        private static void ScanUninstall(List<RegIssue> list, RegCategory c)
        {
            var sources = new[]
            {
                (RegistryHive.LocalMachine, Uninst),
                (RegistryHive.LocalMachine, Uninst32),
                (RegistryHive.CurrentUser, Uninst),
            };
            foreach (var (hive, key) in sources)
                ForEachSubKey(hive, key, (sk, name) =>
                {
                    string display = sk.GetValue("DisplayName")?.ToString();
                    string uninst = sk.GetValue("UninstallString")?.ToString();
                    if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(uninst)) return;
                    if (sk.GetValue("WindowsInstaller")?.ToString() == "1") return;
                    if (uninst.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) return;

                    if (!IsMissingCommand(uninst, out string exe)) return;

                    // Se a pasta de instalação ainda existe, não mexe
                    string loc = sk.GetValue("InstallLocation")?.ToString();
                    if (!string.IsNullOrWhiteSpace(loc) && !IsMissingPath(loc)) return;

                    list.Add(Issue(c, $"Desinstalador ausente: {display}", exe, hive, $"{key}\\{name}", null));
                });
        }

        private static void ScanRun(List<RegIssue> list, RegCategory c)
        {
            var sources = new[]
            {
                (RegistryHive.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\Run"),
                (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
            };
            foreach (var (hive, key) in sources)
                ForEachValue(hive, key, (k, name) =>
                {
                    string cmd = k.GetValue(name)?.ToString();
                    if (IsMissingCommand(cmd, out string exe))
                        list.Add(Issue(c, $"Item de inicialização sem programa: {name}", exe, hive, key, name));
                });
        }

        private static void ScanMuiCache(List<RegIssue> list, RegCategory c)
        {
            const string key = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
            ForEachValue(RegistryHive.CurrentUser, key, (k, name) =>
            {
                if (name.StartsWith("@")) return;
                int idx = name.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return;
                string path = name.Substring(0, idx + 4);
                if (IsMissingPath(path))
                    list.Add(Issue(c, "Programa removido", path, RegistryHive.CurrentUser, key, name));
            });
        }

        private static void ScanInstallerFolders(List<RegIssue> list, RegCategory c)
        {
            const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders";
            ForEachValue(RegistryHive.LocalMachine, key, (k, name) =>
            {
                string dir = name.TrimEnd('\\');
                if (dir.Length > 3 && IsMissingPath(dir))
                    list.Add(Issue(c, "Pasta não existe mais", name, RegistryHive.LocalMachine, key, name));
            });
        }

        private static void ScanHelp(List<RegIssue> list, RegCategory c)
        {
            foreach (var key in new[] { @"SOFTWARE\Microsoft\Windows\Help", @"SOFTWARE\WOW6432Node\Microsoft\Windows\Help" })
                ForEachValue(RegistryHive.LocalMachine, key, (k, name) =>
                {
                    string folder = k.GetValue(name)?.ToString();
                    if (string.IsNullOrWhiteSpace(folder)) return;
                    string full = Path.Combine(Environment.ExpandEnvironmentVariables(folder), name);
                    if (IsMissingPath(full))
                        list.Add(Issue(c, "Arquivo de ajuda ausente", full, RegistryHive.LocalMachine, key, name));
                });
        }

        private static void ScanAppCompat(List<RegIssue> list, RegCategory c)
        {
            var sources = new[]
            {
                (RegistryHive.CurrentUser,  @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store"),
                (RegistryHive.CurrentUser,  @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"),
                (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"),
            };
            foreach (var (hive, key) in sources)
                ForEachValue(hive, key, (k, name) =>
                {
                    if (IsMissingPath(name))
                        list.Add(Issue(c, "Programa não existe mais", name, hive, key, name));
                });
        }

        private static void ScanExtensions(List<RegIssue> list, RegCategory c)
        {
            ForEachSubKey(RegistryHive.CurrentUser, CU_Classes, (sk, name) =>
            {
                if (!name.StartsWith(".") || name.Length < 2) return;

                var values = sk.GetValueNames();
                bool onlyDefault = values.All(v => string.IsNullOrEmpty(v));
                if (sk.SubKeyCount > 0 || !onlyDefault) return; // tem OpenWithProgids etc.: não mexe

                string progId = sk.GetValue("")?.ToString();
                if (string.IsNullOrWhiteSpace(progId))
                {
                    list.Add(Issue(c, $"Extensão {name} vazia", "(sem tipo de arquivo associado)",
                                   RegistryHive.CurrentUser, $"{CU_Classes}\\{name}", null));
                    return;
                }

                using var a = Open(RegistryHive.CurrentUser, $"{CU_Classes}\\{progId}");
                using var b = Open(RegistryHive.LocalMachine, $@"SOFTWARE\Classes\{progId}");
                if (a == null && b == null)
                    list.Add(Issue(c, $"Extensão {name} aponta para tipo inexistente", progId,
                                   RegistryHive.CurrentUser, $"{CU_Classes}\\{name}", null));
            });
        }

        // ================================================================ backup / correção / restauração
        private static bool KeyExists(RegIssue i)
        {
            using var k = Open(i.HiveEnum, i.KeyPath);
            return k != null;
        }

        private static int RunReg(string args)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"), args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode;
        }

        /// <summary>Exporta todas as chaves afetadas para um único .reg. Lança exceção se não conseguir.</summary>
        public static string Backup(IEnumerable<RegIssue> issues)
        {
            Directory.CreateDirectory(BackupFolder);
            string file = Path.Combine(BackupFolder, $"Registro_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.reg");

            var keys = issues.GroupBy(i => i.FullKey, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n\r\n");
            int exported = 0;

            foreach (var i in keys)
            {
                string tmp = Path.Combine(Path.GetTempPath(), $"silverclean_{Guid.NewGuid():N}.reg");
                try
                {
                    int code = RunReg($"export \"{i.FullKey}\" \"{tmp}\" /y");
                    if (code != 0 || !File.Exists(tmp))
                    {
                        if (!KeyExists(i)) continue; // a chave já não existe: nada a salvar nem a apagar
                        throw new IOException($"Falha ao exportar {i.FullKey}");
                    }
                    string text = File.ReadAllText(tmp); // reg.exe grava UTF-16 com BOM
                    int nl = text.IndexOf('\n');
                    sb.Append(nl >= 0 ? text.Substring(nl + 1).TrimStart('\r', '\n') : "");
                    sb.Append("\r\n");
                    exported++;
                }
                finally
                {
                    try { File.Delete(tmp); } catch { }
                }
            }

            File.WriteAllText(file, sb.ToString(), Encoding.Unicode);
            Log.Write($"Backup do Registro criado: {file} ({exported} chaves)");
            return file;
        }

        public static void Fix(RegIssue i)
        {
            using var b = RegistryKey.OpenBaseKey(i.HiveEnum, RegistryView.Registry64);
            if (i.ValueName != null)
            {
                using var k = b.OpenSubKey(i.KeyPath, true);
                k?.DeleteValue(i.ValueName, false);
            }
            else
            {
                int cut = i.KeyPath.LastIndexOf('\\');
                string parent = i.KeyPath.Substring(0, cut);
                string sub = i.KeyPath.Substring(cut + 1);
                using var k = b.OpenSubKey(parent, true);
                k?.DeleteSubKeyTree(sub, false);
            }
            Log.Write($"Registro corrigido [{i.Category}]: {i.KeyText}");
        }

        public static bool Restore(string regFile)
        {
            int code = RunReg($"import \"{regFile}\"");
            Log.Write($"Restauração do Registro de {regFile}: código {code}");
            return code == 0;
        }
    }
}
