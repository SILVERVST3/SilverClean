using System.Diagnostics;

namespace SilverClean.Core
{
    public static class CleanDefinitions
    {
        private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private static string ProgData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        public static List<CleanGroup> Build()
        {
            var groups = new List<CleanGroup> { BuildWindows() };

            AddChromium(groups, "Google Chrome", new[] { "chrome" },
                () => ChromiumProfiles(Path.Combine(Local, @"Google\Chrome\User Data")));
            AddChromium(groups, "Microsoft Edge", new[] { "msedge" },
                () => ChromiumProfiles(Path.Combine(Local, @"Microsoft\Edge\User Data")));
            AddChromium(groups, "Brave", new[] { "brave" },
                () => ChromiumProfiles(Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data")));
            AddChromium(groups, "Opera", new[] { "opera" },
                () => OperaProfiles("Opera Stable"));
            AddChromium(groups, "Opera GX", new[] { "opera" },
                () => OperaProfiles("Opera GX Stable"));
            AddFirefox(groups);

            foreach (var g in groups)
                foreach (var i in g.Items)
                    i.IsChecked = i.DefaultChecked;

            return groups;
        }

        // ------------------------------------------------------------------ Windows
        private static CleanGroup BuildWindows()
        {
            var g = new CleanGroup { Name = "Windows" };
            void Add(string name, string desc, bool def, Func<IEnumerable<CleanTarget>> targets, bool recycle = false)
                => g.Items.Add(new CleanItem { Group = g.Name, Name = name, Description = desc, DefaultChecked = def, Targets = targets, IsRecycleBin = recycle });

            Add("Arquivos temporários do usuário",
                "Pasta %TEMP% do seu usuário. Só remove arquivos com mais de 24 horas.", true,
                () => new[] { new CleanTarget(Path.GetTempPath(), "*", true, 24) });

            Add("Arquivos temporários do Windows",
                "C:\\Windows\\Temp. Só remove arquivos com mais de 24 horas.", true,
                () => new[] { new CleanTarget(Path.Combine(Win, "Temp"), "*", true, 24) });

            Add("Lixeira", "Esvazia a Lixeira de todos os discos.", true, null, recycle: true);

            Add("Cache de miniaturas",
                "Miniaturas de imagens/vídeos do Explorer. O Windows recria quando precisar.", true,
                () => new[]
                {
                    new CleanTarget(Path.Combine(Local, @"Microsoft\Windows\Explorer"), "thumbcache_*.db", false),
                    new CleanTarget(Path.Combine(Local, @"Microsoft\Windows\Explorer"), "iconcache_*.db", false),
                });

            Add("Relatórios de erro do Windows",
                "Relatórios do Relatório de Erros do Windows (WER) já enviados ou na fila.", true,
                () => new[]
                {
                    new CleanTarget(Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportArchive")),
                    new CleanTarget(Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportQueue")),
                    new CleanTarget(Path.Combine(ProgData, @"Microsoft\Windows\WER\Temp")),
                    new CleanTarget(Path.Combine(Local, @"Microsoft\Windows\WER\ReportArchive")),
                    new CleanTarget(Path.Combine(Local, @"Microsoft\Windows\WER\ReportQueue")),
                });

            Add("Despejos de memória (dumps)",
                "Arquivos .dmp gerados quando programas ou o Windows travam.", true,
                () => new[]
                {
                    new CleanTarget(Path.Combine(Local, "CrashDumps")),
                    new CleanTarget(Path.Combine(Win, "Minidump")),
                    new CleanTarget(Win, "MEMORY.DMP", false),
                    new CleanTarget(Path.Combine(Win, "LiveKernelReports"), "*.dmp", true),
                });

            Add("Logs do Windows (mais de 7 dias)",
                "Arquivos .log em C:\\Windows\\Logs com mais de 7 dias.", true,
                () => new[] { new CleanTarget(Path.Combine(Win, "Logs"), "*.log", true, 24 * 7) { DeleteEmptyDirs = false } });

            Add("Cache do Windows Update",
                "Instaladores de atualizações já baixados (SoftwareDistribution\\Download). Seguro, mas o Windows pode baixar de novo algo pendente.", false,
                () => new[] { new CleanTarget(Path.Combine(Win, @"SoftwareDistribution\Download")) });

            Add("Cache de Otimização de Entrega",
                "Cópias de atualizações compartilhadas com outros PCs.", false,
                () => new[] { new CleanTarget(Path.Combine(Win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")) });

            Add("Prefetch",
                "Dados de pré-carregamento de programas. O Windows recria; a próxima inicialização pode ficar um pouco mais lenta.", false,
                () => new[] { new CleanTarget(Path.Combine(Win, "Prefetch"), "*.pf", false) });

            Add("Cache de shaders (DirectX / AMD / NVIDIA)",
                "Shaders compilados de jogos e da placa de vídeo. São recriados; jogos podem engasgar na primeira execução.", false,
                () => new[]
                {
                    new CleanTarget(Path.Combine(Local, "D3DSCache")),
                    new CleanTarget(Path.Combine(Local, @"AMD\DxCache")),
                    new CleanTarget(Path.Combine(Local, @"AMD\DxcCache")),
                    new CleanTarget(Path.Combine(Local, @"AMD\VkCache")),
                    new CleanTarget(Path.Combine(Local, @"AMD\GLCache")),
                    new CleanTarget(Path.Combine(Local, @"NVIDIA\DXCache")),
                    new CleanTarget(Path.Combine(Local, @"NVIDIA\GLCache")),
                });

            return g;
        }

        private static string[] SafeDirs(string folder)
        {
            try { return Directory.Exists(folder) ? Directory.GetDirectories(folder) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        // ------------------------------------------------------------------ Navegadores Chromium
        /// <summary>(pasta de dados do perfil, pasta de cache do perfil)</summary>
        private static IEnumerable<(string data, string cache)> ChromiumProfiles(string userData)
        {
            foreach (var d in SafeDirs(userData))
            {
                string n = Path.GetFileName(d);
                if (n == "Default" || n.StartsWith("Profile ") || n == "Guest Profile")
                    yield return (d, d);
            }
        }

        private static IEnumerable<(string data, string cache)> OperaProfiles(string folderName)
        {
            string data = Path.Combine(Roaming, "Opera Software", folderName);
            string cache = Path.Combine(Local, "Opera Software", folderName);
            if (!Directory.Exists(data)) yield break;

            // Versões novas usam a subpasta "Default"
            if (Directory.Exists(Path.Combine(data, "Default")))
                yield return (Path.Combine(data, "Default"), Path.Combine(cache, "Default"));
            yield return (data, cache);
        }

        private static void AddChromium(List<CleanGroup> groups, string browser, string[] procs,
                                        Func<IEnumerable<(string data, string cache)>> profiles)
        {
            if (!profiles().Any()) return; // navegador não instalado

            var g = new CleanGroup { Name = browser };

            g.Items.Add(new CleanItem
            {
                Group = browser, BrowserName = browser, ProcessNames = procs, DefaultChecked = true,
                Name = "Cache da internet",
                Description = "Imagens e arquivos de sites guardados para carregar mais rápido.",
                Targets = () => profiles().SelectMany(p => new[]
                {
                    new CleanTarget(Path.Combine(p.cache, "Cache")),
                    new CleanTarget(Path.Combine(p.cache, "Code Cache")),
                    new CleanTarget(Path.Combine(p.cache, "GPUCache")),
                    new CleanTarget(Path.Combine(p.data, @"Service Worker\CacheStorage")),
                })
            });

            g.Items.Add(new CleanItem
            {
                Group = browser, BrowserName = browser, ProcessNames = procs, DefaultChecked = false,
                Name = "Histórico de navegação",
                Description = "Sites visitados e downloads. Os favoritos NÃO são apagados.",
                Targets = () => profiles().SelectMany(p => new[]
                {
                    "History", "History-journal", "Visited Links", "Top Sites", "Top Sites-journal",
                    "Shortcuts", "Shortcuts-journal"
                }.Select(f => new CleanTarget(p.data, f, false)))
            });

            g.Items.Add(new CleanItem
            {
                Group = browser, BrowserName = browser, ProcessNames = procs, DefaultChecked = false,
                Name = "Cookies",
                Description = "Você será desconectado dos sites (logins salvos nos sites).",
                Targets = () => profiles().SelectMany(p => new[]
                {
                    new CleanTarget(Path.Combine(p.data, "Network"), "Cookies", false),
                    new CleanTarget(Path.Combine(p.data, "Network"), "Cookies-journal", false),
                    new CleanTarget(p.data, "Cookies", false),
                    new CleanTarget(p.data, "Cookies-journal", false),
                })
            });

            groups.Add(g);
        }

        // ------------------------------------------------------------------ Firefox
        private static IEnumerable<(string data, string cache)> FirefoxProfiles()
        {
            string root = Path.Combine(Roaming, @"Mozilla\Firefox\Profiles");
            foreach (var d in SafeDirs(root))
                yield return (d, Path.Combine(Local, @"Mozilla\Firefox\Profiles", Path.GetFileName(d)));
        }

        private static void AddFirefox(List<CleanGroup> groups)
        {
            if (!FirefoxProfiles().Any()) return;
            const string b = "Mozilla Firefox";
            var procs = new[] { "firefox" };
            var g = new CleanGroup { Name = b };

            g.Items.Add(new CleanItem
            {
                Group = b, BrowserName = b, ProcessNames = procs, DefaultChecked = true,
                Name = "Cache da internet",
                Description = "Imagens e arquivos de sites guardados para carregar mais rápido.",
                Targets = () => FirefoxProfiles().SelectMany(p => new[]
                {
                    new CleanTarget(Path.Combine(p.cache, "cache2")),
                    new CleanTarget(Path.Combine(p.cache, "startupCache")),
                    new CleanTarget(Path.Combine(p.cache, "thumbnails")),
                })
            });

            g.Items.Add(new CleanItem
            {
                Group = b, BrowserName = b, ProcessNames = procs, DefaultChecked = false,
                Name = "Cookies",
                Description = "Você será desconectado dos sites.",
                Targets = () => FirefoxProfiles().SelectMany(p => new[]
                {
                    new CleanTarget(p.data, "cookies.sqlite", false),
                    new CleanTarget(p.data, "cookies.sqlite-wal", false),
                    new CleanTarget(p.data, "cookies.sqlite-shm", false),
                })
            });

            g.Items.Add(new CleanItem
            {
                Group = b, BrowserName = b, ProcessNames = procs, DefaultChecked = false,
                Name = "Histórico de formulários",
                Description = "Textos digitados em campos de sites (autocompletar). O histórico de páginas do Firefox fica junto com os favoritos e por isso não é apagado.",
                Targets = () => FirefoxProfiles().SelectMany(p => new[]
                {
                    new CleanTarget(p.data, "formhistory.sqlite", false),
                })
            });

            groups.Add(g);
        }

        // ------------------------------------------------------------------ Navegadores abertos
        public static bool IsRunning(string[] procs)
        {
            foreach (var p in procs)
            {
                try
                {
                    var list = Process.GetProcessesByName(p);
                    bool any = list.Length > 0;
                    foreach (var x in list) x.Dispose();
                    if (any) return true;
                }
                catch { }
            }
            return false;
        }

        public static async Task CloseProcessesAsync(IEnumerable<string> procs)
        {
            var names = procs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var n in names)
                foreach (var p in Process.GetProcessesByName(n))
                {
                    try { p.CloseMainWindow(); } catch { }
                }

            await Task.Delay(3000);

            foreach (var n in names)
                foreach (var p in Process.GetProcessesByName(n))
                {
                    try { p.Kill(true); } catch { }
                }

            await Task.Delay(800);
        }
    }
}
