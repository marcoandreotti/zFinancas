using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.App.ViewModels;

/// <summary>Conta corrente marcável na tela de usuário (vínculo USUARIOCONTACORRENTE).</summary>
public sealed class ContaSelecionavel : BaseViewModel
{
    private bool _selecionada;

    public long Id { get; init; }
    public string Descricao { get; init; }
    public string Icone { get; init; }
    public string CorHex { get; init; }
    public bool Ativa { get; init; }

    public bool Selecionada
    {
        get => _selecionada;
        set => SetProperty(ref _selecionada, value);
    }

    public Color Cor => Paleta.DeHex(CorHex);
    public string SituacaoConta => Ativa ? "Ativa" : "Inativa";
}

/// <summary>Cadastro/edição de usuário com seleção das contas vinculadas.</summary>
public sealed class UsuarioEdicaoViewModel : BaseViewModel
{
    private readonly UsuarioService _usuarios;
    private readonly ContaCorrenteService _contas;
    private readonly ISessaoAtual _sessao;
    private readonly ServicoNavegacao _navegacao;
    private readonly ServicoDialogos _dialogos;

    private long? _usuarioId;
    private string _nome = string.Empty;
    private string _senha = string.Empty;
    private string _confirmacao = string.Empty;
    private string _dica = string.Empty;
    private bool _ativo = true;
    private bool _ehAdministrador;
    private bool _senhaMascarada = true;
    private bool _mostraSenha;

    public UsuarioEdicaoViewModel(
        UsuarioService usuarios,
        ContaCorrenteService contas,
        ISessaoAtual sessao,
        ServicoNavegacao navegacao,
        ServicoDialogos dialogos)
    {
        _usuarios = usuarios;
        _contas = contas;
        _sessao = sessao;
        _navegacao = navegacao;
        _dialogos = dialogos;

        Titulo = "Novo usuário";

        SalvarCommand = new ComandoAssincrono(SalvarAsync);
        CancelarCommand = new ComandoAssincrono(() => _navegacao.PopAsync());
        AlternarSenhaCommand = new Comando(() => SenhaMascarada = !SenhaMascarada);
    }

    public ObservableCollection<ContaSelecionavel> Contas { get; } = [];

    public ICommand SalvarCommand { get; }
    public ICommand CancelarCommand { get; }
    public ICommand AlternarSenhaCommand { get; }

    public string Nome
    {
        get => _nome;
        set => SetProperty(ref _nome, value);
    }

    public string Senha
    {
        get => _senha;
        set => SetProperty(ref _senha, value);
    }

    public string Confirmacao
    {
        get => _confirmacao;
        set => SetProperty(ref _confirmacao, value);
    }

    public string Dica
    {
        get => _dica;
        set => SetProperty(ref _dica, value);
    }

    public bool Ativo
    {
        get => _ativo;
        set => SetProperty(ref _ativo, value);
    }

    public bool EhAdministrador
    {
        get => _ehAdministrador;
        set
        {
            if (!SetProperty(ref _ehAdministrador, value)) return;

            OnPropertyChanged(nameof(EhUsuarioComum));
            OnPropertyChanged(nameof(MostraContas));
        }
    }

    public bool EhUsuarioComum
    {
        get => !_ehAdministrador;
        set => EhAdministrador = !value;
    }

    /// <summary>ADM acessa todas as contas; o vínculo só faz sentido para USR.</summary>
    public bool MostraContas => !EhAdministrador;

    public bool SenhaMascarada
    {
        get => _senhaMascarada;
        set => SetProperty(ref _senhaMascarada, value);
    }

    public bool MostraSenha
    {
        get => _mostraSenha;
        private set => SetProperty(ref _mostraSenha, value);
    }

    public string RotuloSenha => _usuarioId is null ? "SENHA" : "NOVA SENHA (OPCIONAL)";

    public string ExplicacaoPerfil =>
        "Administrador acessa todas as contas e gerencia usuários. " +
        "Usuário acessa somente as contas vinculadas abaixo.";

    public void Preparar(long? usuarioId)
    {
        _usuarioId = usuarioId;
        _ = InicializarAsync(usuarioId);
    }

    private async Task InicializarAsync(long? usuarioId)
    {
        await ExecutarAsync(async () =>
        {
            var contas = await _contas.ListarAsync(incluirInativas: true);

            var vinculadas = usuarioId is null
                ? []
                : (await _usuarios.ObterAsync(usuarioId.Value))?.ContasIds ?? [];

            Contas.Clear();
            foreach (var conta in contas)
            {
                Contas.Add(new ContaSelecionavel
                {
                    Id = conta.Id,
                    Descricao = conta.Descricao,
                    Icone = conta.Icone,
                    CorHex = conta.CorHex,
                    Ativa = conta.Ativo,
                    Selecionada = vinculadas.Contains(conta.Id)
                });
            }

            if (usuarioId is null)
            {
                Titulo = "Novo usuário";
                EhAdministrador = false;
                Ativo = true;
                MostraSenha = true;
                OnPropertyChanged(nameof(RotuloSenha));
                return;
            }

            var resumo = await _usuarios.ObterAsync(usuarioId.Value)
                         ?? throw new DominioException("Usuário não encontrado.");

            Titulo = "Editar usuário";
            Nome = resumo.Usuario.Nome;
            Dica = resumo.Usuario.DicaSenha ?? string.Empty;
            Ativo = resumo.Usuario.Ativo;
            EhAdministrador = resumo.Usuario.EhAdministrador;
            MostraSenha = true;

            OnPropertyChanged(nameof(RotuloSenha));
            OnPropertyChanged(nameof(MostraContas));
        }, "Não foi possível abrir o usuário");
    }

    private async Task SalvarAsync()
    {
        await ExecutarAsync(async () =>
        {
            if (_usuarioId is null)
            {
                if (string.IsNullOrWhiteSpace(Senha) || Senha != Confirmacao)
                {
                    throw new DominioException("Informe e confirme a senha do novo usuário.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(Senha) && Senha != Confirmacao)
            {
                throw new DominioException("A confirmação da nova senha não confere.");
            }

            var contasIds = Contas.Where(c => c.Selecionada).Select(c => c.Id).ToList();

            var requisicao = new RequisicaoUsuario(
                _usuarioId ?? 0,
                Nome,
                string.IsNullOrWhiteSpace(Senha) ? null : Senha,
                Dica,
                EhAdministrador ? PerfilUsuario.Administrador : PerfilUsuario.Usuario,
                Ativo,
                contasIds);

            if (_usuarioId is null)
            {
                var (_, codigo) = await _usuarios.CriarAsync(requisicao);

                await _dialogos.MostrarCodigoAsync(
                    "Usuário criado",
                    codigo,
                    $"Entregue este código de recuperação para {Nome}. Ele não será exibido novamente.");
            }
            else
            {
                await _usuarios.AtualizarAsync(requisicao);
            }

            await _navegacao.PopAsync();
        }, "Não foi possível salvar o usuário");
    }
}
