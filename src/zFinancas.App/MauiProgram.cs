using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Servicos;
using zFinancas.Core.Dominio.Voz;
using zFinancas.Core.Seguranca;
using zFinancas.Data.Infra;
using zFinancas.Data.Repositorios;

namespace zFinancas.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        RegistrarServicos(builder.Services);

        var app = builder.Build();
        ServicosApp.Inicializar(app.Services);

        return app;
    }

    /// <summary>
    /// Composição do app. Tudo é resolvido aqui é nenhuma camada cria suas dependências
    /// por conta própria (inversão de dependência aplicada de ponta a ponta).
    /// </summary>
    private static void RegistrarServicos(IServiceCollection services)
    {
        // ---------------------------------------------------------- persistência
        services.AddSingleton<CaminhoBanco>();
        services.AddSingleton<zFinancas.Data.Infra.ServicoBackupBanco>();
        services.AddSingleton(sp => new ContextoBanco(sp.GetRequiredService<CaminhoBanco>().Caminho));
        services.AddSingleton<MigradorBanco>();
        services.AddSingleton<ServicoInicializacao>();

        services.AddSingleton<IUsuarioRepositorio, UsuarioRepositorio>();
        services.AddSingleton<IContaCorrenteRepositorio, ContaCorrenteRepositorio>();
        services.AddSingleton<ICentroCustoRepositorio, CentroCustoRepositorio>();
        services.AddSingleton<IUsuarioContaCorrenteRepositorio, UsuarioContaCorrenteRepositorio>();
        services.AddSingleton<ILancamentoRepositorio, LancamentoRepositorio>();
        services.AddSingleton<IConfiguracaoRepositorio, ConfiguracaoRepositorio>();

        services.AddSingleton<IUnidadeDeTrabalho>(sp =>
            new UnidadeDeTrabalho(sp.GetRequiredService<ContextoBanco>()));
        services.AddSingleton<IRelogio, RelogioSistema>();
        services.AddSingleton<ISerializadorJson, SerializadorJson>();

        // ------------------------------------------------------- domínio/aplicação
        services.AddSingleton<SenhaHasher>();
        services.AddSingleton<GeradorDeParcelas>();
        services.AddSingleton<CalculadoraDeSaldo>();
        services.AddSingleton<PoliticaDeQuitacao>();

        services.AddSingleton<SessaoAtual>();
        services.AddSingleton<ISessaoAtual>(sp => sp.GetRequiredService<SessaoAtual>());

        services.AddSingleton<PrimeiroAcessoService>();
        services.AddSingleton<AutenticacaoService>();
        services.AddSingleton<UsuarioService>();
        services.AddSingleton<ContaCorrenteService>();
        services.AddSingleton<CentroCustoService>();
        services.AddSingleton<LancamentoService>();
        services.AddSingleton<ExtratoService>();
        services.AddSingleton<DashboardService>();
        services.AddSingleton<SincronizacaoService>();

        // ---------------------------------------------------------- infra do app
        services.AddSingleton<IInfoDispositivo, InfoDispositivo>();
        services.AddSingleton<IArmazenamentoCompartilhado, ArmazenamentoCompartilhado>();
        services.AddSingleton<ServicoNavegacao>();
        services.AddSingleton<ServicoDialogos>();
        services.AddSingleton<ServicoPreferencias>();
        services.AddSingleton<ServicoPastaDados>();

        // ----------------------------------------------------- voz (speech-to-text)
        services.AddSingleton<InterpretadorDeVoz>();
        services.AddSingleton<IServicoVoz, ServicoVoz>();
        services.AddSingleton<ServicoLancamentoPorVoz>();

#if ANDROID
        services.AddSingleton<IServicoBiometria, Plataformas.ServicoBiometriaAndroid>();
#else
        services.AddSingleton<IServicoBiometria, ServicoBiometriaIndisponivel>();
#endif

        // View models por convenção (transientes: cada tela tem o seu).
        foreach (var tipo in typeof(MauiProgram).Assembly.GetTypes())
        {
            if (tipo is { IsClass: true, IsAbstract: false } && tipo.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            {
                services.AddTransient(tipo);
            }
        }
    }

}
