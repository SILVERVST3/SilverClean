using System.Windows;
using SilverClean.Core;

namespace SilverClean
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var v = typeof(MainWindow).Assembly.GetName().Version;
            VersionText.Text = $"Versão {v?.Major}.{v?.Minor}.{v?.Build}";
            NavClean.IsChecked = true;
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PageClean == null) return; // ainda inicializando

            PageClean.Visibility = NavClean.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageStartup.Visibility = NavStartup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PagePrograms.Visibility = NavPrograms.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageDisk.Visibility = NavDisk.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            if (NavStartup.IsChecked == true) PageStartup.EnsureLoaded();
            if (NavPrograms.IsChecked == true) PagePrograms.EnsureLoaded();
        }

        private void OpenLogs_Click(object sender, RoutedEventArgs e) => Log.OpenFolder();
    }
}
