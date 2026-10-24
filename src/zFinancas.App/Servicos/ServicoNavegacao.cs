using zFinancas.App.ViewModels;

namespace zFinancas.App.Servicos;

/// <summary>Secoes do app (itens do menu lateral).</summary>
public static class Secoes
{
    public const string Dashboard = "dashboard";
    public const string Lancamentos = "lancamentos";
    public const string Extrato = "extrato";
    public const string Contas = "contas";
    public const string CentrosCusto = "centros-custo";
    public const string Usuarios = "usuarios";
    public const string Sincronizacao = "sincronizacao";
    public const string Configuracoes = "configuracoes";
}

/// <summary>
/// Navegacao do app sobre FlyoutPage + NavigationPage.
/// O Shell do MAUI nao se comporta bem com varias secoes no Windows
/// ("Pending Navigations still processing"); com FlyoutPage o menu e 100% nosso.
/// </summary>
public sealed class ServicoNavegacao
{
    private static Window? Janela => Application.Current?.Windows.FirstOrDefault();

    /// <summary>Abre a area autenticada; a troca de raiz vai para o proximo ciclo da UI.</summary>
    public Task AbrirAplicacaoAsync()
    {
        TrocarRaiz(() => new AppShell());
        return Task.CompletedTask;
    }

    /// <summary>Volta para a tela de entrada.</summary>
    public Task AbrirLoginAsync()
    {
        TrocarRaiz(CriarCascaDeEntrada);
        return Task.CompletedTask;
    }

    /// <summary>Casca inicial: abertura animada (sem barra de navegação).</summary>
    public static Page CriarCascaDeAbertura()
    {
        var abertura = new Pages.AberturaPage();
        NavigationPage.SetHasNavigationBar(abertura, false);

        return new NavigationPage(abertura)
        {
            BarBackgroundColor = Paleta.Fundo,
            BarTextColor = Paleta.Texto
        };
    }

    /// <summary>Casca inicial: apenas navegacao de entrada (sem menu lateral).</summary>
    public static Page CriarCascaDeEntrada()
    {
        var login = new Pages.LoginPage();
        NavigationPage.SetHasNavigationBar(login, false);

        return new NavigationPage(login)
        {
            BarBackgroundColor = Paleta.Fundo,
            BarTextColor = Paleta.Texto
        };
    }

    /// <summary>Troca a secao exibida no menu lateral.</summary>
    public Task IrParaSecaoAsync(string secao) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (Janela?.Page is not AppShell casca)
        {
            return;
        }

        var pagina = CriarPagina(secao);
        await casca.NavegarParaAsync(pagina);
    });

    /// <summary>Empilha uma pagina de detalhe/edicao.</summary>
    public Task PushAsync(Page pagina) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var navegacao = ObterNavegacao();
        if (navegacao is not null)
        {
            await navegacao.PushAsync(pagina);
        }
    });

    public Task PopAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var navegacao = ObterNavegacao();
        if (navegacao is not null && navegacao.Navigation.NavigationStack.Count > 1)
        {
            await navegacao.PopAsync();
        }
    });

    public Task VoltarAsync() => PopAsync();

    public Task AbrirPrimeiroAcessoAsync() => PushAsync(new Pages.PrimeiroAcessoPage());

    public Task AbrirRecuperacaoSenhaAsync() => PushAsync(new Pages.RecuperarSenhaPage());

    /// <summary>
    /// Abre o formulário de lançamento. Com <paramref name="rascunho"/> (vindo do microfone),
    /// a tela já abre preenchida para o usuário revisar e salvar.
    /// </summary>
    public Task AbrirLancamentoAsync(long? lancamentoId = null, Core.Dominio.Voz.LancamentoFalado? rascunho = null)
    {
        var pagina = new Pages.LancamentoEdicaoPage();
        pagina.ViewModel.Preparar(lancamentoId, rascunho);
        return PushAsync(pagina);
    }

    public Task AbrirUsuarioAsync(long? usuarioId = null)
    {
        var pagina = new Pages.UsuarioEdicaoPage();
        pagina.ViewModel.Preparar(usuarioId);
        return PushAsync(pagina);
    }

    public Task FecharMenuAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        if (Janela?.Page is FlyoutPage flyout)
        {
            flyout.IsPresented = false;
        }
    });

    private static void TrocarRaiz(Func<Page> criar)
    {
        var janela = Janela;
        if (janela is null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                janela.Page = criar();
            }
            catch (Exception ex)
            {
                RegistroDeFalhas.Registrar(ex, "TrocarRaiz");
            }
        });
    }

    private static NavigationPage? ObterNavegacao() => Janela?.Page switch
    {
        AppShell casca => casca.Navegacao,
        NavigationPage navegacao => navegacao,
        _ => null
    };

    /// <summary>Instancia a pagina de uma secao do menu.</summary>
    public static Page CriarPagina(string secao) => secao switch
    {
        Secoes.Dashboard => new Pages.DashboardPage(),
        Secoes.Lancamentos => new Pages.LancamentosPage(),
        Secoes.Extrato => new Pages.ExtratoPage(),
        Secoes.Contas => new Pages.ContasPage(),
        Secoes.CentrosCusto => new Pages.CentrosCustoPage(),
        Secoes.Usuarios => new Pages.UsuariosPage(),
        Secoes.Sincronizacao => new Pages.SincronizacaoPage(),
        Secoes.Configuracoes => new Pages.ConfiguracoesPage(),
        _ => new Pages.DashboardPage()
    };
}



