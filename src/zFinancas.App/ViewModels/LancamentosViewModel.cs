using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.App.ViewModels;

/// <summary>Lista de lançamentos com filtros e ações rápidas (compensar, excluir).</summary>
public sealed class LancamentosViewModel : BaseViewModel
{
    private readonly LancamentoService _lancamentos;
    private readonly ContaCorrenteService _contas;
    private readonly CentroCustoService _centros;
    private readonly ISessaoAtual _sessao;
    private readonly ServicoNavegacao _navegacao;
    private readonly ServicoDialogos _dialogos;
    private SelecaoParcelasViewModel? _selecao;

    private SelecaoContasViewModel? _selecaoContas;
    private IReadOnlyList<long> _idsContas = [];
    private string _resumoContas = "Todas as contas";
    private bool _aplicandoSelecao;

    /// <summary>Quantos dias antes de hoje a lista começa (valor inicial da data de corte).</summary>
    private const int DiasDeCorte = 4;

    private DateTime _dataCorte = DateTime.Today.AddDays(-DiasDeCorte);

    private CancellationTokenSource? _atrasoBusca;
    private bool _carregando;
    private bool _recarregarPendente;
    private string _busca = string.Empty;
    private bool _somentePendentes;
    private string _saldoAnteriorTexto = "R$ 0,00";

    public LancamentosViewModel(
        LancamentoService lancamentos,
        ContaCorrenteService contas,
        CentroCustoService centros,
        ISessaoAtual sessao,
        ServicoNavegacao navegacao,
        ServicoDialogos dialogos)
    {
        _lancamentos = lancamentos;
        _contas = contas;
        _centros = centros;
        _sessao = sessao;
        _navegacao = navegacao;
        _dialogos = dialogos;

        Titulo = "Lançamentos";

        CarregarCommand = new ComandoAssincrono(CarregarAsync);
        NovoCommand = new ComandoAssincrono(() => _navegacao.AbrirLancamentoAsync());
        EditarCommand = new ComandoAssincrono<LancamentoVm>(item =>
            item is null ? Task.CompletedTask : _navegacao.AbrirLancamentoAsync(item.Id));
        AcoesCommand = new ComandoAssincrono<LancamentoVm>(ExecutarAcoesAsync);
        CompensarCommand = new ComandoAssincrono<LancamentoVm>(CompensarAsync);
        AbrirSelecaoContasCommand = new ComandoAssincrono(AbrirSelecaoContasAsync);
    }

    private IReadOnlyList<LancamentoVm> _itens = [];

    /// <summary>
    /// Lista da tela. É trocada inteira a cada carga (uma única notificação), em vez de
    /// receber item a item — diferença grande de desempenho no Android com muitos lançamentos.
    /// </summary>
    public IReadOnlyList<LancamentoVm> Itens
    {
        get => _itens;
        private set
        {
            if (SetProperty(ref _itens, value))
            {
                OnPropertyChanged(nameof(SemItens));
            }
        }
    }

    public ICommand CarregarCommand { get; }
    public ICommand NovoCommand { get; }
    public ICommand EditarCommand { get; }
    public ICommand AcoesCommand { get; }

    /// <summary>Compensação direta na linha (mesmo atalho do extrato).</summary>
    public ICommand CompensarCommand { get; }

    public ICommand AbrirSelecaoContasCommand { get; }

    public SelecaoContasViewModel? SelecaoContas
    {
        get => _selecaoContas;
        private set
        {
            if (SetProperty(ref _selecaoContas, value))
            {
                OnPropertyChanged(nameof(SelecaoContasAtiva));
            }
        }
    }

    public bool SelecaoContasAtiva => SelecaoContas is not null;

    /// <summary>Resumo das contas marcadas (texto do botão do filtro).</summary>
    public string ResumoContas
    {
        get => _resumoContas;
        private set => SetProperty(ref _resumoContas, value);
    }

    /// <summary>
    /// Data de corte da lista: carrega os lançamentos a partir dela.
    /// Dá para voltar no tempo quando aparecem parcelas de lançamentos antigos.
    /// </summary>
    public DateTime DataCorte
    {
        get => _dataCorte;
        set
        {
            if (SetProperty(ref _dataCorte, value.Date) && !_carregando)
            {
                _ = CarregarAsync();
            }
        }
    }

    public string Busca
    {
        get => _busca;
        set
        {
            if (!SetProperty(ref _busca, value))
            {
                return;
            }

            // Espera o usuário parar de digitar: recarregar a cada tecla travava no celular.
            _atrasoBusca?.Cancel();
            _atrasoBusca = new CancellationTokenSource();
            var token = _atrasoBusca.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(350, token).ConfigureAwait(false);

                    if (!token.IsCancellationRequested)
                    {
                        MainThread.BeginInvokeOnMainThread(() => _ = CarregarAsync());
                    }
                }
                catch (TaskCanceledException)
                {
                    // digitou mais uma letra; a próxima execução assume
                }
            });
        }
    }

    public bool SomentePendentes
    {
        get => _somentePendentes;
        set
        {
            if (SetProperty(ref _somentePendentes, value))
            {
                _ = CarregarAsync();
            }
        }
    }

    public string SaldoAnteriorTexto
    {
        get => _saldoAnteriorTexto;
        private set => SetProperty(ref _saldoAnteriorTexto, value);
    }

    public bool SemItens => Itens.Count == 0;

    public async Task CarregarAsync()
    {
        // Duas cargas simultâneas (ex.: toque + evento do Picker) seriam inúteis e podem
        // embaralhar a lista; a última prevalece.
        if (_carregando)
        {
            // Uma carga pedida durante outra é executada ao final, para não
            // perder a última digitação/busca.
            _recarregarPendente = true;
            return;
        }

        _carregando = true;

        try
        {
            await ExecutarAsync(async () =>
            {
                var relogio = System.Diagnostics.Stopwatch.StartNew();

                // Contas marcadas no painel (vazio = todas as permitidas ao usuário).
                var ids = _idsContas.Count > 0
                    ? _idsContas.ToList()
                    : _sessao.ContasPermitidas.Select(c => c.Id).ToList();

                var corte = DataCorte.Date;
                var somentePendentes = SomentePendentes;
                var busca = Busca;

                // O driver do SQLite é síncrono: sem Task.Run tudo isso roda na thread de UI
                // e o Android trava ("Skipped N frames"). Aqui só leitura, sem tocar na tela.
                var (contas, idPorConta, novos, saldoAnterior) = await Task.Run(async () =>
                {
                    var contasLocal = await _contas.ListarAsync();
                    var centros = (await _centros.ListarAsync(incluirInativos: true)).ToDictionary(c => c.Id);

                    var todos = new List<Lancamento>();
                    foreach (var contaId in ids)
                    {
                        todos.AddRange(await _lancamentos.ListarAsync(contaId));
                    }

                    var filtrados = todos
                        .Where(l => l.DataVencimento.Date >= corte)
                        .Where(l => !somentePendentes || l.Pendente)
                        .Where(l => string.IsNullOrWhiteSpace(busca)
                                    || l.Descricao.Contains(busca, StringComparison.CurrentCultureIgnoreCase))
                        .OrderBy(l => l.DataVencimento)
                        .ThenBy(l => l.Id)
                        .ToList();

                    var contasPorId = contasLocal.ToDictionary(c => c.Id);
                    var saldos = CalcularSaldos(todos, corte);
                    var lista = new List<LancamentoVm>(filtrados.Count);

                    foreach (var lancamento in filtrados)
                    {
                        var acumulado = saldos.GetValueOrDefault(lancamento.Id);

                        lista.Add(LancamentoVm.Criar(
                            lancamento,
                            contasPorId.GetValueOrDefault(lancamento.ContaCorrenteId),
                            centros.GetValueOrDefault(lancamento.CentroCustoId),
                            acumulado,
                            $"saldo {Formatador.Moeda(acumulado)}",
                            parcelaNoDetalhe: true));
                    }

                    return (contasLocal, contasPorId, lista, CalcularSaldoAnterior(todos, corte));
                });

                AtualizarResumoContas(contas, ids);

                Itens = novos;
                SaldoAnteriorTexto = Formatador.Moeda(saldoAnterior);

                System.Diagnostics.Debug.WriteLine(
                    $"[perf] Lancamentos total={relogio.ElapsedMilliseconds}ms linhas={novos.Count} " +
                    $"contas={idPorConta.Count}");
            }, "Não foi possível carregar os lançamentos");
        }
        finally
        {
            _carregando = false;

            if (_recarregarPendente)
            {
                _recarregarPendente = false;
                _ = CarregarAsync();
            }
        }
    }

    /// <summary>
    /// Saldo acumulado de cada lançamento, com a mesma regra do extrato consolidado:
    /// parte do que foi compensado ANTES do corte e cada linha recebe o valor quando a
    /// compensação caiu dentro da janela. Contas marcadas são somadas (igual ao extrato).
    /// </summary>
    private static Dictionary<long, decimal> CalcularSaldos(IEnumerable<Lancamento> lancamentos, DateTime corte)
    {
        var todos = lancamentos as IReadOnlyCollection<Lancamento> ?? lancamentos.ToList();
        var saldos = new Dictionary<long, decimal>(todos.Count);
        var saldo = CalcularSaldoAnterior(todos, corte);

        foreach (var lancamento in todos.OrderBy(l => l.DataVencimento).ThenBy(l => l.Id))
        {
            // A lista mostra o saldo acumulado linha a linha (pendentes entram na conta,
            // ou seja, é o saldo previsto). O "saldo anterior" do topo continua sendo o
            // compensado até o corte.
            if (DataDaCompensacao(lancamento).Date >= corte.Date)
            {
                saldo += lancamento.Valor;
            }

            saldos[lancamento.Id] = saldo;
        }

        return saldos;
    }

    /// <summary>Saldo de partida da lista: compensados com compensação anterior ao corte.</summary>
    private static decimal CalcularSaldoAnterior(IEnumerable<Lancamento> lancamentos, DateTime corte)
        => lancamentos
            .Where(l => l.Compensado && DataDaCompensacao(l).Date < corte.Date)
            .Sum(l => l.Valor);

    /// <summary>Data em que o dinheiro se moveu (compensação), com o vencimento como reserva.</summary>
    private static DateTime DataDaCompensacao(Lancamento lancamento)
        => lancamento.DataCompensado ?? lancamento.DataVencimento;

    // ------------------------------------------------------------- seleção de contas

    private async Task AbrirSelecaoContasAsync()
    {
        var contas = await _contas.ListarAsync();

        if (contas.Count == 0)
        {
            await _dialogos.AlertaAsync("Lançamentos", "Nenhuma conta disponível.");
            return;
        }

        var idsAtuais = _idsContas.Count > 0
            ? _idsContas
            : _sessao.ContasPermitidas.Select(c => c.Id).ToList();

        var opcoes = contas
            .Select(c => new ContaFiltro
            {
                Id = c.Id,
                Descricao = c.Descricao,
                Icone = c.Icone,
                Principal = c.Principal,
                Selecionada = idsAtuais.Contains(c.Id)
            })
            .ToList();

        var selecao = new SelecaoContasViewModel(
            "Contas",
            "Marque as contas que deseja analisar. O resumo e a lista passam a considerar apenas elas.",
            "Aplicar",
            opcoes);

        selecao.Confirmou += (_, ids) =>
        {
            SelecaoContas = null;
            _idsContas = ids;
            _ = CarregarAsync();
        };

        selecao.Cancelou += (_, _) => SelecaoContas = null;

        SelecaoContas = selecao;
    }

    private void AtualizarResumoContas(IReadOnlyList<ContaCorrente> contas, IReadOnlyList<long> ids)
    {
        var marcadas = contas.Where(c => ids.Contains(c.Id)).ToList();

        ResumoContas = marcadas.Count switch
        {
            0 => "Todas as contas",
            1 => marcadas[0].Descricao,
            _ => $"{marcadas.Count} contas"
        };
    }

    // ------------------------------------------------------------- seleção de parcelas

    private SelecaoParcelasViewModel? _selecaoPendente;
    private Func<IReadOnlyList<long>, Task>? _acaoDaSelecao;

    /// <summary>Painel de seleção em uso (null quando fechado).</summary>
    public SelecaoParcelasViewModel? Selecao
    {
        get => _selecaoPendente;
        private set
        {
            if (SetProperty(ref _selecaoPendente, value))
            {
                OnPropertyChanged(nameof(SelecaoAtiva));
            }
        }
    }

    public bool SelecaoAtiva => Selecao is not null;

    /// <summary>Abre o painel sobre a própria lista (sem navegação, o caminho mais estável).</summary>
    private void AbrirSelecao(
        string titulo,
        string instrucao,
        List<ParcelaSelecionavel> opcoes,
        string textoConfirmar,
        Func<IReadOnlyList<long>, Task> acao)
    {
        _acaoDaSelecao = acao;
        Selecao = new SelecaoParcelasViewModel(titulo, instrucao, opcoes, textoConfirmar);
    }

    private void FecharSelecao()
    {
        _acaoDaSelecao = null;
        Selecao = null;
    }

    /// <summary>Confirmou: fecha o painel e executa a ação escolhida (excluir ou quitar).</summary>
    public ICommand ConfirmarSelecaoCommand => _confirmarSelecao ??= new ComandoAssincrono(ConfirmarSelecaoAsync);

    private ICommand? _confirmarSelecao;

    public ICommand CancelarSelecaoCommand => _cancelarSelecao ??= new Comando(FecharSelecao);
    private ICommand? _cancelarSelecao;

    public ICommand MarcarTodasSelecaoCommand => _marcarTodasSelecao ??= new Comando(() => Selecao?.MarcarTodas());
    private ICommand? _marcarTodasSelecao;

    public ICommand LimparSelecaoCommand => _limparSelecao ??= new Comando(() => Selecao?.Limpar());
    private ICommand? _limparSelecao;

    private async Task ConfirmarSelecaoAsync()
    {
        var selecao = Selecao;
        var acao = _acaoDaSelecao;

        if (selecao is null)
        {
            return;
        }

        var ids = selecao.IdsSelecionados;

        if (ids.Count == 0 || acao is null)
        {
            FecharSelecao();
            return;
        }

        FecharSelecao();

        await acao(ids);
        await CarregarAsync();
    }

    private async Task ExecutarAcoesAsync(LancamentoVm? item)
    {
        if (item is null) return;

        // Só é "série" quando o lançamento tem mais de uma parcela.
        var temSerie = !string.IsNullOrWhiteSpace(item.RotuloParcela);

        var opcoes = new List<string>();

        if (item.Pendente)
        {
            opcoes.Add("Marcar como compensado");

            if (temSerie)
            {
                opcoes.Add("Quitar parcelas…");
            }
        }
        else
        {
            opcoes.Add("Desfazer compensação");
        }

        opcoes.Add("Editar");

        if (item.Pendente)
        {
            opcoes.Add("Excluir esta parcela");

            if (temSerie)
            {
                opcoes.Add("Excluir esta e as seguintes em aberto");
                opcoes.Add("Escolher parcelas para excluir…");
            }
        }

        var escolha = await _dialogos.EscolherAsync($"{item.Descricao} • {item.ValorTexto}", [.. opcoes]);

        switch (escolha)
        {
            case "Marcar como compensado":
                await _lancamentos.CompensarAsync(item.Id);
                break;

            case "Desfazer compensação":
                await _lancamentos.DescompensarAsync(item.Id);
                break;

            case "Quitar parcelas…":
                await AbrirQuitacaoAsync(item);
                return;

            case "Excluir esta parcela":
                if (await _dialogos.ConfirmarAsync(
                        "Excluir parcela",
                        $"Excluir '{item.Descricao}' ({item.RotuloParcela})?",
                        "Excluir",
                        "Cancelar"))
                {
                    await _lancamentos.ExcluirAsync(item.Id);
                }
                break;

            case "Excluir esta e as seguintes em aberto":
                if (await _dialogos.ConfirmarAsync(
                        "Excluir parcelas",
                        $"Excluir esta parcela e as seguintes ainda em aberto de '{item.Descricao}'?" +
                        "\n\nAs parcelas já compensadas são mantidas.",
                        "Excluir",
                        "Cancelar"))
                {
                    await _lancamentos.ExcluirAsync(item.Id, aPartirDaParcela: true);
                }
                break;

            case "Escolher parcelas para excluir…":
                await AbrirExclusaoAsync(item);
                return;

            case "Editar":
                await _navegacao.AbrirLancamentoAsync(item.Id);
                return;

            default:
                return;
        }

        await CarregarAsync();
    }

    /// <summary>
    /// Quitação em série: abre o painel com TODAS as parcelas em aberto para o usuário
    /// escolher. Ao confirmar, as escolhidas são excluídas e um único lançamento
    /// compensado é criado com a soma.
    /// </summary>
    private async Task AbrirQuitacaoAsync(LancamentoVm item)
    {
        var lancamento = await _lancamentos.ObterAsync(item.Id);
        if (lancamento?.ChaveDaSerie is null)
        {
            await _dialogos.AlertaAsync("Quitação", "Este lançamento não faz parte de uma série de parcelas.");
            return;
        }

        var serie = await _lancamentos.ListarSerieAsync(lancamento.ChaveDaSerie);
        var pendentes = serie
            .Where(l => !l.Compensado)
            .OrderBy(l => l.Parcela)
            .ToList();

        if (pendentes.Count == 0)
        {
            await _dialogos.AlertaAsync("Quitação", "Não há parcelas pendentes para quitar nesta série.");
            return;
        }

        var opcoes = pendentes
            .Select(l => new ParcelaSelecionavel
            {
                Id = l.Id,
                Descricao = $"{l.Descricao} • {l.RotuloParcela}",
                Detalhe = $"vence {Formatador.Data(l.DataVencimento)}",
                Valor = l.Valor,
                Selecionada = true
            })
            .ToList();

        AbrirSelecao(
            "Quitar parcelas",
            "Marque as parcelas que entram na quitação. Elas serão excluídas e substituídas por um único lançamento compensado.",
            opcoes,
            "Quitar selecionadas",
            QuitarParcelasAsync);
    }

    private async Task QuitarParcelasAsync(IReadOnlyList<long> ids)
    {
        var plano = await _lancamentos.PlanejarQuitacaoAsync(ids, DateTime.Today);

        var confirmar = await _dialogos.ConfirmarAsync(
            "Confirmar quitação",
            $"As {plano.QuantidadeParcelas} parcela(s) selecionada(s) serão excluídas e substituídas por " +
            $"um lançamento compensado de {Formatador.Moeda(plano.ValorTotal)}.",
            "Quitar",
            "Cancelar");

        if (!confirmar) return;

        await _lancamentos.QuitarAsync(plano);

        await _dialogos.AlertaAsync(
            "Quitação concluída",
            "As parcelas selecionadas foram consolidadas em um único lançamento compensado.");
    }

    /// <summary>Exclusão em lote: o usuário escolhe quais parcelas em aberto excluir.</summary>
    private async Task AbrirExclusaoAsync(LancamentoVm item)
    {
        var lancamento = await _lancamentos.ObterAsync(item.Id);

        if (lancamento?.ChaveDaSerie is null)
        {
            if (await _dialogos.ConfirmarAsync("Excluir", $"Excluir '{item.Descricao}'?", "Excluir", "Cancelar"))
            {
                await _lancamentos.ExcluirAsync(item.Id);
                await CarregarAsync();
            }

            return;
        }

        var serie = await _lancamentos.ListarSerieAsync(lancamento.ChaveDaSerie);
        var pendentes = serie
            .Where(l => !l.Compensado)
            .OrderBy(l => l.Parcela)
            .ToList();

        if (pendentes.Count == 0)
        {
            await _dialogos.AlertaAsync("Excluir", "Esta série não tem parcelas em aberto.");
            return;
        }

        var opcoes = pendentes
            .Select(l => new ParcelaSelecionavel
            {
                Id = l.Id,
                Descricao = $"{l.Descricao} • {l.RotuloParcela}",
                Detalhe = $"vence {Formatador.Data(l.DataVencimento)}",
                Valor = l.Valor,
                Selecionada = l.Id == item.Id
            })
            .ToList();

        AbrirSelecao(
            "Excluir parcelas",
            "Marque as parcelas em aberto que deseja excluir. As compensadas não aparecem.",
            opcoes,
            "Excluir selecionadas",
            ExcluirParcelasAsync);
    }

    private async Task ExcluirParcelasAsync(IReadOnlyList<long> ids)
    {
        var texto = ids.Count == 1 ? "1 parcela" : $"{ids.Count} parcelas";

        if (!await _dialogos.ConfirmarAsync("Excluir", $"Excluir {texto}?", "Excluir", "Cancelar"))
        {
            return;
        }

        await _lancamentos.ExcluirVariasAsync(ids);
    }

#if DEBUG

    /// <summary>Atalho de desenvolvimento: abre o painel de quitação para a 1ª série pendente.</summary>
    public async Task DepurarQuitacaoAsync()
    {
        await CarregarAsync();
        var item = Itens.FirstOrDefault(i => i.Pendente && !string.IsNullOrWhiteSpace(i.RotuloParcela));
        if (item is not null)
        {
            await AbrirQuitacaoAsync(item);
        }
    }

    /// <summary>Atalho de desenvolvimento: abre o painel de exclusão em lote.</summary>
    public async Task DepurarExclusaoAsync()
    {
        await CarregarAsync();
        var item = Itens.FirstOrDefault(i => i.Pendente && !string.IsNullOrWhiteSpace(i.RotuloParcela));
        if (item is not null)
        {
            await AbrirExclusaoAsync(item);
        }
    }

#endif
    // ---------------------------------------------------------- compensação

    private CompensarViewModel? _compensacao;

    /// <summary>Painel de compensação em uso (null quando fechado).</summary>
    public CompensarViewModel? Compensacao
    {
        get => _compensacao;
        private set
        {
            if (SetProperty(ref _compensacao, value))
            {
                OnPropertyChanged(nameof(CompensacaoAtiva));
            }
        }
    }

    public bool CompensacaoAtiva => Compensacao is not null;

    private void AbrirCompensacao(LancamentoVm item)
    {
        var painel = new CompensarViewModel(item);

        painel.Confirmou += async (_, data) =>
        {
            Compensacao = null;
            await _lancamentos.CompensarAsync(item.Id, data);
            await CarregarAsync();
        };

        painel.Cancelou += (_, _) => Compensacao = null;

        Compensacao = painel;
    }

#if DEBUG

    /// <summary>Atalho de desenvolvimento: abre o painel de compensação da 1ª pendência.</summary>
    public async Task DepurarCompensacaoAsync()
    {
        await CarregarAsync();
        var item = Itens.FirstOrDefault(i => i.Pendente);
        if (item is not null)
        {
            AbrirCompensacao(item);
        }
    }

#endif

    /// <summary>Atalho da linha: abre o painel de compensação (data escolhida).</summary>
    private Task CompensarAsync(LancamentoVm? item)
    {
        if (item is null || !item.Pendente)
        {
            return Task.CompletedTask;
        }

        AbrirCompensacao(item);

        return Task.CompletedTask;
    }
}
