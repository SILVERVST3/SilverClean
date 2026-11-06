using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SilverClean.Core;

namespace SilverClean.Pages
{
    public class DriveChoice
    {
        public string Path { get; init; }
        public string Label { get; init; }
    }

    public class DiskRow
    {
        public string Name { get; init; }
        public string FullPath { get; init; }
        public long Size { get; init; }
        public double Percent { get; init; }
        public int Files { get; init; }
        public DirNode Node { get; init; } // null = "arquivos nesta pasta"

        public string SizeText => Fmt.Size(Size);
        public string PercentText => $"{Percent:0.0}%";
        public string FilesText => Files.ToString("N0");
    }

    public partial class DiskPage : UserControl
    {
        private CancellationTokenSource _cts;
        private DirNode _current;
        private string _customFolder;

        public DiskPage()
        {
            InitializeComponent();
            LoadDrives();
        }

        private void LoadDrives()
        {
            var list = new List<DriveChoice>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                    long used = d.TotalSize - d.TotalFreeSpace;
                    string label = string.IsNullOrEmpty(d.VolumeLabel) ? "Disco local" : d.VolumeLabel;
                    list.Add(new DriveChoice
                    {
                        Path = d.RootDirectory.FullName,
                        Label = $"{d.Name.TrimEnd('\\')}  {label} — {Fmt.Size(used)} usados de {Fmt.Size(d.TotalSize)}"
                    });
                }
                catch { }
            }
            DriveBox.ItemsSource = list;
            if (list.Count > 0) DriveBox.SelectedIndex = 0;
        }

        private void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Escolha a pasta para analisar" };
            if (dlg.ShowDialog() == true)
            {
                _customFolder = dlg.FolderName;
                StatusText.Text = "Pasta escolhida: " + _customFolder;
                Scan_Click(sender, e);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            string path = _customFolder ?? (DriveBox.SelectedItem as DriveChoice)?.Path;
            _customFolder = null;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var analyzer = new DiskAnalyzer();

            ScanBtn.IsEnabled = false;
            CancelBtn.IsEnabled = true;
            BusyBar.Visibility = Visibility.Visible;
            BusyBar.IsIndeterminate = true;
            FolderGrid.ItemsSource = null;
            FilesGrid.ItemsSource = null;
            TitleText.Text = "Analisando " + path;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, a) => StatusText.Text = $"{analyzer.FilesScanned:N0} arquivos • {analyzer.CurrentFolder}";
            timer.Start();

            try
            {
                var result = await Task.Run(() => analyzer.Scan(path, ct), ct);
                FilesGrid.ItemsSource = result.LargestFiles;
                ShowNode(result.Root);
                TitleText.Text = $"{path} — {Fmt.Size(result.Root.Size)}";
                InfoText.Text = $"{result.Root.FileCount:N0} arquivos analisados. Pastas protegidas do sistema " +
                                "que o Windows não deixa ler não entram na conta.";
                Log.Write($"Análise de disco: {path} = {Fmt.Size(result.Root.Size)}");
            }
            catch (OperationCanceledException)
            {
                TitleText.Text = "Análise cancelada";
            }
            finally
            {
                timer.Stop();
                StatusText.Text = "";
                BusyBar.Visibility = Visibility.Hidden;
                BusyBar.IsIndeterminate = false;
                ScanBtn.IsEnabled = true;
                CancelBtn.IsEnabled = false;
            }
        }

        private void ShowNode(DirNode node)
        {
            _current = node;
            PathText.Text = $"{node.FullPath}   ({Fmt.Size(node.Size)})";
            UpBtn.IsEnabled = node.Parent != null;

            double total = Math.Max(1, node.Size);
            var rows = node.Children.Select(c => new DiskRow
            {
                Name = "📁 " + c.Name,
                FullPath = c.FullPath,
                Size = c.Size,
                Files = c.FileCount,
                Percent = c.Size * 100.0 / total,
                Node = c
            }).ToList();

            if (node.OwnFiles > 0)
            {
                int own = node.FileCount - node.Children.Sum(c => c.FileCount);
                rows.Add(new DiskRow
                {
                    Name = "📄 [arquivos nesta pasta]",
                    FullPath = node.FullPath,
                    Size = node.OwnFiles,
                    Files = own,
                    Percent = node.OwnFiles * 100.0 / total
                });
            }

            FolderGrid.ItemsSource = rows.OrderByDescending(r => r.Size).ToList();
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            if (_current?.Parent != null) ShowNode(_current.Parent);
        }

        private void FolderGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(FolderGrid, e.OriginalSource as DependencyObject) is not DataGridRow) return;
            if (FolderGrid.SelectedItem is DiskRow r && r.Node != null && r.Node.Children.Count + (r.Node.OwnFiles > 0 ? 1 : 0) > 0)
                ShowNode(r.Node);
        }

        private void FilesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(FilesGrid, e.OriginalSource as DependencyObject) is not DataGridRow) return;
            if (FilesGrid.SelectedItem is FileEntry f) CommandLine.ShowInExplorer(f.Path);
        }

        private void OpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (FilesGrid.IsVisible && FilesGrid.SelectedItem is FileEntry f)
                CommandLine.ShowInExplorer(f.Path);
            else if (FolderGrid.SelectedItem is DiskRow r)
                CommandLine.ShowInExplorer(r.FullPath);
            else if (_current != null)
                CommandLine.ShowInExplorer(_current.FullPath);
        }
    }
}
