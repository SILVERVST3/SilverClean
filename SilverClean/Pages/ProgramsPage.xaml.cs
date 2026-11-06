using System.Windows;
using System.Windows.Controls;
using SilverClean.Core;

namespace SilverClean.Pages
{
    public partial class ProgramsPage : UserControl
    {
        private List<InstalledProgram> _all = new();
        private bool _loaded;

        public ProgramsPage()
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
            _all = ProgramManager.Load();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string q = SearchBox.Text?.Trim() ?? "";
            var list = string.IsNullOrEmpty(q)
                ? _all
                : _all.Where(p => p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                                  p.Publisher.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
            ProgGrid.ItemsSource = list;
            long total = _all.Sum(p => p.SizeBytes);
            InfoText.Text = $"{_all.Count} programas instalados (≈ {Fmt.Size(total)} informados pelos instaladores). " +
                            "Desinstalar abre o desinstalador oficial de cada programa.";
        }

        private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (ProgGrid.SelectedItem is not InstalledProgram p)
            {
                MessageBox.Show("Selecione um programa na lista.", "SilverClean", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var r = MessageBox.Show($"Abrir o desinstalador de \"{p.Name}\"?", "Desinstalar",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            UninstallBtn.IsEnabled = false;
            try
            {
                await ProgramManager.UninstallAsync(p);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível iniciar o desinstalador:\n" + ex.Message, "SilverClean",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                UninstallBtn.IsEnabled = true;
                Reload();
            }
        }
    }
}
