using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Entidades;

namespace zFinancas.App.ViewModels;

/// <summary>
/// Dashboard: saudação, saldo consolidado, carrossel de contas e lançamentos dos últimos 7 dias.
/// </summary>
public sealed class DashboardViewModel : BaseViewModel
{
    private readonly DashboardService _dashboard;
    private readonly ContaCorrenteService _contas;
    private readonly CentroCustoService _centros;
    private readonly ISessaoAtual _sessao;
    private readonly ServicoNavegacao _navegacao;
    private readonly ServicoLancamentoPorVoz _vozServico;
    private readonly ServicoDialogos _dialogos;

    private string _saudacao = string.Empty;
    private string _dataTexto = string.Empty;
    private string _totalTexto = "R$ 0,00";
    private string _projetadoTexto = "R$ 0,00";
    private string _compensadoTexto = "R$ 0,00";
    private string _pendenteTexto = "R$ 0,00";
    private string _vencidoTexto = "R$ 0,00";
    private string _entradasTexto = "R$ 0,00";
    private string _saidasTexto = "R$ 0,00";
    private string _periodoInicialTexto = string.Empty;
    private string _periodoFinalTexto = string.Empty;
    private int _posicaoCarrossel;

    public DashboardViewModel(
        DashboardService dashboard,
        ContaCorrenteService contas,
        CentroCustoService centros,
        ISessaoAtual sessao,
        ServicoNavegacao navegacao,
        ServicoLancamentoPorVoz vozServico,
        ServicoDialogos dialogos)
    {
        _dashboard = dashboard;
        _contas = contas;
        _centros = centros;
        _sessao = sessao;
        _navegacao = navegacao;
        _vozServico = vozServico;
        _dialogos = dialogos;

        Titulo = "Dashboard";

        AtualizarCommand = new ComandoAssincrono(CarregarAsync);
        CriarContaCommand = new ComandoAssincrono(
            () => ServicosApp.Obter<ServicoNavegacao>().IrParaSecaoAsync(Secoes.Contas));
        NovoLancamentoCommand = new ComandoAssincrono(() => _navegacao.AbrirLancamentoAsync());
        VerExtratoCommand = new ComandoAssincrono(() => _navegacao.IrParaSecaoAsync(Secoes.Extrato));
        VerLancamentosCommand = new ComandoAssincrono(() => _navegacao.IrParaSecaoAsync(Secoes.Lancamentos));
        FalarCommand = new ComandoAssincrono(FalarAsync);
    }

    public ObservableCollection<ContaCartaoVm> Contas { get; } = [];

    public ObservableCollection<LancamentoVm> UltimosLancamentos { get; } = [];

    public ICommand AtualizarCommand { get; }
    public ICommand NovoLancamentoCommand { get; }
    public ICommand VerExtratoCommand { get; }
    public ICommand VerLancamentosCommand { get; }

    /// <summary>Microfone do dashboard: dita e abre o formulário já preenchido.</summary>
    public ICommand FalarCommand { get; }

    /// <summary>Atalho do estado vazio: leva para o cadastro de contas.</summary>
    public ICommand CriarContaCommand { get; }

    public string Saudacao
    {
        get => _saudacao;
        private set => SetProperty(ref _saudacao, value);
    }

    public string DataTexto
    {
        get => _dataTexto;
        private set => SetProperty(ref _dataTexto, value);
    }

    public string TotalTexto
    {
        get => _totalTexto;
        private set => SetProperty(ref _totalTexto, value);
    }

    public string ProjetadoTexto
    {
        get => _projetadoTexto;
        private set => SetProperty(ref _projetadoTexto, value);
    }

    public string CompensadoTexto
    {
        get => _compensadoTexto;
        private set => SetProperty(ref _compensadoTexto, value);
    }

    public string PendenteTexto
    {
        get => _pendenteTexto;
        private set => SetProperty(ref _pendenteTexto, value);
    }

    public string VencidoTexto
    {
        get => _vencidoTexto;
        private set => SetProperty(ref _vencidoTexto, value);
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

    public string PeriodoInicialTexto
    {
        get => _periodoInicialTexto;
        private set => SetProperty(ref _periodoInicialTexto, value);
    }

    public string PeriodoFinalTexto
    {
        get => _periodoFinalTexto;
        private set => SetProperty(ref _periodoFinalTexto, value);
    }

    public bool SemLancamentos => UltimosLancamentos.Count == 0;

    public bool SemContas => Contas.Count == 0;

    public async Task CarregarAsync()
    {
        await ExecutarAsync(async () =>
        {
            var relogio = System.Diagnostics.Stopwatch.StartNew();

            // Driver do SQLite é síncrono: sem Task.Run a leitura roda na thread de UI
            // (o Android chega a pular centenas de frames na abertura).
            var (resumo, contas, centros) = await Task.Run(async () =>
            {
                var resumoLocal = await _dashboard.ObterAsync();
                var contasLocal = (await _contas.ListarAsync()).ToDictionary(c => c.Id);
                var centrosLocal = (await _centros.ListarAsync()).ToDictionary(c => c.Id);

                return (resumoLocal, contasLocal, centrosLocal);
            });

            var msBanco = relogio.ElapsedMilliseconds;

            Saudacao = $"{PrimeiroNome(_sessao.Usuario?.Nome)}";
            DataTexto = $"{Formatador.Saudacao()}, {DateTime.Now.ToString("dddd, dd 'de' MMMM", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}";

            // O número grande é o dinheiro que está na conta (compensado);
            // a projeção (com pendentes) fica no card ao lado.
            TotalTexto = Formatador.Moeda(resumo.Total.Compensado);
            ProjetadoTexto = Formatador.Moeda(resumo.Total.Total);
            CompensadoTexto = Formatador.Moeda(resumo.Total.Compensado);
            PendenteTexto = Formatador.Moeda(resumo.Total.NaoCompensado);
            VencidoTexto = Formatador.Moeda(resumo.Total.NaoCompensadoVencido);
            EntradasTexto = Formatador.Moeda(resumo.EntradasNaJanela);
            SaidasTexto = Formatador.Moeda(resumo.SaidasNaJanela);
            PeriodoInicialTexto = $"de {Formatador.Data(resumo.InicioJanela)}";
            PeriodoFinalTexto = $" a {Formatador.Data(resumo.FimJanela)}";

            Contas.Clear();
            foreach (var conta in resumo.Contas)
            {
                Contas.Add(ContaCartaoVm.Criar(conta));
            }

            UltimosLancamentos.Clear();
            foreach (var lancamento in Ordenar(resumo.UltimosLancamentos))
            {
                UltimosLancamentos.Add(LancamentoVm.Criar(
                    lancamento,
                    contas.GetValueOrDefault(lancamento.ContaCorrenteId),
                    centros.GetValueOrDefault(lancamento.CentroCustoId)));
            }

            OnPropertyChanged(nameof(SemLancamentos));
            OnPropertyChanged(nameof(SemContas));

            System.Diagnostics.Debug.WriteLine(
                $"[perf] Dashboard total={relogio.ElapsedMilliseconds}ms banco={msBanco}ms " +
                $"contas={resumo.Contas.Count}");
        }, "Não foi possível carregar o dashboard");
    }

    private static IEnumerable<Lancamento> Ordenar(IEnumerable<Lancamento> lancamentos)
        => lancamentos.OrderByDescending(l => l.DataLancamento).ThenByDescending(l => l.Id);

    private static string PrimeiroNome(string? nome)
        => string.IsNullOrWhiteSpace(nome) ? "você" : nome.Trim().Split(' ')[0];

    // ---------------------------------------------------------------- voz

    private bool _ouvindo;

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

    public string RotuloFalar => _ouvindo ? "Ouvindo…" : "Falar";

    /// <summary>
    /// Ouve a frase, interpreta e abre o cadastro já preenchido — nada é gravado
    /// sem a confirmação do usuário.
    /// </summary>
    private async Task FalarAsync()
    {
        if (Ouvindo) return;

        Ouvindo = true;

        try
        {
            var resultado = await _vozServico.OuvirEInterpretarAsync();

            if (!resultado.Sucesso || resultado.Lancamento is null)
            {
                await _dialogos.AlertaAsync(
                    "Não entendi",
                    resultado.Mensagem + "\n\nDica: “gastei 32,50 no mercado” ou “recebi 1.200 de freelance”.");
                return;
            }

            await _navegacao.AbrirLancamentoAsync(null, resultado.Lancamento);
        }
        finally
        {
            Ouvindo = false;
        }
    }
}
