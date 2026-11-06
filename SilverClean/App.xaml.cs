using System.Windows;
using SilverClean.Core;

namespace SilverClean
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, ex) =>
            {
                Log.Write("ERRO: " + ex.Exception);
                MessageBox.Show("Ocorreu um erro inesperado:\n\n" + ex.Exception.Message +
                                "\n\nOs detalhes foram gravados no log.", "SilverClean",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
            Log.Write("SilverClean iniciado");
            base.OnStartup(e);
        }
    }
}
