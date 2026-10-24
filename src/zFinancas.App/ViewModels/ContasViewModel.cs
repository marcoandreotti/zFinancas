using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.App.ViewModels;

/// <summary>Cadastro de contas correntes (lista + formulário inline).</summary>
public sealed class ContasViewModel : BaseViewModel
{
    private readonly ContaCorrenteService _servico;
    private readonly ISessaoAtual _sessao;
    private readonly ServicoDialogos _dialogos;

    private ContaVm? _contaEmEdicao;
    private bool _formularioVisivel;
    private string _descricao = string.Empty;
    private string _icone = "🏦";
    private string _corHex = ContaCorrente.PaletaCores[0];
    private bool _principal;
    private bool _ativa = true;

    public ContasViewModel(ContaCorrenteService servico, ISessaoAtual sessao, ServicoDialogos dialogos)
    {
        _servico = servico;
        _sessao = sessao;
        _dialogos = dialogos;

        Titulo = "Contas correntes";

        CarregarCommand = new ComandoAssincrono(CarregarAsync);
        NovaCommand = new Comando(() => AbrirFormulario(null));
        EditarCommand = new ComandoAssincrono<ContaVm>(item =>
        {
            AbrirFormulario(item);
            return Task.CompletedTask;
        });
        SalvarCommand = new ComandoAssincrono(SalvarAsync);
        CancelarCommand = new Comando(() => FormularioVisivel = false);
        AlternarAtivoCommand = new ComandoAssincrono<ContaVm>(AlternarAtivoAsync);
        AcoesCommand = new ComandoAssincrono<ContaVm>(ExecutarAcoesAsync);
        DefinirPrincipalCommand = new ComandoAssincrono<ContaVm>(DefinirPrincipalAsync);

        Cores = new ObservableCollection<CorOpcao>(ContaCorrente.PaletaCores.Select(hex => new CorOpcao(hex)));
        Icones = new ObservableCollection<string>(["🏦", "💳", "🐖", "💰", "📈", "🏠", "🚗", "✈️", "🎯", "💼"]);
    }

    public ObservableCollection<ContaVm> Itens { get; } = [];

    public ObservableCollection<CorOpcao> Cores { get; }

    public ObservableCollection<string> Icones { get; }

    public ICommand CarregarCommand { get; }
    public ICommand NovaCommand { get; }
    public ICommand EditarCommand { get; }
    public ICommand SalvarCommand { get; }
    public ICommand CancelarCommand { get; }
    public ICommand AlternarAtivoCommand { get; }
    public ICommand DefinirPrincipalCommand { get; }
    public ICommand AcoesCommand { get; }

    public bool FormularioVisivel
    {
        get => _formularioVisivel;
        private set => SetProperty(ref _formularioVisivel, value);
    }

    public string TituloFormulario => _contaEmEdicao is null ? "Nova conta corrente" : "Editar conta corrente";

    public string Descricao
    {
        get => _descricao;
        set => SetProperty(ref _descricao, value);
    }

    public string Icone
    {
        get => _icone;
        set => SetProperty(ref _icone, value);
    }

    public string CorHex
    {
        get => _corHex;
        set
        {
            if (SetProperty(ref _corHex, value)) OnPropertyChanged(nameof(CorPreview));
        }
    }

    public Color CorPreview => Paleta.DeHex(CorHex);

    /// <summary>Seleciona a cor pelo chip do formulário.</summary>
    public void DefinirCor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex)) CorHex = hex;
    }

    public bool Principal
    {
        get => _principal;
        set => SetProperty(ref _principal, value);
    }

    public bool Ativa
    {
        get => _ativa;
        set => SetProperty(ref _ativa, value);
    }

    public bool SemItens => Itens.Count == 0;

    public async Task CarregarAsync()
    {
        await ExecutarAsync(async () =>
        {
            var contas = await _servico.ListarAsync(incluirInativas: true);

            Itens.Clear();
            foreach (var conta in contas)
            {
                Itens.Add(new ContaVm(conta.Id, conta.Descricao, conta.Icone, conta.CorHex, conta.Principal, conta.Ativo));
            }

            OnPropertyChanged(nameof(SemItens));
        }, "Não foi possível carregar as contas");
    }

    private void AbrirFormulario(ContaVm? conta)
    {
        _contaEmEdicao = conta;
        Descricao = conta?.Descricao ?? string.Empty;
        Icone = conta?.Icone ?? "🏦";
        CorHex = conta?.CorHex ?? ContaCorrente.PaletaCores[0];
        Principal = conta?.Principal ?? false;
        Ativa = conta?.Ativo ?? true;

        OnPropertyChanged(nameof(TituloFormulario));
        FormularioVisivel = true;
    }

    private async Task SalvarAsync()
    {
        await ExecutarAsync(async () =>
        {
            var requisicao = new RequisicaoConta(
                _contaEmEdicao?.Id ?? 0,
                Descricao,
                CorHex,
                Icone,
                Principal,
                Ativa);

            if (_contaEmEdicao is null)
            {
                await _servico.CriarAsync(requisicao);
            }
            else
            {
                await _servico.AtualizarAsync(requisicao);
            }

            FormularioVisivel = false;
            await CarregarAsync();
        }, "Não foi possível salvar a conta");
    }

    private async Task AlternarAtivoAsync(ContaVm? conta)
    {
        if (conta is null) return;

        await ExecutarAsync(async () =>
        {
            await _servico.AlterarAtivoAsync(conta.Id, !conta.Ativo);
            await CarregarAsync();
        }, "Não foi possível alterar a conta");
    }

    private async Task DefinirPrincipalAsync(ContaVm? conta)
    {
        if (conta is null) return;

        await ExecutarAsync(async () =>
        {
            await _servico.DefinirPrincipalAsync(conta.Id);
            await CarregarAsync();
        }, "Não foi possível definir a conta principal");
    }
    /// <summary>Menu de ações da conta na lista (editar, principal, ativar/desativar).</summary>
    private async Task ExecutarAcoesAsync(ContaVm? conta)
    {
        if (conta is null) return;

        var opcoes = new List<string> { "Editar" };

        if (!conta.Principal && conta.Ativo)
        {
            opcoes.Add("Definir como principal");
        }

        opcoes.Add(conta.Ativo ? "Desativar" : "Ativar");

        var escolha = await _dialogos.EscolherAsync($"{conta.Icone} {conta.Descricao}", [.. opcoes]);

        switch (escolha)
        {
            case "Editar":
                AbrirFormulario(conta);
                return;

            case "Definir como principal":
                await DefinirPrincipalAsync(conta);
                break;

            case "Ativar":
            case "Desativar":
                await AlternarAtivoAsync(conta);
                break;

            default:
                return;
        }

        await CarregarAsync();
    }
}
