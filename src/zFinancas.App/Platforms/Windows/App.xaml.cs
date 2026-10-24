using Microsoft.UI.Xaml;
using zFinancas.App.Servicos;

// Projeto WinUI do zFinancas: captura falhas não tratadas em um arquivo local.
namespace zFinancas.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            RegistroDeFalhas.Registrar(e.Exception, "WinUI.UnhandledException");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            RegistroDeFalhas.Registrar(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            RegistroDeFalhas.Registrar(e.Exception, "TaskScheduler.UnobservedTaskException");
        };
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
