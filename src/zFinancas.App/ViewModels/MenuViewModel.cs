using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Enums;

namespace zFinancas.App.ViewModels;

/// <summary>Menu lateral (flyout) do app autenticado.</summary>
public sealed class MenuViewModel : BaseViewModel
{
    private readonly ISessaoAtual _sessao;
    private readonly AutenticacaoService _autenticacao;
    private readonly ServicoNavegacao _navegacao;
    private readonly ServicoDialogos _dialogos;
    private readonly IInfoDispositivo _dispositivo;

    private string _nomeUsuario = string.Empty;
    private string _perfilDescricao = string.Empty;
    private string _iniciais = "?";
    private string _detalheSessao = string.Empty;

    public MenuViewModel(
        ISessaoAtual sessao,
        AutenticacaoService autenticacao,
        ServicoNavegacao navegacao,
        ServicoDialogos dialogos,
        IInfoDispositivo dispositivo)
    {
        _sessao = sessao;
        _autenticacao = autenticacao;
        _navegacao = navegacao;
        _dialogos = dialogos;
        _dispositivo = dispositivo;

        NavegarCommand = new Comando<ItemMenu>(item =>
        {
            SolicitarNavegacao(item);
            return Task.CompletedTask;
        });

        SairCommand = new ComandoAssincrono(SairAsync);
    }

    /// <summary>Disparado quando o usuário escolhe uma seção.</summary>
    public event EventHandler<string>? SolicitouNavegacao;

    public ObservableCollection<ItemMenu> Itens { get; } = [];

    public ICommand NavegarCommand { get; }

    public ICommand SairCommand { get; }

    public string NomeUsuario
    {
        get => _nomeUsuario;
        private set => SetProperty(ref _nomeUsuario, value);
    }

    public string PerfilDescricao
    {
        get => _perfilDescricao;
        private set => SetProperty(ref _perfilDescricao, value);
    }

    public string Iniciais
    {
        get => _iniciais;
        private set => SetProperty(ref _iniciais, value);
    }

    public string DetalheSessao
    {
        get => _detalheSessao;
        private set => SetProperty(ref _detalheSessao, value);
    }

    public string Versao => $"zFinancas 1.0 • {_dispositivo.Plataforma}";

    /// <summary>Recarrega o menu conforme a sessão e destaca a seção atual.</summary>
    public void Atualizar(string secaoAtual = Secoes.Dashboard)
    {
        var usuario = _sessao.Usuario;

        NomeUsuario = usuario?.Nome ?? "Visitante";
        Iniciais = Formatador.Iniciais(usuario?.Nome);
        PerfilDescricao = usuario?.Perfil.Descrever() ?? "-";

        var contaAtiva = _sessao.ContasPermitidas.FirstOrDefault(c => c.Id == _sessao.ContaAtivaId);
        DetalheSessao = contaAtiva is not null
            ? $"{contaAtiva.Icone} {contaAtiva.Descricao} • {_sessao.ContasPermitidas.Count} conta(s)"
            : $"{_sessao.ContasPermitidas.Count} conta(s) disponível(is)";

        Itens.Clear();
        Adicionar("Dashboard", "🏠", Secoes.Dashboard, secaoAtual);
        Adicionar("Lançamentos", "🧾", Secoes.Lancamentos, secaoAtual);
        Adicionar("Extrato", "📊", Secoes.Extrato, secaoAtual);
        Adicionar("Contas correntes", "💳", Secoes.Contas, secaoAtual);
        Adicionar("Centros de custo", "🏷️", Secoes.CentrosCusto, secaoAtual);

        if (_sessao.EhAdministrador)
        {
            Adicionar("Usuários", "👥", Secoes.Usuarios, secaoAtual);
        }

        Adicionar("Sincronização", "🔄", Secoes.Sincronizacao, secaoAtual);
        Adicionar("Configurações", "⚙️", Secoes.Configuracoes, secaoAtual);
    }

    public void Selecionar(ItemMenu? item) => SolicitarNavegacao(item);

    /// <summary>Seleciona pela rota (usado em teste automatizado/atalho de debug).</summary>
    public void SelecionarPorRota(string? rota) =>
        SolicitarNavegacao(Itens.FirstOrDefault(i => string.Equals(i.Rota, rota, StringComparison.OrdinalIgnoreCase)));

    private void Adicionar(string titulo, string icone, string rota, string secaoAtual)
    {
        Itens.Add(new ItemMenu
        {
            Titulo = titulo,
            Icone = icone,
            Rota = rota,
            Selecionado = string.Equals(rota, secaoAtual, StringComparison.Ordinal)
        });
    }

    private void SolicitarNavegacao(ItemMenu? item)
    {
        if (item is null) return;
        SolicitouNavegacao?.Invoke(this, item.Rota);
    }

    private async Task SairAsync()
    {
        var confirmar = await _dialogos.ConfirmarAsync("Sair", "Deseja encerrar a sessão?", "Sair", "Continuar");
        if (!confirmar) return;

        _autenticacao.Sair();
        await _navegacao.AbrirLoginAsync();
    }
}
