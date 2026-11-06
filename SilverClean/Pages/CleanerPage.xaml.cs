using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SilverClean.Core;

namespace SilverClean.Pages
{
    public partial class CleanerPage : UserControl
    {
        private readonly List<CleanGroup> _groups;
        private readonly ObservableCollection<CleanItem> _results = new();
        private List<CleanItem> _analyzed = new();
        private bool _analysisValid;
        private CancellationTokenSource _cts;

        public CleanerPage()
        {
            InitializeComponent();
            _groups = CleanDefinitions.Build();
            GroupList.ItemsSource = _groups;
            ResultGrid.ItemsSource = _results;

            foreach (var i in AllItems)
                i.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(CleanItem.IsChecked)) _analysisValid = false;
                };
        }

        private IEnumerable<CleanItem> AllItems => _groups.SelectMany(g => g.Items);

        private void Defaults_Click(object sender, RoutedEventArgs e)
        {
            foreach (var i in AllItems) i.IsChecked = i.DefaultChecked;
        }

        private void None_Click(object sender, RoutedEventArgs e)
        {
            foreach (var i in AllItems) i.IsChecked = false;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void SetBusy(bool busy)
        {
            AnalyzeBtn.IsEnabled = !busy;
            CleanBtn.IsEnabled = !busy;
            CancelBtn.IsEnabled = busy;
            GroupList.IsEnabled = !busy;
            BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
            BusyBar.IsIndeterminate = busy;
            if (!busy) StatusText.Text = "";
        }

        /// <summary>Verifica navegadores abertos. Devolve os itens que devem ser ignorados.</summary>
        private async Task<HashSet<CleanItem>> HandleOpenBrowsersAsync(List<CleanItem> items)
        {
            var skip = new HashSet<CleanItem>();
            var open = items.Where(i => i.ProcessNames.Length > 0)
                            .GroupBy(i => i.BrowserName)
                            .Where(g => CleanDefinitions.IsRunning(g.First().ProcessNames))
                            .ToList();
            if (open.Count == 0) return skip;

            string names = string.Join(", ", open.Select(g => g.Key));
            var r = MessageBox.Show(
                $"Estes navegadores estão abertos: {names}.\n\n" +
                "Para limpar os dados deles é preciso fechá-los. Fechar agora?\n" +
                "(Sim = fecha os navegadores  •  Não = pula esses itens)",
                "SilverClean", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (r == MessageBoxResult.Yes)
            {
                StatusText.Text = "Fechando navegadores...";
                await CleanDefinitions.CloseProcessesAsync(open.SelectMany(g => g.First().ProcessNames));
                foreach (var g in open)
                    if (CleanDefinitions.IsRunning(g.First().ProcessNames))
                        foreach (var i in g) skip.Add(i);
            }
            else
            {
                foreach (var g in open) foreach (var i in g) skip.Add(i);
            }
            return skip;
        }

        private async Task<bool> AnalyzeAsync()
        {
            var items = AllItems.Where(i => i.IsChecked).ToList();
            if (items.Count == 0)
            {
                MessageBox.Show("Marque pelo menos um item para analisar.", "SilverClean",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            SetBusy(true);
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _results.Clear();
            SummaryTitle.Text = "Analisando...";
            SummaryText.Text = "Procurando arquivos que podem ser removidos.";

            try
            {
                var skip = await HandleOpenBrowsersAsync(items);
                var progress = new Progress<string>(s => StatusText.Text = s);

                foreach (var item in items)
                {
                    item.ResetResult();
                    _results.Add(item);

                    if (skip.Contains(item))
                    {
                        item.Skipped = true;
                        item.Note = "Navegador aberto — ignorado";
                        continue;
                    }

                    await Task.Run(() => CleanEngine.Analyze(item, progress, ct), ct);
                    item.RefreshResult();
                    if (item.FoundFiles == 0) item.Note = "Nada encontrado";
                }

                _analyzed = items.Where(i => !i.Skipped).ToList();
                _analysisValid = true;

                long total = _analyzed.Sum(i => i.FoundBytes);
                int files = _analyzed.Where(i => !i.IsRecycleBin).Sum(i => i.FoundFiles);
                SummaryTitle.Text = $"{Fmt.Size(total)} podem ser liberados";
                SummaryText.Text = $"{files:N0} arquivos encontrados. Clique em Limpar para remover. " +
                                   "Arquivos em uso serão ignorados automaticamente.";
                return true;
            }
            catch (OperationCanceledException)
            {
                SummaryTitle.Text = "Análise cancelada";
                SummaryText.Text = "";
                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void Analyze_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

        private async void Clean_Click(object sender, RoutedEventArgs e)
        {
            if (!_analysisValid && !await AnalyzeAsync()) return;

            long total = _analyzed.Sum(i => i.FoundBytes);
            int files = _analyzed.Sum(i => i.FoundFiles);
            if (files == 0)
            {
                MessageBox.Show("Não há nada para limpar.", "SilverClean", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var r = MessageBox.Show(
                $"Serão removidos aproximadamente {Fmt.Size(total)} ({files:N0} arquivos/itens).\n\n" +
                "Essa ação não pode ser desfeita. Continuar?",
                "Confirmar limpeza", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            SetBusy(true);
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var progress = new Progress<string>(s => StatusText.Text = s);
            SummaryTitle.Text = "Limpando...";
            SummaryText.Text = "";

            long freed = 0;
            int removed = 0, failed = 0;
            try
            {
                Log.Write($"=== Limpeza iniciada ({_analyzed.Count} itens) ===");
                foreach (var item in _analyzed)
                {
                    if (item.FoundFiles == 0) continue;
                    var s = await Task.Run(() => CleanEngine.Clean(item, progress, ct), ct);
                    freed += s.Bytes; removed += s.Files; failed += s.Failed;

                    item.Note = s.Failed > 0
                        ? $"Removido {Fmt.Size(s.Bytes)} • {s.Failed} em uso"
                        : $"Removido {Fmt.Size(s.Bytes)}";
                    item.FoundBytes = 0;
                    item.FoundFiles = 0;
                    item.Found = new();
                    item.RefreshResult();
                }

                SummaryTitle.Text = $"✔ {Fmt.Size(freed)} liberados";
                SummaryText.Text = $"{removed:N0} arquivos removidos." +
                                   (failed > 0 ? $" {failed:N0} estavam em uso ou protegidos e foram mantidos." : "");
                Log.Write($"=== Limpeza concluída: {Fmt.Size(freed)}, {removed} arquivos, {failed} ignorados ===");
            }
            catch (OperationCanceledException)
            {
                SummaryTitle.Text = $"Limpeza interrompida — {Fmt.Size(freed)} liberados";
                Log.Write("Limpeza cancelada pelo usuário");
            }
            finally
            {
                _analysisValid = false;
                SetBusy(false);
            }
        }
    }
}
