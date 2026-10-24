using System.ComponentModel;
using zFinancas.App.Pages;
using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App;

/// <summary>
/// Casca autenticada: menu lateral (flyout) + pilha de navegacao no detalhe.
/// A troca de secao recria o NavigationPage do detalhe: e previsivel e evita
/// manipulacao manual da pilha (que se mostrou instavel no Windows).
/// </summary>
public partial class AppShell : FlyoutPage
{
    private string _secaoAtual = Secoes.Dashboard;

    public AppShell()
    {
        InitializeComponent();

        Detail = CriarNavegacao(new DashboardPage());

        MenuPagina.ViewModel.SolicitouNavegacao += AoSolicitarNavegacao;

#if DEBUG
        // Atalho de teste: ZFINANCAS_MENU=extrato simula o clique no item do menu.
        var menuTeste = Environment.GetEnvironmentVariable("ZFINANCAS_MENU");
        if (!string.IsNullOrWhiteSpace(menuTeste))
        {
            Dispatcher.DispatchDelayed(
                TimeSpan.FromSeconds(3),
                () => MenuPagina.ViewModel.SelecionarPorRota(menuTeste));
        }
#endif
        PropertyChanged += AoMudarPropriedade;
    }

    /// <summary>Pilha de navegacao da secao atual.</summary>
    public NavigationPage Navegacao => (NavigationPage)Detail;

    public string SecaoAtual => _secaoAtual;

    /// <summary>Troca a secao exibida no detalhe.</summary>
    public Task NavegarParaAsync(Page pagina)
    {
        var painelAberto = IsPresented;
        IsPresented = false;

        _secaoAtual = pagina switch
        {
            DashboardPage => Secoes.Dashboard,
            LancamentosPage => Secoes.Lancamentos,
            ExtratoPage => Secoes.Extrato,
            ContasPage => Secoes.Contas,
            CentrosCustoPage => Secoes.CentrosCusto,
            UsuariosPage => Secoes.Usuarios,
            SincronizacaoPage => Secoes.Sincronizacao,
            ConfiguracoesPage => Secoes.Configuracoes,
            _ => _secaoAtual
        };
        // No Windows, trocar o Detail enquanto o painel do menu ainda está fechando é
        // simplesmente ignorado pela plataforma. Aplicamos a troca no próximo ciclo
        // (com uma pequena espera quando o menu estava aberto).
        void Aplicar()
        {
            Detail = CriarNavegacao(pagina);
            AtualizarMenu();
        }

        if (painelAberto)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), Aplicar);
        }
        else
        {
            Dispatcher.Dispatch(Aplicar);
        }

        return Task.CompletedTask;
    }

    private static NavigationPage CriarNavegacao(Page raiz) => new(raiz)
    {
        BarBackgroundColor = Paleta.Fundo,
        BarTextColor = Paleta.Texto
    };

    /// <summary>
    /// Recria o <c>Detail</c> com a página atual. Necessário no Windows: após um
    /// <c>PopAsync</c> o painel do FlyoutPage às vezes continua exibindo a página removida.
    /// </summary>
    public void ReancorarDetalhe()
    {
        if (Detail is NavigationPage navegacao && navegacao.CurrentPage is { } atual)
        {
            Detail = CriarNavegacao(atual);
        }
    }

    private void AtualizarMenu() => MenuPagina.ViewModel.Atualizar(_secaoAtual);

    private async void AoSolicitarNavegacao(object? sender, string secao)
    {
        IsPresented = false;

        try
        {
            var pagina = ServicoNavegacao.CriarPagina(secao);
            await NavegarParaAsync(pagina);
        }
        catch (Exception ex)
        {
            RegistroDeFalhas.Registrar(ex, "NavegarParaSecao");
        }
    }

    private void AoMudarPropriedade(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsPresented) && IsPresented)
        {
            AtualizarMenu();
        }
    }
}



