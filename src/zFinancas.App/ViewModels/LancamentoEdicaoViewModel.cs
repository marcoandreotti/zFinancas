using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Voz;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.App.ViewModels;

/// <summary>Opção de recorrência exibida no formulário.</summary>
public sealed record RecorrenciaOpcao(Recorrencia Valor, string Descricao)
{
    public override string ToString() => Descricao;
}

/// <summary>Cadastro/edição de lançamento, incluindo recorrência, parcelas e quitação.</summary>
public sealed class LancamentoEdicaoViewModel : BaseViewModel
{
    private readonly LancamentoService _lancamentos;
    private readonly ContaCorrenteService _contas;
    private readonly CentroCustoService _centros;
    private readonly ISessaoAtual _sessao;
    private readonly ServicoNavegacao _navegacao;
    private readonly ServicoDialogos _dialogos;
    private readonly ServicoLancamentoPorVoz _vozServico;

    private long? _lancamentoId;
    private string _descricao = string.Empty;
    private string _valorTexto = string.Empty;
    private bool _ehDespesa = true;
    private ContaCorrente? _contaSelecionada;
    private CentroCusto? _centroSelecionado;
    private DateTime _dataVencimento = DateTime.Today;
    private DateTime _dataLancamento = DateTime.Today;
    private RecorrenciaOpcao? _recorrenciaSelecionada;
    private string _quantidadeParcelas = "1";
    private bool _compensado;
    private DateTime _dataCompensado = DateTime.Today;
    private string _observacao = string.Empty;
    private string _resumoParcelas = string.Empty;
    private bool _podeExcluir;
    private bool _podeQuitar;
    private bool _editandoSerie;
    private Lancamento? _lancamentoOriginal;

    public LancamentoEdicaoViewModel(
        LancamentoService lancamentos,
        ContaCorrenteService contas,
        CentroCustoService centros,
        ISessaoAtual sessao,
        ServicoNavegacao navegacao,
        ServicoDialogos dialogos,
        ServicoLancamentoPorVoz vozServico)
    {
        _lancamentos = lancamentos;
        _contas = contas;
        _centros = centros;
        _sessao = sessao;
        _navegacao = navegacao;
        _dialogos = dialogos;
        _vozServico = vozServico;

        Titulo = "Novo lançamento";

        SalvarCommand = new ComandoAssincrono(SalvarAsync);
        ExcluirCommand = new ComandoAssincrono(ExcluirAsync);
        CompensarCommand = new ComandoAssincrono(AlternarCompensacaoAsync);
        QuitarCommand = new ComandoAssincrono(QuitarPendentesAsync);
        CancelarCommand = new ComandoAssincrono(() => _navegacao.PopAsync());
        DespesaCommand = new Comando(() => EhDespesa = true);
        RecebimentoCommand = new Comando(() => EhDespesa = false);
        FalarCommand = new ComandoAssincrono(FalarAsync);

        Recorrencias =
        [
            new(Recorrencia.Unica, "Não repetir"),
            new(Recorrencia.Semanal, "Semanal"),
            new(Recorrencia.Quinzenal, "Quinzenal"),
            new(Recorrencia.Mensal, "Mensal"),
            new(Recorrencia.Anual, "Anual")
        ];
    }

    public ObservableCollection<ContaCorrente> Contas { get; } = [];

    public ObservableCollection<CentroCusto> Centros { get; } = [];

    public ObservableCollection<RecorrenciaOpcao> Recorrencias { get; }

    public ICommand SalvarCommand { get; }
    public ICommand ExcluirCommand { get; }
    public ICommand CompensarCommand { get; }
    public ICommand QuitarCommand { get; }
    public ICommand CancelarCommand { get; }
    public ICommand DespesaCommand { get; }
    public ICommand RecebimentoCommand { get; }

    /// <summary>Microfone: dita o lançamento e preenche o formulário.</summary>
    public ICommand FalarCommand { get; }

    public string Descricao
    {
        get => _descricao;
        set => SetProperty(ref _descricao, value);
    }

    public string ValorTexto
    {
        get => _valorTexto;
        set
        {
            // Celular pode não ter vírgula no teclado numérico: aceitamos os dois
            // separadores e normalizamos para o padrão pt-BR (vírgula, 2 casas).
            var normalizado = MascararValor(value);

            if (SetProperty(ref _valorTexto, normalizado))
            {
                AtualizarResumo();
            }
        }
    }

    /// <summary>
    /// Máscara de moeda: aceita apenas dígitos e formata como pt-BR (centavos).
    /// Digitar 3 2 5 0 vira 32,50; digitar 1 5 0 0 0 0 vira 1.500,00. Assim não há
    /// ambiguidade entre vírgula e ponto, independente do teclado do aparelho.
    /// </summary>
    private static string MascararValor(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var digitos = new string(texto.Where(char.IsDigit).ToArray());

        if (digitos.Length == 0) return string.Empty;
        if (digitos.Length > 13) digitos = digitos[..13];

        if (!decimal.TryParse(digitos, NumberStyles.None, CultureInfo.InvariantCulture, out var centavos))
        {
            return string.Empty;
        }

        return (centavos / 100m).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
    }

    public bool EhDespesa
    {
        get => _ehDespesa;
        set
        {
            if (SetProperty(ref _ehDespesa, value))
            {
                OnPropertyChanged(nameof(EhRecebimento));
                OnPropertyChanged(nameof(CorTipo));
            }
        }
    }

    public bool EhRecebimento => !_ehDespesa;

    public Color CorTipo => EhDespesa ? Paleta.Negativo : Paleta.Positivo;

    public string RotuloTipo => EhDespesa ? "Despesa" : "Recebimento";

    public ContaCorrente? ContaSelecionada
    {
        get => _contaSelecionada;
        set => SetProperty(ref _contaSelecionada, value);
    }

    public CentroCusto? CentroSelecionado
    {
        get => _centroSelecionado;
        set => SetProperty(ref _centroSelecionado, value);
    }

    public DateTime DataVencimento
    {
        get => _dataVencimento;
        set
        {
            if (SetProperty(ref _dataVencimento, value)) AtualizarResumo();
        }
    }

    public DateTime DataLancamento
    {
        get => _dataLancamento;
        set => SetProperty(ref _dataLancamento, value);
    }

    public RecorrenciaOpcao? RecorrenciaSelecionada
    {
        get => _recorrenciaSelecionada;
        set
        {
            if (!SetProperty(ref _recorrenciaSelecionada, value)) return;

            OnPropertyChanged(nameof(PermiteParcelas));
            AtualizarResumo();
        }
    }

    public bool PermiteParcelas => RecorrenciaSelecionada?.Valor != Recorrencia.Unica;

    public string QuantidadeParcelas
    {
        get => _quantidadeParcelas;
        set
        {
            if (SetProperty(ref _quantidadeParcelas, value)) AtualizarResumo();
        }
    }

    public bool Compensado
    {
        get => _compensado;
        set
        {
            if (SetProperty(ref _compensado, value))
            {
                OnPropertyChanged(nameof(MostraDataCompensacao));
            }
        }
    }

    public bool MostraDataCompensacao => Compensado || _lancamentoOriginal?.Compensado == true;

    public DateTime DataCompensado
    {
        get => _dataCompensado;
        set => SetProperty(ref _dataCompensado, value);
    }

    public string Observacao
    {
        get => _observacao;
        set => SetProperty(ref _observacao, value);
    }

    public string ResumoParcelas
    {
        get => _resumoParcelas;
        private set => SetProperty(ref _resumoParcelas, value);
    }

    public bool PodeExcluir
    {
        get => _podeExcluir;
        private set => SetProperty(ref _podeExcluir, value);
    }

    public bool PodeQuitar
    {
        get => _podeQuitar;
        private set => SetProperty(ref _podeQuitar, value);
    }

    public bool EditandoSerie
    {
        get => _editandoSerie;
        private set => SetProperty(ref _editandoSerie, value);
    }

    public string TextoCompensacao => _lancamentoOriginal?.Compensado == true
        ? "Desfazer compensação"
        : "Marcar como compensado";

    /// <summary>Prepara o formulário: nulo = novo lançamento.</summary>
    /// <summary>Prepara o formulário: nulo = novo lançamento (opcionalmente vindo da voz).</summary>
    public void Preparar(long? lancamentoId, Core.Dominio.Voz.LancamentoFalado? rascunho = null)
    {
        _lancamentoId = lancamentoId;
        _rascunhoDeVoz = rascunho;
        _ = InicializarAsync(lancamentoId);
    }

    private async Task InicializarAsync(long? lancamentoId)
    {
        await ExecutarAsync(async () =>
        {
            var contas = await _contas.ListarAsync();
            var centros = await _centros.ListarAsync();

            Contas.Clear();
            foreach (var conta in contas) Contas.Add(conta);

            Centros.Clear();
            foreach (var centro in centros) Centros.Add(centro);

            if (lancamentoId is null)
            {
                Titulo = "Novo lançamento";
                ContaSelecionada = Contas.FirstOrDefault(c => c.Id == _sessao.ContaAtivaId)
                                   ?? Contas.FirstOrDefault(c => c.Principal)
                                   ?? Contas.FirstOrDefault();
                CentroSelecionado = Centros.FirstOrDefault(c => c.Principal) ?? Centros.FirstOrDefault();
                RecorrenciaSelecionada = Recorrencias[0];
                DataVencimento = DateTime.Today;
                DataLancamento = DateTime.Today;
                DataCompensado = DateTime.Today;
                PodeExcluir = false;
                PodeQuitar = false;
                AplicarRascunhoDeVoz();
                AtualizarResumo();
                return;
            }

            var lancamento = await _lancamentos.ObterAsync(lancamentoId.Value)
                             ?? throw new DominioException("Lançamento não encontrado.");

            _lancamentoOriginal = lancamento;
            Titulo = "Editar lançamento";

            Descricao = lancamento.Descricao;
            EhDespesa = lancamento.Valor < 0m;
            ValorTexto = Math.Abs(lancamento.Valor).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
            ContaSelecionada = Contas.FirstOrDefault(c => c.Id == lancamento.ContaCorrenteId);
            CentroSelecionado = Centros.FirstOrDefault(c => c.Id == lancamento.CentroCustoId);
            DataVencimento = lancamento.DataVencimento;
            DataLancamento = lancamento.DataLancamento;
            Compensado = lancamento.Compensado;
            DataCompensado = lancamento.DataCompensado ?? DateTime.Today;
            Observacao = lancamento.Observacao ?? string.Empty;
            RecorrenciaSelecionada = Recorrencias[0];

            EditandoSerie = lancamento.FazParteDeSerie;
            PodeExcluir = !lancamento.Compensado;
            PodeQuitar = !lancamento.Compensado && lancamento.FazParteDeSerie;

            OnPropertyChanged(nameof(MostraDataCompensacao));
            OnPropertyChanged(nameof(TextoCompensacao));
            AtualizarResumo();
        }, "Não foi possível abrir o lançamento");
    }

    private decimal ObterValorAbsoluto()
    {
        var texto = (ValorTexto ?? string.Empty)
            .Replace("R$", string.Empty)
            .Replace(" ", string.Empty)
            .Trim();

        if (string.IsNullOrWhiteSpace(texto))
        {
            throw new DominioException("Informe o valor do lançamento.");
        }

        if (!decimal.TryParse(texto, NumberStyles.Any, CultureInfo.GetCultureInfo("pt-BR"), out var valor)
            && !decimal.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out valor))
        {
            throw new DominioException("Valor inválido. Use, por exemplo, 1.234,56.");
        }

        if (valor <= 0m)
        {
            throw new DominioException("O valor deve ser maior que zero. Use despesa ou recebimento para definir o sinal.");
        }

        return valor;
    }

    private decimal ValorComSinal() => EhDespesa ? -ObterValorAbsoluto() : ObterValorAbsoluto();

    private int ObterQuantidadeParcelas()
    {
        if (!PermiteParcelas) return 1;

        if (!int.TryParse(QuantidadeParcelas, out var quantidade) || quantidade < 1)
        {
            throw new DominioException("Informe a quantidade de parcelas (1 ou mais).");
        }

        return quantidade;
    }

    private void AtualizarResumo()
    {
        try
        {
            if (!PermiteParcelas)
            {
                ResumoParcelas = "Lançamento único.";
                return;
            }

            var valor = ObterValorAbsoluto();
            var quantidade = int.TryParse(QuantidadeParcelas, out var q) && q > 0 ? q : 0;

            ResumoParcelas = quantidade <= 1
                ? "Informe 2 ou mais parcelas para gerar uma série."
                : $"{quantidade}× de {Formatador.Moeda(valor)} = {Formatador.Moeda(valor * quantidade)}";
        }
        catch (DominioException)
        {
            ResumoParcelas = PermiteParcelas ? "Informe o valor para ver o total da série." : "Lançamento único.";
        }
    }

    private async Task SalvarAsync()
    {
        await ExecutarAsync(async () =>
        {
            if (ContaSelecionada is null) throw new DominioException("Selecione a conta corrente.");
            if (CentroSelecionado is null) throw new DominioException("Selecione o centro de custo.");

            var valor = ValorComSinal();

            if (_lancamentoId is null)
            {
                var requisicao = new RequisicaoLancamento(
                    0,
                    ContaSelecionada.Id,
                    CentroSelecionado.Id,
                    Descricao,
                    valor,
                    DataVencimento,
                    RecorrenciaSelecionada?.Valor ?? Recorrencia.Unica,
                    ObterQuantidadeParcelas(),
                    Compensado,
                    Compensado ? DataCompensado : null,
                    Observacao,
                    DataLancamento);

                var resultado = await _lancamentos.CriarAsync(requisicao);

                var mensagem = resultado.Lancamentos.Count > 1
                    ? $"{resultado.Lancamentos.Count} parcelas criadas (série {resultado.Agrupador})."
                    : "Lançamento criado.";

                await _dialogos.AlertaAsync("Tudo certo", mensagem);
            }
            else
            {
                var requisicao = new RequisicaoLancamento(
                    _lancamentoId.Value,
                    ContaSelecionada.Id,
                    CentroSelecionado.Id,
                    Descricao,
                    valor,
                    DataVencimento,
                    Recorrencia.Unica,
                    1,
                    Compensado,
                    Compensado ? DataCompensado : null,
                    Observacao,
                    DataLancamento);

                await _lancamentos.AtualizarAsync(requisicao);
            }

            await _navegacao.PopAsync();
        }, "Não foi possível salvar o lançamento");
    }

    private async Task ExcluirAsync()
    {
        if (_lancamentoId is null) return;

        var somenteEsta = await _dialogos.ConfirmarAsync(
            "Excluir lançamento",
            EditandoSerie
                ? "Excluir esta parcela e as seguintes ainda pendentes da série?"
                : "Confirma a exclusão deste lançamento?",
            "Excluir",
            "Cancelar");

        if (!somenteEsta) return;

        await ExecutarAsync(async () =>
        {
            await _lancamentos.ExcluirAsync(_lancamentoId.Value, aPartirDaParcela: EditandoSerie);
            await _navegacao.PopAsync();
        }, "Não foi possível excluir");
    }

    private async Task AlternarCompensacaoAsync()
    {
        if (_lancamentoOriginal is null) return;

        await ExecutarAsync(async () =>
        {
            if (_lancamentoOriginal.Compensado)
            {
                await _lancamentos.DescompensarAsync(_lancamentoOriginal.Id);
            }
            else
            {
                await _lancamentos.CompensarAsync(_lancamentoOriginal.Id, DataCompensado);
            }

            await _navegacao.PopAsync();
        }, "Não foi possível alterar a compensação");
    }

    private async Task QuitarPendentesAsync()
    {
        if (_lancamentoOriginal?.ChaveDaSerie is null) return;

        await ExecutarAsync(async () =>
        {
            var serie = await _lancamentos.ListarSerieAsync(_lancamentoOriginal.ChaveDaSerie);
            var pendentes = serie
                .Where(l => !l.Compensado && l.Parcela >= _lancamentoOriginal.Parcela)
                .ToList();

            if (pendentes.Count == 0)
            {
                await _dialogos.AlertaAsync("Quitação", "Não há parcelas pendentes nesta série.");
                return;
            }

            var plano = await _lancamentos.PlanejarQuitacaoAsync(pendentes.Select(l => l.Id), DateTime.Today);

            var confirmar = await _dialogos.ConfirmarAsync(
                "Quitar parcelas",
                $"Quitar {plano.QuantidadeParcelas} parcela(s) por {Formatador.Moeda(plano.ValorTotal)}?\n\n" +
                "As parcelas pendentes serão substituídas por um único lançamento compensado.",
                "Quitar",
                "Cancelar");

            if (!confirmar) return;

            await _lancamentos.QuitarAsync(plano);
            await _navegacao.PopAsync();
        }, "Não foi possível quitar as parcelas");
    }

    // ---------------------------------------------------------------- voz

    private LancamentoFalado? _rascunhoDeVoz;
    private bool _ouvindo;
    private string _statusVoz = string.Empty;

    public bool Ouvindo
    {
        get => _ouvindo;
        private set
        {
            if (SetProperty(ref _ouvindo, value))
            {
                OnPropertyChanged(nameof(RotuloFalar));
            }
        }
    }

    public string RotuloFalar => _ouvindo ? "Ouvindo… fale agora" : "Falar e preencher";

    /// <summary>Feedback do reconhecimento (o que foi entendido ou o motivo da falha).</summary>
    public string StatusVoz
    {
        get => _statusVoz;
        private set
        {
            if (SetProperty(ref _statusVoz, value))
            {
                OnPropertyChanged(nameof(TemStatusVoz));
            }
        }
    }

    public bool TemStatusVoz => !string.IsNullOrWhiteSpace(_statusVoz);

    /// <summary>Aplica no formulário o que foi ditado (o usuário revisa antes de salvar).</summary>
    private void AplicarRascunhoDeVoz()
    {
        if (_rascunhoDeVoz is null)
        {
            return;
        }

        var rascunho = _rascunhoDeVoz;
        _rascunhoDeVoz = null;

        EhDespesa = rascunho.EhDespesa;
        ValorTexto = Math.Abs(rascunho.Valor).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
        Descricao = rascunho.Descricao;
        DataVencimento = rascunho.DataVencimento;
        DataLancamento = rascunho.DataVencimento;
        Compensado = rascunho.Compensado;
        DataCompensado = rascunho.Compensado ? rascunho.DataVencimento : DateTime.Today;

        if (rascunho.CentroCustoId is { } centroId)
        {
            CentroSelecionado = Centros.FirstOrDefault(c => c.Id == centroId) ?? CentroSelecionado;
        }

        StatusVoz = rascunho.Resumo;
    }

    private async Task FalarAsync()
    {
        if (Ouvindo) return;

        Ouvindo = true;
        StatusVoz = "Ouvindo… fale o lançamento (ex.: “gastei 32,50 no mercado”).";

        try
        {
            var resultado = await _vozServico.OuvirEInterpretarAsync();

            if (!resultado.Sucesso || resultado.Lancamento is null)
            {
                StatusVoz = resultado.Mensagem;
                await _dialogos.AlertaAsync("Não entendi", resultado.Mensagem);
                return;
            }

            _rascunhoDeVoz = resultado.Lancamento;
            AplicarRascunhoDeVoz();
        }
        finally
        {
            Ouvindo = false;
        }
    }
}
