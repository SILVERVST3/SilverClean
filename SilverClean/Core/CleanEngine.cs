using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace SilverClean.Core
{
    /// <summary>Uma pasta (e máscara de arquivos) que um item de limpeza varre.</summary>
    public class CleanTarget
    {
        public string Folder;
        public string Pattern = "*";
        public bool Recursive = true;
        /// <summary>Só apaga arquivos sem modificação há pelo menos estas horas (0 = todos).</summary>
        public int MinAgeHours;
        public bool DeleteEmptyDirs = true;

        public CleanTarget(string folder, string pattern = "*", bool recursive = true, int minAgeHours = 0)
        {
            Folder = folder; Pattern = pattern; Recursive = recursive; MinAgeHours = minAgeHours;
        }
    }

    public class CleanItem : Observable
    {
        public string Group { get; init; }
        public string Name { get; init; }
        public string Description { get; init; }
        public bool DefaultChecked { get; init; }
        public bool IsRecycleBin { get; init; }
        /// <summary>Processos do navegador; se estiverem abertos o item é ignorado.</summary>
        public string[] ProcessNames { get; init; } = Array.Empty<string>();
        public string BrowserName { get; init; }
        public Func<IEnumerable<CleanTarget>> Targets { get; init; } = () => Array.Empty<CleanTarget>();

        private bool _checked;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }

        // Resultado da análise
        internal List<(string path, long size)> Found = new();
        internal List<CleanTarget> FoundTargets = new();
        public int FoundFiles { get; internal set; }
        public long FoundBytes { get; internal set; }
        public bool Skipped { get; internal set; }

        private string _note = "";
        public string Note { get => _note; set => Set(ref _note, value); }

        public string FilesText => IsRecycleBin ? $"{FoundFiles} itens" : $"{FoundFiles:N0}";
        public string SizeText => Fmt.Size(FoundBytes);

        public void ResetResult()
        {
            Found = new(); FoundTargets = new();
            FoundFiles = 0; FoundBytes = 0; Skipped = false; Note = "";
            RefreshResult();
        }

        public void RefreshResult()
        {
            Raise(nameof(FoundFiles)); Raise(nameof(FoundBytes));
            Raise(nameof(FilesText)); Raise(nameof(SizeText));
        }
    }

    public class CleanGroup
    {
        public string Name { get; init; }
        public ObservableCollection<CleanItem> Items { get; } = new();
    }

    public class CleanSummary
    {
        public long Bytes;
        public int Files;
        public int Failed;
    }

    public static class CleanEngine
    {
        private static readonly EnumerationOptions OptRec = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // não segue junções/links
            ReturnSpecialDirectories = false
        };

        private static readonly EnumerationOptions OptFlat = new()
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        /// <summary>Nunca deixa limpar a raiz de um disco ou pastas críticas inteiras.</summary>
        public static bool IsSafeFolder(string folder)
        {
            try
            {
                string full = Path.GetFullPath(folder).TrimEnd('\\');
                string root = (Path.GetPathRoot(full) ?? "").TrimEnd('\\');
                if (full.Length <= 3 || string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return false;

                string[] forbidden =
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                };
                foreach (var f in forbidden)
                {
                    if (string.IsNullOrEmpty(f)) continue;
                    if (string.Equals(full, f.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
                }
                return true;
            }
            catch { return false; }
        }

        // ---------------- Lixeira ----------------
        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 1, SHERB_NOPROGRESSUI = 2, SHERB_NOSOUND = 4;

        private static (long size, long count) QueryRecycleBin()
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            int hr = SHQueryRecycleBin(null, ref info);
            return hr == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
        }

        // ---------------- Análise ----------------
        public static void Analyze(CleanItem item, IProgress<string> progress, CancellationToken ct)
        {
            item.Found = new();
            item.FoundTargets = new();
            item.FoundFiles = 0;
            item.FoundBytes = 0;

            if (item.IsRecycleBin)
            {
                var (size, count) = QueryRecycleBin();
                item.FoundBytes = size;
                item.FoundFiles = (int)Math.Min(count, int.MaxValue);
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in item.Targets())
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(t.Folder) || !Directory.Exists(t.Folder)) continue;
                // Varreduras amplas (recursivas ou com curinga) só em pastas seguras;
                // um arquivo com nome exato (ex.: C:\Windows\MEMORY.DMP) pode ficar em qualquer lugar.
                bool broad = t.Recursive || t.Pattern.Contains('*') || t.Pattern.Contains('?');
                if (broad && !IsSafeFolder(t.Folder)) continue;

                progress?.Report($"Analisando: {t.Folder}");
                item.FoundTargets.Add(t);
                DateTime limit = DateTime.UtcNow.AddHours(-t.MinAgeHours);

                try
                {
                    var di = new DirectoryInfo(t.Folder);
                    foreach (var f in di.EnumerateFiles(t.Pattern, t.Recursive ? OptRec : OptFlat))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            if (t.MinAgeHours > 0 && f.LastWriteTimeUtc > limit) continue;
                            if (!seen.Add(f.FullName)) continue;
                            long len = f.Length;
                            item.Found.Add((f.FullName, len));
                            item.FoundBytes += len;
                            item.FoundFiles++;
                        }
                        catch { /* arquivo sumiu no meio da análise */ }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Log.Write($"Erro ao analisar {t.Folder}: {ex.Message}"); }
            }
        }

        // ---------------- Limpeza ----------------
        public static CleanSummary Clean(CleanItem item, IProgress<string> progress, CancellationToken ct)
        {
            var s = new CleanSummary();

            if (item.IsRecycleBin)
            {
                var (size, count) = QueryRecycleBin();
                if (count > 0)
                {
                    int hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                    if (hr == 0) { s.Bytes = size; s.Files = (int)Math.Min(count, int.MaxValue); }
                    else s.Failed = 1;
                }
                Log.Write($"Lixeira esvaziada: {Fmt.Size(s.Bytes)}");
                return s;
            }

            progress?.Report($"Limpando: {item.Group} › {item.Name}");
            foreach (var (path, size) in item.Found)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var attr = File.GetAttributes(path);
                    if ((attr & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
                    File.Delete(path);
                    s.Bytes += size;
                    s.Files++;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch { s.Failed++; } // em uso ou sem permissão: ignora
            }

            // Remove subpastas que ficaram vazias (nunca a pasta-alvo em si)
            foreach (var t in item.FoundTargets.Where(x => x.Recursive && x.DeleteEmptyDirs))
            {
                try
                {
                    var dirs = Directory.EnumerateDirectories(t.Folder, "*", OptRec)
                                        .OrderByDescending(d => d.Length).ToList();
                    foreach (var d in dirs)
                    {
                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(d).Any())
                                Directory.Delete(d, false);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            Log.Write($"Limpo [{item.Group}] {item.Name}: {s.Files} arquivos, {Fmt.Size(s.Bytes)}, {s.Failed} em uso/ignorados");
            return s;
        }
    }
}
