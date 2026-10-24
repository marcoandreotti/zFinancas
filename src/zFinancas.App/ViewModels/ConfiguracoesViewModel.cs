using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;

namespace zFinancas.App.ViewModels;

/// <summary>Configurações do aparelho e da sessão (biometria, conta ativa, sobre).</summary>
public sealed class ConfiguracoesViewModel : BaseViewModel
{
    private readonly ISessaoAtual _sessao;
    private readonly ContaCorrenteService _contas;
    private readonly AutenticacaoService _autenticacao;
    private readonly ServicoPreferencias _preferencias;
    private readonly IServicoBiometria _biometria;
    private readonly ServicoNavegacao _navegacao;
    private readonly ServicoDialogos _dialogos;
    private readonly IInfoDispositivo _dispositivo;
    private readonly CaminhoBanco _caminhoBanco;
    private readonly ServicoPastaDados _pastaDados;

    private ContaCorrente? _contaAtiva;
    private bool _biometriaHabilitada;
    private bool _biometriaDisponivel;
    private string _mensagemBiometria = string.Empty;

    public ConfiguracoesViewModel(
        ISessaoAtual sessao,
        ContaCorrenteService contas,
        AutenticacaoService autenticacao,
        ServicoPreferencias preferencias,
        IServicoBiometria biometria,
        ServicoNavegacao navegacao,
        ServicoDialogos dialogos,
        IInfoDispositivo dispositivo,
        CaminhoBanco caminhoBanco,
        ServicoPastaDados pastaDados)
    {
        _sessao = sessao;
        _contas = contas;
        _autenticacao = autenticacao;
        _preferencias = preferencias;
        _biometria = biometria;
        _navegacao = navegacao;
        _dialogos = dialogos;
        _dispositivo = dispositivo;
        _caminhoBanco = caminhoBanco;
        _pastaDados = pastaDados;

        Titulo = "Configurações";

        CarregarCommand = new ComandoAssincrono(CarregarAsync);
        AlternarBiometriaCommand = new ComandoAssincrono(AlternarBiometriaAsync);
        SairCommand = new ComandoAssincrono(SairAsync);
        EscolherPastaBancoCommand = new ComandoAssincrono(() => _pastaDados.EscolherPastaAsync());
    }

    public ObservableCollection<ContaCorrente> Contas { get; } = [];

    public ICommand CarregarCommand { get; }
    public ICommand AlternarBiometriaCommand { get; }
    public ICommand SairCommand { get; }

    /// <summary>Deixa o usuário escolher a pasta do banco (útil quando o padrão está bloqueado).</summary>
    public ICommand EscolherPastaBancoCommand { get; }

    public string NomeUsuario => _sessao.Usuario?.Nome ?? "-";

    public string PerfilUsuario => _sessao.Usuario?.Perfil.Descrever() ?? "-";

    public string Iniciais => Formatador.Iniciais(_sessao.Usuario?.Nome);

    public string ContasVinculadas => _sessao.EhAdministrador
        ? "Administrador: acesso a todas as contas ativas."
        : $"{_sessao.ContasPermitidas.Count} conta(s) vinculada(s).";

    public ContaCorrente? ContaAtiva
    {
        get => _contaAtiva;
        set
        {
            if (!SetProperty(ref _contaAtiva, value) || value is null) return;

            _sessao.DefinirContaAtiva(value.Id);
            OnPropertyChanged(nameof(ContaAtivaDescricao));
        }
    }

    public string ContaAtivaDescricao => ContaAtiva is null
        ? "Nenhuma conta selecionada"
        : $"{ContaAtiva.Icone} {ContaAtiva.Descricao}";

    public bool BiometriaDisponivel => _biometriaDisponivel && _dispositivo.EhMovel;

    public bool BiometriaHabilitada
    {
        get => _biometriaHabilitada;
        private set => SetProperty(ref _biometriaHabilitada, value);
    }

    public string MensagemBiometria
    {
        get => _mensagemBiometria;
        private set => SetProperty(ref _mensagemBiometria, value);
    }

    public string Dispositivo => $"{_dispositivo.Nome} • {_dispositivo.Plataforma}";

    public string Versao => "zFinancas 1.0 • .NET MAUI • SQLite local";

    /// <summary>Caminho real do arquivo — o primeiro lugar a olhar em caso de erro de acesso.</summary>
    public string CaminhoBanco => _caminhoBanco.Caminho;

    /// <summary>Aviso quando o app precisou usar uma pasta alternativa para o banco.</summary>
    public string AvisoBanco => _caminhoBanco.Aviso ?? string.Empty;

    public bool TemAvisoBanco => _caminhoBanco.UsouLocalAlternativo;

    public async Task CarregarAsync()
    {
        await ExecutarAsync(async () =>
        {
            var contas = await _contas.ListarAsync();
            var permitidas = _sessao.ContasPermitidas.Select(c => c.Id).ToHashSet();

            Contas.Clear();
            foreach (var conta in contas.Where(c => permitidas.Contains(c.Id)))
            {
                Contas.Add(conta);
            }

            _contaAtiva = Contas.FirstOrDefault(c => c.Id == _sessao.ContaAtivaId) ?? Contas.FirstOrDefault();
            OnPropertyChanged(nameof(ContaAtiva));
            OnPropertyChanged(nameof(ContaAtivaDescricao));
            OnPropertyChanged(nameof(NomeUsuario));
            OnPropertyChanged(nameof(PerfilUsuario));
            OnPropertyChanged(nameof(Iniciais));
            OnPropertyChanged(nameof(ContasVinculadas));

            _biometriaDisponivel = await _biometria.DisponivelAsync();
            OnPropertyChanged(nameof(BiometriaDisponivel));

            var usuarioBiometria = await _preferencias.ObterUsuarioComBiometriaAsync();
            BiometriaHabilitada = usuarioBiometria == _sessao.Usuario?.Id;

            MensagemBiometria = !BiometriaDisponivel
                ? "A entrada por biometria está disponível apenas no celular, com digital ou rosto cadastrado."
                : BiometriaHabilitada
                    ? "Ao abrir o app, você poderá entrar com biometria neste aparelho."
                    : "Ative para entrar com digital/rosto neste aparelho.";
        }, "Não foi possível carregar as configurações");
    }

    private async Task AlternarBiometriaAsync()
    {
        if (!BiometriaDisponivel)
        {
            await _dialogos.AlertaAsync("Biometria", "Este aparelho não oferece biometria disponível.");
            return;
        }

        if (BiometriaHabilitada)
        {
            await _preferencias.DesabilitarBiometriaAsync();
            BiometriaHabilitada = false;
            MensagemBiometria = "Entrada por biometria desativada.";
            return;
        }

        var autorizado = await _biometria.AutenticarAsync("Confirme sua biometria para ativar a entrada rápida");
        if (!autorizado)
        {
            await _dialogos.AlertaAsync("Biometria", "Não foi possível confirmar sua biometria.");
            return;
        }

        if (_sessao.Usuario is null) return;

        await _preferencias.HabilitarBiometriaAsync(_sessao.Usuario.Id);
        BiometriaHabilitada = true;
        MensagemBiometria = "Entrada por biometria ativada para este aparelho.";
    }

    private async Task SairAsync()
    {
        var confirmar = await _dialogos.ConfirmarAsync("Sair", "Deseja encerrar a sessão?", "Sair", "Continuar");
        if (!confirmar) return;

        _autenticacao.Sair();
        await _navegacao.AbrirLoginAsync();
    }
}
