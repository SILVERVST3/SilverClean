using Microsoft.Win32;

namespace SilverClean.Core
{
    public class StartupEntry : Observable
    {
        public string Name { get; init; }
        public string Command { get; init; }
        public string Location { get; init; }

        internal RegistryHive ApprovedHive;
        internal string ApprovedKey;   // ...\StartupApproved\Run | Run32 | StartupFolder
        internal string ApprovedValue; // nome do valor (nome da entrada ou do arquivo)
        internal string FilePath;      // atalho da pasta Inicializar (se houver)

        private bool _enabled;
        public bool Enabled
        {
            get => _enabled;
            set { if (Set(ref _enabled, value)) Raise(nameof(StatusText)); }
        }

        public string StatusText => Enabled ? "Ativado" : "Desativado";

        public string TargetPath
        {
            get
            {
                if (!string.IsNullOrEmpty(FilePath)) return FilePath;
                return CommandLine.Split(Command).exe;
            }
        }
    }

    /// <summary>
    /// Lê e altera os itens de inicialização usando o mesmo mecanismo do Gerenciador de Tarefas
    /// (chaves StartupApproved), então nada é apagado: desativar é totalmente reversível.
    /// </summary>
    public static class StartupManager
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string RunKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        private const string Approved = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupEntry> Load()
        {
            var list = new List<StartupEntry>();

            ReadRun(list, RegistryHive.CurrentUser, RunKey, Approved + "Run", "Registro (usuário atual)");
            ReadRun(list, RegistryHive.LocalMachine, RunKey, Approved + "Run", "Registro (todos os usuários)");
            ReadRun(list, RegistryHive.LocalMachine, RunKey32, Approved + "Run32", "Registro 32 bits (todos os usuários)");

            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                       RegistryHive.CurrentUser, "Pasta Inicializar (usuário atual)");
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                       RegistryHive.LocalMachine, "Pasta Inicializar (todos os usuários)");

            return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static RegistryKey Base(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

        private static void ReadRun(List<StartupEntry> list, RegistryHive hive, string runKey, string approvedKey, string location)
        {
            try
            {
                using var b = Base(hive);
                using var k = b.OpenSubKey(runKey);
                if (k == null) return;
                foreach (var name in k.GetValueNames())
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    var cmd = k.GetValue(name)?.ToString() ?? "";
                    list.Add(new StartupEntry
                    {
                        Name = name,
                        Command = cmd,
                        Location = location,
                        ApprovedHive = hive,
                        ApprovedKey = approvedKey,
                        ApprovedValue = name,
                        Enabled = ReadEnabled(hive, approvedKey, name)
                    });
                }
            }
            catch (Exception ex) { Log.Write($"Erro lendo {runKey}: {ex.Message}"); }
        }

        private static void ReadFolder(List<StartupEntry> list, string folder, RegistryHive hive, string location)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (var f in Directory.GetFiles(folder))
                {
                    string fn = Path.GetFileName(f);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new StartupEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Command = f,
                        Location = location,
                        FilePath = f,
                        ApprovedHive = hive,
                        ApprovedKey = Approved + "StartupFolder",
                        ApprovedValue = fn,
                        Enabled = ReadEnabled(hive, Approved + "StartupFolder", fn)
                    });
                }
            }
            catch (Exception ex) { Log.Write($"Erro lendo pasta {folder}: {ex.Message}"); }
        }

        private static bool ReadEnabled(RegistryHive hive, string key, string value)
        {
            try
            {
                using var b = Base(hive);
                using var k = b.OpenSubKey(key);
                if (k?.GetValue(value) is byte[] data && data.Length > 0)
                    return (data[0] & 1) == 0; // 02/06 = ativado, 03/07 = desativado
            }
            catch { }
            return true; // sem registro = ativado
        }

        public static void SetEnabled(StartupEntry e, bool enabled)
        {
            var data = new byte[12];
            if (enabled)
            {
                data[0] = 0x02;
            }
            else
            {
                data[0] = 0x03;
                BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(data, 4);
            }

            using var b = Base(e.ApprovedHive);
            using var k = b.CreateSubKey(e.ApprovedKey, true);
            k.SetValue(e.ApprovedValue, data, RegistryValueKind.Binary);
            e.Enabled = enabled;
            Log.Write($"Inicialização: '{e.Name}' {(enabled ? "ativado" : "desativado")}");
        }
    }
}
