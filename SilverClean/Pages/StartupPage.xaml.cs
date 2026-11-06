using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SilverClean.Core;

namespace SilverClean.Pages
{
    public partial class StartupPage : UserControl
    {
        private bool _loaded;

        public StartupPage()
        {
            InitializeComponent();
        }

        public void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Reload();
        }

        private void Reload()
        {
            var list = StartupManager.Load();
            StartupGrid.ItemsSource = list;
            int on = list.Count(x => x.Enabled);
            InfoText.Text = $"{list.Count} itens encontrados, {on} ativados. Desativar não apaga nada: é o mesmo ajuste " +
                            "do Gerenciador de Tarefas e pode ser revertido a qualquer momento.";
        }

        private StartupEntry Selected => StartupGrid.SelectedItem as StartupEntry;

        private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

        private void Toggle(bool enable)
        {
            var s = Selected;
            if (s == null)
            {
                MessageBox.Show("Selecione um item na lista.", "SilverClean", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                StartupManager.SetEnabled(s, enable);
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível alterar o item:\n" + ex.Message, "SilverClean",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Enable_Click(object sender, RoutedEventArgs e) => Toggle(true);
        private void Disable_Click(object sender, RoutedEventArgs e) => Toggle(false);

        private void OpenLocation_Click(object sender, RoutedEventArgs e)
        {
            var s = Selected;
            if (s == null) return;
            string p = s.TargetPath;
            if (File.Exists(p) || Directory.Exists(p)) CommandLine.ShowInExplorer(p);
            else MessageBox.Show("O arquivo não foi encontrado:\n" + p, "SilverClean", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Grid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(StartupGrid, e.OriginalSource as DependencyObject) is not DataGridRow) return;
            var s = Selected;
            if (s != null) Toggle(!s.Enabled);
        }
    }
}
