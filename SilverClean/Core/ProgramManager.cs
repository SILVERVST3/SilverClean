using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SilverClean.Core
{
    public class InstalledProgram
    {
        public string Name { get; init; }
        public string Publisher { get; init; }
        public string Version { get; init; }
        public DateTime? InstallDate { get; init; }
        public long SizeBytes { get; init; }
        public string UninstallString { get; init; }
        public string Scope { get; init; }

        public string InstallDateText => InstallDate?.ToString("dd/MM/yyyy") ?? "";
        public string SizeText => SizeBytes > 0 ? Fmt.Size(SizeBytes) : "";
    }

    public static class ProgramManager
    {
        private const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string Key32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<InstalledProgram> Load()
        {
            var list = new List<InstalledProgram>();
            Read(list, RegistryHive.LocalMachine, Key, "64 bits");
            Read(list, RegistryHive.LocalMachine, Key32, "32 bits");
            Read(list, RegistryHive.CurrentUser, Key, "Usuário");

            return list
                .GroupBy(p => (p.Name.ToLowerInvariant(), (p.Version ?? "").ToLowerInvariant()))
                .Select(g => g.First())
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static void Read(List<InstalledProgram> list, RegistryHive hive, string path, string scope)
        {
            try
            {
                using var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var root = b.OpenSubKey(path);
                if (root == null) return;

                foreach (var sub in root.GetSubKeyNames())
                {
                    try
                    {
                        using var k = root.OpenSubKey(sub);
                        if (k == null) continue;

                        string name = k.GetValue("DisplayName")?.ToString();
                        string uninst = k.GetValue("UninstallString")?.ToString();
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uninst)) continue;
                        if (k.GetValue("SystemComponent")?.ToString() == "1") continue;
                        if (k.GetValue("ParentKeyName") != null) continue; // atualizações/componentes
                        string rel = k.GetValue("ReleaseType")?.ToString() ?? "";
                        if (rel.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                            rel.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)) continue;

                        long size = 0;
                        var es = k.GetValue("EstimatedSize");
                        if (es != null) { try { size = Convert.ToInt64(es) * 1024; } catch { } }

                        DateTime? date = null;
                        var ds = k.GetValue("InstallDate")?.ToString();
                        if (!string.IsNullOrEmpty(ds) &&
                            DateTime.TryParseExact(ds, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                            date = d;

                        list.Add(new InstalledProgram
                        {
                            Name = name.Trim(),
                            Publisher = k.GetValue("Publisher")?.ToString() ?? "",
                            Version = k.GetValue("DisplayVersion")?.ToString() ?? "",
                            InstallDate = date,
                            SizeBytes = size,
                            UninstallString = uninst,
                            Scope = scope
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log.Write($"Erro lendo programas ({path}): {ex.Message}"); }
        }

        /// <summary>Abre o desinstalador do próprio programa e espera ele terminar.</summary>
        public static async Task UninstallAsync(InstalledProgram p)
        {
            var (exe, args) = CommandLine.Split(p.UninstallString);

            if (Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
                exe.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                // MsiExec /I{GUID} abre "modificar"; /X{GUID} desinstala
                args = Regex.Replace(args, @"(^|\s)/I", "$1/X", RegexOptions.IgnoreCase);
            }

            Log.Write($"Desinstalando '{p.Name}': {exe} {args}");
            using var proc = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
            if (proc != null) await proc.WaitForExitAsync();
        }
    }
}
