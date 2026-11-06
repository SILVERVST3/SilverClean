using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using SilverClean.Core;

namespace SilverClean.Pages
{
    public partial class RegistryPage : UserControl
    {
        private readonly List<RegCategory> _cats = RegistryScanner.Categories();
        private readonly ObservableCollection<RegIssue> _issues = new();
        private CancellationTokenSource _cts;

        public RegistryPage()
        {
            InitializeComponent();
            CategoryList.ItemsSource = _cats;

            var view = new ListCollectionView(_issues);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RegIssue.Category)));
            IssueGrid.ItemsSource = view;
            IssueGrid.GroupStyle.Add(BuildGroupStyle());
        }

        private static GroupStyle BuildGroupStyle()
        {
            // Cabeçalho simples de grupo: "Categoria (N)"
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            text.SetValue(TextBlock.ForegroundProperty, Application.Current.FindResource("AccentDark"));
            text.SetValue(TextBlock.MarginProperty, new Thickness(6, 8, 0, 3));
            return new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = text } };
        }

        private void SetBusy(bool busy)
        {
            ScanBtn.IsEnabled = !busy;
            FixBtn.IsEnabled = !busy && _issues.Any(i => i.Status == "");
            CancelBtn.IsEnabled = busy;
            CategoryList.IsEnabled = !busy;
            BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
            BusyBar.IsIndeterminate = busy;
            if (!busy) StatusText.Text = "";
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void CheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var i in _issues.Where(x => x.Status == "")) i.IsChecked = true;
        }

        private void UncheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var i in _issues) i.IsChecked = false;
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            var cats = _cats.Where(c => c.IsChecked).ToList();
            if (cats.Count == 0)
            {
                MessageBox.Show("Marque pelo menos uma categoria.", "SilverClean", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _issues.Clear();
            foreach (var c in _cats) c.Count = 0;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            SetBusy(true);
            SummaryTitle.Text = "Verificando o Registro...";

            try
            {
                var progress = new Progress<string>(s => StatusText.Text = s);
                var found = await Task.Run(() => RegistryScanner.Scan(cats, progress, ct), ct);

                foreach (var i in found) _issues.Add(i);
                foreach (var c in _cats) c.Count = found.Count(i => i.Category == c.Name);

                if (found.Count == 0)
                {
                    SummaryTitle.Text = "✔ Nenhum problema encontrado";
                    SummaryText.Text = "O Registro está em ordem nas categorias verificadas.";
                }
                else
                {
                    SummaryTitle.Text = $"{found.Count} problemas encontrados";
                    SummaryText.Text = "Revise a lista e desmarque o que não quiser alterar. Ao corrigir, um backup é salvo antes.";
                }
                Log.Write($"Verificação do Registro: {found.Count} problemas");
            }
            catch (OperationCanceledException)
            {
                SummaryTitle.Text = "Verificação cancelada";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void Fix_Click(object sender, RoutedEventArgs e)
        {
            var sel = _issues.Where(i => i.IsChecked && i.Status == "").ToList();
            if (sel.Count == 0)
            {
                MessageBox.Show("Nenhum item selecionado.", "SilverClean", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var r = MessageBox.Show(
                $"Corrigir {sel.Count} itens do Registro?\n\n" +
                "Um backup (.reg) será salvo antes, e você pode restaurá-lo depois pelo botão 'Restaurar backup'.",
                "Confirmar correção", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            SetBusy(true);
            StatusText.Text = "Salvando backup...";
            string backup;
            try
            {
                backup = await Task.Run(() => RegistryScanner.Backup(sel));
            }
            catch (Exception ex)
            {
                SetBusy(false);
                MessageBox.Show("Não foi possível criar o backup, então nada foi alterado.\n\n" + ex.Message,
                                "SilverClean", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int ok = 0, fail = 0;
            StatusText.Text = "Corrigindo...";
            foreach (var i in sel)
            {
                try
                {
                    await Task.Run(() => RegistryScanner.Fix(i));
                    i.Status = "Corrigido";
                    i.IsChecked = false;
                    ok++;
                }
                catch (Exception ex)
                {
                    i.Status = "Falhou";
                    Log.Write($"Falha ao corrigir {i.KeyText}: {ex.Message}");
                    fail++;
                }
            }

            SetBusy(false);
            SummaryTitle.Text = $"✔ {ok} itens corrigidos";
            SummaryText.Text = (fail > 0 ? $"{fail} não puderam ser alterados. " : "") +
                               $"Backup salvo em: {backup}";
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(RegistryScanner.BackupFolder);
            var dlg = new OpenFileDialog
            {
                Title = "Escolha o backup do Registro para restaurar",
                Filter = "Backup do Registro (*.reg)|*.reg",
                InitialDirectory = RegistryScanner.BackupFolder
            };
            if (dlg.ShowDialog() != true) return;

            var r = MessageBox.Show($"Restaurar o backup\n{Path.GetFileName(dlg.FileName)}?\n\n" +
                                    "As entradas removidas voltarão ao Registro.",
                                    "Restaurar backup", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            bool ok = RegistryScanner.Restore(dlg.FileName);
            MessageBox.Show(ok ? "Backup restaurado com sucesso." : "O Windows não conseguiu importar o backup (veja o log).",
                            "SilverClean", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }
}
