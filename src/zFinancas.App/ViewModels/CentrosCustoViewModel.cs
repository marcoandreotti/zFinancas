using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Entidades;

namespace zFinancas.App.ViewModels;

/// <summary>Cadastro de centros de custo (lista + formulário inline com escolha de ícone).</summary>
public sealed class CentrosCustoViewModel : BaseViewModel
{
    private readonly CentroCustoService _servico;
    private readonly ServicoDialogos _dialogos;

    private CentroCustoVm? _centroEmEdicao;
    private bool _formularioVisivel;
    private string _descricao = string.Empty;
    private string _icone = "🧾";
    private bool _principal;
    private bool _ativo = true;

    public CentrosCustoViewModel(CentroCustoService servico, ServicoDialogos dialogos)
    {
        _dialogos = dialogos;
        _servico = servico;

        Titulo = "Centros de custo";

        CarregarCommand = new ComandoAssincrono(CarregarAsync);
        NovoCommand = new Comando(() => AbrirFormulario(null));
        EditarCommand = new ComandoAssincrono<CentroCustoVm>(item =>
        {
            AbrirFormulario(item);
            return Task.CompletedTask;
        });
        SalvarCommand = new ComandoAssincrono(SalvarAsync);
        CancelarCommand = new Comando(() => FormularioVisivel = false);
        AlternarAtivoCommand = new ComandoAssincrono<CentroCustoVm>(AlternarAtivoAsync);
        AcoesCommand = new ComandoAssincrono<CentroCustoVm>(ExecutarAcoesAsync);
        DefinirPrincipalCommand = new ComandoAssincrono<CentroCustoVm>(DefinirPrincipalAsync);

        Icones = new ObservableCollection<string>(CentroCusto.PaletaIcones);
    }

    public ObservableCollection<CentroCustoVm> Itens { get; } = [];

    /// <summary>Ícones exibidos como chips (fonte maior que o Picker nativo).</summary>
    public ObservableCollection<IconeOpcao> IconesOpcoes { get; } = [];

    public ObservableCollection<string> Icones { get; }

    public ICommand CarregarCommand { get; }
    public ICommand NovoCommand { get; }
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

    public string TituloFormulario => _centroEmEdicao is null ? "Novo centro de custo" : "Editar centro de custo";

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

    public bool Principal
    {
        get => _principal;
        set => SetProperty(ref _principal, value);
    }

    public bool Ativo
    {
        get => _ativo;
        set => SetProperty(ref _ativo, value);
    }

    public bool SemItens => Itens.Count == 0;

    public async Task CarregarAsync()
    {
        // Idem contas: este recarregamento também roda dentro do fluxo de salvar.
        Ocupado = true;

        try
        {
            var centros = await _servico.ListarAsync(incluirInativos: true);

            Itens.Clear();
            foreach (var centro in centros)
            {
                Itens.Add(new CentroCustoVm(centro.Id, centro.Descricao, centro.Icone, centro.Principal, centro.Ativo));
            }

            OnPropertyChanged(nameof(SemItens));
        }
        catch (Core.Dominio.Excecoes.DominioException ex)
        {
            await _dialogos.AlertaAsync("Centros de custo", ex.Message);
        }
        catch (Exception ex)
        {
            await _dialogos.AlertaAsync("Centros de custo", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void AbrirFormulario(CentroCustoVm? centro)
    {
        _centroEmEdicao = centro;
        Descricao = centro?.Descricao ?? string.Empty;
        Icone = centro?.Icone ?? "🧾";

        PrepararIcones();
        DefinirIcone(Icone);
        Principal = centro?.Principal ?? false;
        Ativo = centro?.Ativo ?? true;

        OnPropertyChanged(nameof(TituloFormulario));
        FormularioVisivel = true;
    }

    private async Task SalvarAsync()
    {
        await ExecutarAsync(async () =>
        {
            var requisicao = new RequisicaoCentroCusto(
                _centroEmEdicao?.Id ?? 0,
                Descricao,
                Icone,
                Principal,
                Ativo);

            if (_centroEmEdicao is null)
            {
                await _servico.CriarAsync(requisicao);
            }
            else
            {
                await _servico.AtualizarAsync(requisicao);
            }

            FormularioVisivel = false;
            await CarregarAsync();
        }, "Não foi possível salvar o centro de custo");
    }

    private async Task AlternarAtivoAsync(CentroCustoVm? centro)
    {
        if (centro is null) return;

        await ExecutarAsync(async () =>
        {
            await _servico.AlterarAtivoAsync(centro.Id, !centro.Ativo);
            await CarregarAsync();
        }, "Não foi possível alterar o centro de custo");
    }

    private async Task DefinirPrincipalAsync(CentroCustoVm? centro)
    {
        if (centro is null) return;

        await ExecutarAsync(async () =>
        {
            await _servico.DefinirPrincipalAsync(centro.Id);
            await CarregarAsync();
        }, "Não foi possível definir como principal");
    }

    /// <summary>Menu de ações do centro de custo na lista (editar, principal, ativar/desativar).</summary>
    private async Task ExecutarAcoesAsync(CentroCustoVm? centro)
    {
        if (centro is null) return;

        var opcoes = new List<string> { "Editar" };

        if (!centro.Principal && centro.Ativo)
        {
            opcoes.Add("Definir como principal");
        }

        opcoes.Add(centro.Ativo ? "Desativar" : "Ativar");

        var escolha = await _dialogos.EscolherAsync(centro.Descricao, [.. opcoes]);

        switch (escolha)
        {
            case "Editar":
                AbrirFormulario(centro);
                return;

            case "Definir como principal":
                await DefinirPrincipalAsync(centro);
                break;

            case "Ativar":
            case "Desativar":
                await AlternarAtivoAsync(centro);
                break;

            default:
                return;
        }

        await CarregarAsync();
    }

    /// <summary>Seleciona o ícone pelo chip e atualiza a marcação visual.</summary>
    public void DefinirIcone(string? icone)
    {
        if (string.IsNullOrWhiteSpace(icone)) return;

        Icone = icone;

        foreach (var opcao in IconesOpcoes)
        {
            opcao.Selecionado = string.Equals(opcao.Valor, icone, StringComparison.Ordinal);
        }
    }

    /// <summary>Monta os chips de ícones a partir da lista de ícones disponíveis.</summary>
    public void PrepararIcones()
    {
        if (IconesOpcoes.Count > 0) return;

        foreach (var icone in Icones)
        {
            IconesOpcoes.Add(new IconeOpcao(icone));
        }
    }
}
