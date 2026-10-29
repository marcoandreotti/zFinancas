namespace zFinancas.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // O zFinancas tem identidade visual escura por decisão de design:
        // o mesmo tema no Windows e no Android evita telas "meio claras" nos controles nativos.
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var janela = new Window(Servicos.ServicoNavegacao.CriarCascaDeAbertura())
        {
            Title = "zFinancas"
        };

#if WINDOWS
        janela.Width = 1280;
        janela.Height = 880;
        janela.MinimumWidth = 960;
        janela.MinimumHeight = 700;
#endif

        return janela;
    }
}
