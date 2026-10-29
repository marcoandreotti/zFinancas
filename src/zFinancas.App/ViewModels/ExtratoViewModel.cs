using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Servicos;

namespace zFinancas.App.ViewModels;

/// <summary>
/// Extrato por período. Permite escolher <b>várias contas</b> e analisar o conjunto:
/// os saldos são somados e os lançamentos aparecem em ordem de vencimento, com o saldo
/// consolidado linha a linha.
/// </summary>
public sealed class ExtratoViewModel : BaseViewModel
{
    private readonly ExtratoService _extrato;
    private readonly LancamentoService _lancamentos;
    private readonly ContaCorrenteService _contas;
    private readonly CentroCustoService _centros;
    private readonly ISessaoAtual _sessao;
    private readonly ServicoDialogos _dialogos;

    private SelecaoContasViewModel? _selecaoContas;
    private IReadOnlyList<long> _idsContas = [];
    private string _resumoContas = "Selecione as contas";
    private DateTime _dataInicio = DateTime.Today.AddDays(-4);
    private DateTime _dataFim = DateTime.Today.AddMonths(2);
    private string _saldoAnteriorTexto = "R$ 0,00";
    private string _saldoFinalTexto = "R$ 0,00";
    private string _saldoCompensadoTexto = "R$ 0,00";
    private string _entradasTexto = "R$ 0,00";
    private string _saidasTexto = "R$ 0,00";
    private string _periodoTexto = string.Empty;
    private bool _carregando;
    private bool _carregouContas;

    public ExtratoViewModel(
        ExtratoService extrato,
        LancamentoService lancamentos,
        ContaCorrenteService contas,
        CentroCustoService centros,
        ISessaoAtual sessao,
        ServicoDialogos dialogos)
    {
        _extrato = extrato;
        _lancamentos = lancamentos;
        _contas = contas;
        _centros = centros;
        _sessao = sessao;
        _dialogos = dialogos;

        Titulo = "Extrato";

        CarregarCommand = new ComandoAssincrono(CarregarAsync);
        CompensarCommand = new ComandoAssincrono<LancamentoVm>(Compensar);
        AlternarTotaisCommand = new ComandoAssincrono<LancamentoVm>(AlternarTotaisAsync);
        AbrirSelecaoContasCommand = new ComandoAssincrono(AbrirSelecaoContasAsync);

        Ultimos4DiasCommand = new ComandoAssincrono(() =>
        {
            DataInicio = DateTime.Today.AddDays(-4);
            DataFim = DateTime.Today.AddMonths(2);
            return CarregarAsync();
        });

        Ultimos2MesesCommand = new ComandoAssincrono(() =>
        {
            DataInicio = DateTime.Today.AddMonths(-2);
            DataFim = DateTime.Today;
            return CarregarAsync();
        });

        MesPassadoCommand = new ComandoAssincrono(() =>
        {
            var referencia = DateTime.Today.AddMonths(-1);
            DataInicio = new DateTime(referencia.Year, referencia.Month, 1);
            DataFim = new DateTime(referencia.Year, referencia.Month, DateTime.DaysInMonth(referencia.Year, referencia.Month));
            return CarregarAsync();
        });

        MesAtualCommand = new ComandoAssincrono(() =>
        {
            DataInicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DataFim = DateTime.Today;
            return CarregarAsync();
        });
    }

    private IReadOnlyList<LancamentoVm> _itens = [];

    /// <summary>Lista da tela: trocada inteira a cada carga (uma notificação só).</summary>
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
    public ICommand CompensarCommand { get; }
    public ICommand AlternarTotaisCommand { get; }
    public ICommand Ultimos4DiasCommand { get; }
    public ICommand Ultimos2MesesCommand { get; }
    public ICommand MesPassadoCommand { get; }
    public ICommand MesAtualCommand { get; }
    public ICommand AbrirSelecaoContasCommand { get; }

    // ------------------------------------------------------------ seleção de contas

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

    /// <summary>Texto do botão do filtro ("Conta Principal", "3 contas"…).</summary>
    public string ResumoContas
    {
        get => _resumoContas;
        private set => SetProperty(ref _resumoContas, value);
    }

    public DateTime DataInicio
    {
        get => _dataInicio;
        set => SetProperty(ref _dataInicio, value);
    }

    public DateTime DataFim
    {
        get => _dataFim;
        set => SetProperty(ref _dataFim, value);
    }

    public string SaldoAnteriorTexto
    {
        get => _saldoAnteriorTexto;
        private set => SetProperty(ref _saldoAnteriorTexto, value);
    }

    public string SaldoFinalTexto
    {
        get => _saldoFinalTexto;
        private set => SetProperty(ref _saldoFinalTexto, value);
    }

    public string SaldoCompensadoTexto
    {
        get => _saldoCompensadoTexto;
        private set => SetProperty(ref _saldoCompensadoTexto, value);
    }

    public string EntradasTexto
    {
        get => _entradasTexto;
        private set => SetProperty(ref _entradasTexto, value);
    }

    public string SaidasTexto
    {
        get => _saidasTexto;
        private set => SetProperty(ref _saidasTexto, value);
    }

    public string PeriodoTexto
    {
        get => _periodoTexto;
        private set => SetProperty(ref _periodoTexto, value);
    }

    public bool Carregando
    {
        get => _carregando;
        private set
        {
            if (SetProperty(ref _carregando, value))
            {
                AtualizarIndicador(value);
            }
        }
    }

    public bool SemItens => Itens.Count == 0;

    // ------------------------------------------------------------------ carga

    public async Task CarregarAsync()
    {
        if (Carregando) return;
        Carregando = true;

        try
        {
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            var contas = await _contas.ListarAsync();

            if (!_carregouContas)
            {
                _carregouContas = true;

                var inicial = contas.FirstOrDefault(c => c.Id == _sessao.ContaAtivaId) ?? contas.FirstOrDefault();
                _idsContas = inicial is null ? [] : [inicial.Id];
            }

            var selecionadas = contas.Where(c => _idsContas.Contains(c.Id)).ToList();

            if (selecionadas.Count == 0)
            {
                selecionadas = contas.Take(1).ToList();
                _idsContas = selecionadas.Select(c => c.Id).ToList();
            }

            AtualizarResumoContas(selecionadas);

            var inicio = DataInicio;
            var fim = DataFim;
            var idsSelecionados = selecionadas;

            // Driver do SQLite é síncrono: sem Task.Run a consulta inteira roda na thread de
            // UI e o Android trava. Aqui só leitura/cálculo, sem tocar em propriedade da tela.
            var (novos, numeros) = await Task.Run(async () =>
            {
                var centros = (await _centros.ListarAsync(incluirInativos: true)).ToDictionary(c => c.Id);

                decimal anterior = 0m;
                decimal final = 0m;
                decimal compensado = 0m;
                decimal entrada = 0m;
                decimal saida = 0m;

                var linhas = new List<(ContaCorrente Conta, LancamentoExtrato Linha)>();

                // Uma conta = extrato normal; várias = extrato consolidado (saldos somados).
                foreach (var conta in idsSelecionados)
                {
                    var extrato = await _extrato.ObterAsync(conta.Id, inicio, fim);

                    anterior += extrato.SaldoAnterior;
                    final += extrato.SaldoFinal;
                    compensado += extrato.SaldoCompensadoFinal;
                    entrada += extrato.TotalEntradas;
                    saida += extrato.TotalSaidas;

                    linhas.AddRange(extrato.Itens.Select(l => (conta, l)));
                }

                var lista = new List<LancamentoVm>(linhas.Count);
                var acumulado = anterior;

                foreach (var (conta, linha) in linhas
                             .OrderBy(x => x.Linha.Lancamento.DataVencimento)
                             .ThenBy(x => x.Linha.Lancamento.Id))
                {
                    // Saldo previsto linha a linha: pendentes também entram.
                    acumulado += linha.Lancamento.Valor;

                    lista.Add(LancamentoVm.Criar(
                        linha.Lancamento,
                        conta,
                        centros.GetValueOrDefault(linha.Lancamento.CentroCustoId),
                        acumulado,
                        $"{Formatador.Moeda(acumulado)}",
                        linha.ForaDoPeriodo));
                }

                return (lista, (anterior, final, compensado, entrada, saida));
            });

            Itens = novos;

            SaldoAnteriorTexto = Formatador.Moeda(numeros.anterior);
            SaldoFinalTexto = Formatador.Moeda(numeros.final);
            SaldoCompensadoTexto = Formatador.Moeda(numeros.compensado);
            EntradasTexto = Formatador.Moeda(numeros.entrada);
            SaidasTexto = Formatador.Moeda(numeros.saida);
            PeriodoTexto = $"{Formatador.Data(DataInicio)} a {Formatador.Data(DataFim)}";

            System.Diagnostics.Debug.WriteLine(
                $"[perf] Extrato total={relogio.ElapsedMilliseconds}ms contas={selecionadas.Count} linhas={novos.Count}");
        }
        catch (Exception ex)
        {
            await _dialogos.AlertaAsync("Extrato", ex.Message);
        }
        finally
        {
            Carregando = false;
        }
    }

    /// <summary>
    /// Duplo clique na linha de uma série: mostra (ou esconde) total pago, a pagar e geral.
    /// Os totais são buscados uma única vez por linha.
    /// </summary>
    private async Task AlternarTotaisAsync(LancamentoVm? linha)
    {
        if (linha is null || !linha.PodeExpandir)
        {
            return;
        }

        if (linha.TotaisVisiveis)
        {
            linha.TotaisVisiveis = false;
            return;
        }

        if (linha.TotaisCarregados)
        {
            linha.TotaisVisiveis = true;
            return;
        }

        await ExecutarAsync(async () =>
        {
            var totais = await _lancamentos.ObterTotaisDaSerieAsync(linha.ChaveDaSerie);

            linha.PreencherTotais(totais.Pago, totais.APagar, totais.Total);
        });
    }

    // ------------------------------------------------------- seleção (painel)

    private async Task AbrirSelecaoContasAsync()
    {
        var contas = await _contas.ListarAsync();

        if (contas.Count == 0)
        {
            await _dialogos.AlertaAsync("Extrato", "Nenhuma conta disponível.");
            return;
        }

        var idsAtuais = _idsContas;

        var opcoes = contas
            .Select(c => new ContaFiltro
            {
                Id = c.Id,
                Descricao = c.Descricao,
                Icone = c.Icone,
                Principal = c.Principal,
                Selecionada = idsAtuais.Count == 0 || idsAtuais.Contains(c.Id)
            })
            .ToList();

        var selecao = new SelecaoContasViewModel(
            "Contas do extrato",
            "Marque as contas que entram na análise. Com mais de uma, os saldos são somados e os lançamentos aparecem em ordem de vencimento.",
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

    private void AtualizarResumoContas(IReadOnlyList<ContaCorrente> selecionadas)
        => ResumoContas = selecionadas.Count switch
        {
            0 => "Selecione as contas",
            1 => selecionadas[0].Descricao,
            _ => $"{selecionadas.Count} contas"
        };

    // ------------------------------------------------------------------ ações

    private Task Compensar(LancamentoVm? item)
    {
        if (item is null || !item.Pendente) return Task.CompletedTask;

        var painel = new CompensarViewModel(item);

        painel.Confirmou += async (_, data) =>
        {
            Compensacao = null;
            await _lancamentos.CompensarAsync(item.Id, data);
            await CarregarAsync();
        };

        painel.Cancelou += (_, _) => Compensacao = null;

        Compensacao = painel;

        return Task.CompletedTask;
    }

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
}
