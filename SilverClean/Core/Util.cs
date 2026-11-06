using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace SilverClean.Core
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    public static class Fmt
    {
        public static string Size(long bytes)
        {
            if (bytes < 0) bytes = 0;
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return i == 0 ? $"{bytes} B" : $"{v:0.##} {u[i]}";
        }
    }

    public static class Log
    {
        private static readonly object _lock = new();

        public static string Folder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SilverClean", "logs");

        public static void Write(string text)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(Folder);
                    string file = Path.Combine(Folder, $"SilverClean_{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch { /* log nunca pode derrubar o programa */ }
        }

        public static void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Folder}\"") { UseShellExecute = true });
            }
            catch { }
        }
    }

    public static class CommandLine
    {
        /// <summary>Separa "C:\caminho\app.exe" -args em (exe, args).</summary>
        public static (string exe, string args) Split(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return ("", "");
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());

            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 0) return (cmd.Substring(1, end - 1), cmd.Substring(end + 1).Trim());
            }

            int idx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                int cut = idx + 4;
                return (cmd.Substring(0, cut), cmd.Substring(cut).Trim());
            }

            int sp = cmd.IndexOf(' ');
            return sp > 0 ? (cmd.Substring(0, sp), cmd.Substring(sp + 1)) : (cmd, "");
        }

        public static void ShowInExplorer(string path)
        {
            try
            {
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                else if (Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
