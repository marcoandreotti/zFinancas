using zFinancas.Core.Aplicacao;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Servicos;
using zFinancas.Core.Seguranca;
using zFinancas.Data.Infra;
using zFinancas.Data.Repositorios;

namespace zFinancas.Tests.Apoio;

/// <summary>
/// Monta o aplicativo inteiro (Core + Data) sobre um banco SQLite temporário,
/// permitindo testar os fluxos reais sem UI é mesmo grafo de objetos usado em produção.
/// </summary>
public sealed class CenarioTeste : IAsyncDisposable
{
    private readonly string _caminhoBanco;

    private CenarioTeste(string caminhoBanco)
    {
        _caminhoBanco = caminhoBanco;
        Relogio = new RelogioFake(new DateTime(2025, 3, 15));
        Hasher = new SenhaHasher(10_000); // menos iterações: testes rápidos, mesma lógica
        Sessao = new SessaoAtual();

        Contexto = new ContextoBanco(caminhoBanco);
        Migrador = new MigradorBanco(Contexto);

        UsuarioRepositorio = new UsuarioRepositorio(Contexto);
        ContaRepositorio = new ContaCorrenteRepositorio(Contexto);
        CentroRepositorio = new CentroCustoRepositorio(Contexto);
        VinculoRepositorio = new UsuarioContaCorrenteRepositorio(Contexto, Relogio);
        LancamentoRepositorio = new LancamentoRepositorio(Contexto);
        ConfiguracaoRepositorio = new ConfiguracaoRepositorio(Contexto, Relogio);

        UnidadeDeTrabalho = new UnidadeDeTrabalho(Contexto);
        Serializador = new SerializadorJson();

        PrimeiroAcesso = new PrimeiroAcessoService(
            UsuarioRepositorio, ContaRepositorio, CentroRepositorio, UnidadeDeTrabalho, Hasher, Relogio);

        Autenticacao = new AutenticacaoService(
            UsuarioRepositorio, ContaRepositorio, VinculoRepositorio, Sessao, Hasher, Relogio);

        Usuarios = new UsuarioService(UsuarioRepositorio, VinculoRepositorio, UnidadeDeTrabalho, Hasher, Relogio);
        Contas = new ContaCorrenteService(ContaRepositorio, UnidadeDeTrabalho, Relogio);
        Centros = new CentroCustoService(CentroRepositorio, UnidadeDeTrabalho, Relogio);

        Calculadora = new CalculadoraDeSaldo();
        Lancamentos = new LancamentoService(
            LancamentoRepositorio, UnidadeDeTrabalho, new GeradorDeParcelas(), new PoliticaDeQuitacao(), Sessao, Relogio);

        Extrato = new ExtratoService(LancamentoRepositorio, Calculadora, Sessao, Relogio);
        Dashboard = new DashboardService(LancamentoRepositorio, Calculadora, Sessao, Relogio);

        Sincronizacao = new SincronizacaoService(
            UsuarioRepositorio, ContaRepositorio, CentroRepositorio, LancamentoRepositorio,
            VinculoRepositorio, ConfiguracaoRepositorio, Serializador, Dispositivo, UnidadeDeTrabalho, Relogio);
    }

    public RelogioFake Relogio { get; }

    public SenhaHasher Hasher { get; }

    public SessaoAtual Sessao { get; }

    public DispositivoFake Dispositivo { get; } = new();

    public ContextoBanco Contexto { get; }

    public MigradorBanco Migrador { get; }

    public UsuarioRepositorio UsuarioRepositorio { get; }

    public ContaCorrenteRepositorio ContaRepositorio { get; }

    public CentroCustoRepositorio CentroRepositorio { get; }

    public UsuarioContaCorrenteRepositorio VinculoRepositorio { get; }

    public LancamentoRepositorio LancamentoRepositorio { get; }

    public ConfiguracaoRepositorio ConfiguracaoRepositorio { get; }

    public UnidadeDeTrabalho UnidadeDeTrabalho { get; }

    public SerializadorJson Serializador { get; }

    public PrimeiroAcessoService PrimeiroAcesso { get; }

    public AutenticacaoService Autenticacao { get; }

    public UsuarioService Usuarios { get; }

    public ContaCorrenteService Contas { get; }

    public CentroCustoService Centros { get; }

    public CalculadoraDeSaldo Calculadora { get; }

    public LancamentoService Lancamentos { get; }

    public ExtratoService Extrato { get; }

    public DashboardService Dashboard { get; }

    public SincronizacaoService Sincronizacao { get; }

    public static async Task<CenarioTeste> CriarAsync(DateTime? hoje = null)
    {
        var pasta = Path.Combine(Path.GetTempPath(), "zfinancas-testes");
        Directory.CreateDirectory(pasta);

        var caminho = Path.Combine(pasta, $"zfinancas-{Guid.NewGuid():N}.db");
        var cenario = new CenarioTeste(caminho);

        if (hoje is not null)
        {
            cenario.Relogio.Hoje = hoje.Value.Date;
            cenario.Relogio.AgoraUtc = DateTime.SpecifyKind(hoje.Value.Date.AddHours(12), DateTimeKind.Utc);
        }

        await cenario.Migrador.AplicarAsync();
        return cenario;
    }

    // ------------------------------------------------------------------ atalhos

    /// <summary>Cria o administrador e já autentica a sessão.</summary>
    /// <summary>
    /// Cria o ADM e, por conveniência dos testes, uma conta e um centro principais.
    /// (O app real não semeia mais nada: a base nasce vazia, ver PrimeiroAcessoService.)
    /// </summary>
    public async Task<Usuario> ConfigurarAdministradorAsync(string nome = "Marco", string senha = "senha123")
    {
        var resultado = await PrimeiroAcesso.ConfigurarAsync(new RequisicaoConfiguracaoInicial(nome, senha));
        await Autenticacao.EntrarAsync(resultado.Administrador.Id, senha);

        await CriarContaAsync("Conta Principal", principal: true);
        await CriarCentroAsync("Geral", principal: true);

        return resultado.Administrador;
    }

    public async Task<ContaCorrente> CriarContaAsync(string descricao = "Carteira", bool principal = false)
    {
        var conta = await Contas.CriarAsync(new RequisicaoConta(0, descricao, "#6C5BFF", "🏦", principal, true));
        await SessaoAtualizarContasAsync();
        return conta;
    }

    public async Task<CentroCusto> CriarCentroAsync(string descricao = "Alimentação", bool principal = false)
        => await Centros.CriarAsync(new RequisicaoCentroCusto(0, descricao, "🍕", principal, true));

    public async Task<Lancamento> LancarAsync(
        long contaId,
        long centroId,
        decimal valor,
        DateTime vencimento,
        string descricao = "Lançamento",
        Recorrencia recorrencia = Recorrencia.Unica,
        int parcelas = 1,
        bool compensado = false,
        DateTime? dataCompensado = null,
        DateTime? dataLancamento = null)
    {
        var resultado = await Lancamentos.CriarAsync(new RequisicaoLancamento(
            0, contaId, centroId, descricao, valor, vencimento,
            recorrencia, parcelas, compensado, dataCompensado, null, dataLancamento));

        return resultado.Lancamentos[0];
    }

    /// <summary>Recarrega as contas permitidas na sessão após criar/alterar contas.</summary>
    public async Task SessaoAtualizarContasAsync()
    {
        if (Sessao.Usuario is null) return;

        var usuario = await UsuarioRepositorio.ObterPorIdAsync(Sessao.Usuario.Id);
        if (usuario is null) return;

        var contas = await ResolverContasAsync(usuario);
        Sessao.Iniciar(usuario, contas, Sessao.ContaAtivaId ?? contas.FirstOrDefault()?.Id);
    }

    private async Task<IReadOnlyList<ContaCorrente>> ResolverContasAsync(Usuario usuario)
    {
        var ativas = await ContaRepositorio.ListarAsync(incluirInativas: false);
        if (usuario.EhAdministrador) return ativas;

        var vinculadas = await VinculoRepositorio.ListarContasDoUsuarioAsync(usuario.Id);
        var permitidas = new HashSet<long>(vinculadas);
        return ativas.Where(c => permitidas.Contains(c.Id)).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await Contexto.DisposeAsync();

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (File.Exists(_caminhoBanco)) File.Delete(_caminhoBanco);
            if (File.Exists(_caminhoBanco + "-wal")) File.Delete(_caminhoBanco + "-wal");
            if (File.Exists(_caminhoBanco + "-shm")) File.Delete(_caminhoBanco + "-shm");
        }
        catch (IOException)
        {
            // arquivo temporário: a limpeza é best-effort
        }
    }
}
