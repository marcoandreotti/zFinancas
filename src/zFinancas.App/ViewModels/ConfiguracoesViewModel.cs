using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Data.Infra;
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
    private readonly ServicoBackupBanco _backup;
    private readonly ServicoInicializacao _inicializacao;
    private readonly IArmazenamentoCompartilhado _armazenamento;

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
        ServicoPastaDados pastaDados,
        ServicoBackupBanco backup,
        ServicoInicializacao inicializacao,
        IArmazenamentoCompartilhado armazenamento)
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
        _backup = backup;
        _inicializacao = inicializacao;
        _armazenamento = armazenamento;

        Titulo = "Configurações";

        CarregarCommand = new ComandoAssincrono(CarregarAsync);
        AlternarBiometriaCommand = new ComandoAssincrono<bool>(AlternarBiometriaAsync);
        SairCommand = new ComandoAssincrono(SairAsync);
        EscolherPastaBancoCommand = new ComandoAssincrono(() => _pastaDados.EscolherPastaAsync());
        CriarBackupCommand = new ComandoAssincrono(CriarBackupAsync);
        RestaurarBackupCommand = new ComandoAssincrono(RestaurarBackupAsync);
    }

    public ObservableCollection<ContaCorrente> Contas { get; } = [];

    public ICommand CarregarCommand { get; }
    public ICommand AlternarBiometriaCommand { get; }
    public ICommand SairCommand { get; }

    /// <summary>Deixa o usuário escolher a pasta do banco (útil quando o padrão está bloqueado).</summary>
    public ICommand EscolherPastaBancoCommand { get; }

    public ICommand CriarBackupCommand { get; }

    public ICommand RestaurarBackupCommand { get; }

    /// <summary>Quando foi gerado o último backup neste aparelho.</summary>
    public string DataUltimoBackup
    {
        get
        {
            var texto = Preferences.Default.Get("backup.ultimo", string.Empty);

            return DateTime.TryParse(texto, out var quando)
                ? $"último backup: {Formatador.DataHora(quando)}"
                : "nenhum backup gerado neste aparelho";
        }
    }

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

    /// <summary>
    /// Liga/desliga a entrada por biometria para o usuário atual.
    /// Recebe o estado desejado (o do switch), em vez de alternar o atual: assim um
    /// estado que não pôde ser salvo não deixa o switch fora de sincronia.
    /// </summary>
    public async Task AlternarBiometriaAsync(bool ativar)
    {
        System.Diagnostics.Debug.WriteLine($"zFinancas: alternar biometria para {ativar}");

        if (!BiometriaDisponivel)
        {
            BiometriaHabilitada = false;
            MensagemBiometria = "Biometria indisponível neste aparelho.";
            await _dialogos.AlertaAsync("Biometria", "Este aparelho não oferece biometria disponível.");
            return;
        }

        if (!ativar)
        {
            await _preferencias.DesabilitarBiometriaAsync();
            BiometriaHabilitada = false;
            MensagemBiometria = "Entrada por biometria desativada.";
            return;
        }

        var resultado = await _biometria.AutenticarAsync("Confirme sua biometria para ativar a entrada rápida");

        if (resultado != ResultadoBiometria.Sucesso)
        {
            MensagemBiometria = resultado switch
            {
                ResultadoBiometria.Cancelado => "Ativação cancelada.",
                ResultadoBiometria.Indisponivel => "Biometria indisponível neste aparelho.",
                _ => "Biometria não reconhecida. Tente novamente."
            };

            BiometriaHabilitada = false;
            return;
        }

        if (_sessao.Usuario is null)
        {
            BiometriaHabilitada = false;
            return;
        }

        await _preferencias.HabilitarBiometriaAsync(_sessao.Usuario.Id);
        BiometriaHabilitada = true;
        MensagemBiometria = "Entrada por biometria ativada para este aparelho.";
    }

    private async Task CriarBackupAsync()
    {
        await ExecutarAsync(async () =>
        {
            var backup = await _backup.CriarAsync();
            var caminho = await _armazenamento.SalvarBinarioAsync(backup.NomeArquivo, backup.Conteudo);

            if (caminho is null)
            {
                return;
            }

            Preferences.Default.Set("backup.ultimo", DateTime.Now.ToString("o"));
            OnPropertyChanged(nameof(DataUltimoBackup));

            var tamanho = backup.Conteudo.Length / 1024.0;

            await _dialogos.AlertaAsync(
                "Backup criado",
                $"Banco completo com {tamanho:N1} KB.\n\n{caminho}");
        }, "Não foi possível criar o backup");
    }

    private async Task RestaurarBackupAsync()
    {
        try
        {
            var conteudo = await _armazenamento.EscolherArquivoBinarioAsync(".db");

            if (conteudo is null)
            {
                return;
            }

            var confirmar = await _dialogos.ConfirmarAsync(
                "Restaurar backup",
                "Os dados atuais serão substituídos pelos do arquivo escolhido. " +
                "Uma cópia de segurança do banco atual é guardada antes.",
                "Restaurar",
                "Cancelar");

            if (!confirmar)
            {
                return;
            }

            await ExecutarAsync(async () =>
            {
                var seguranca = await _backup.RestaurarAsync(conteudo);

                _inicializacao.Reiniciar();

                await _dialogos.AlertaAsync(
                    "Backup restaurado",
                    "Os dados foram restaurados. Você voltará para a tela de entrada." +
                    $"\n\nCópia do banco anterior:\n{seguranca}");

                _autenticacao.Sair();
                await _navegacao.AbrirLoginAsync();
            }, "Não foi possível restaurar o backup");
        }
        catch (Core.Dominio.Excecoes.DominioException ex)
        {
            await _dialogos.AlertaAsync("Restaurar backup", ex.Message);
        }
    }

    private async Task SairAsync()
    {
        var confirmar = await _dialogos.ConfirmarAsync("Sair", "Deseja encerrar a sessão?", "Sair", "Continuar");
        if (!confirmar) return;

        _autenticacao.Sair();
        await _navegacao.AbrirLoginAsync();
    }
}
