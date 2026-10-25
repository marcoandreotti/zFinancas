using zFinancas.Core.Aplicacao;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Servicos;
using zFinancas.Core.Seguranca;
using zFinancas.Data.Infra;
using zFinancas.Data.Repositorios;

// ---------------------------------------------------------------------------
// Cria um banco de demonstração do zFinancas.
// Uso:  dotnet run --project tools/zFinancas.Demo -- [caminho-do-banco] [--zerado]
// Depois abra o app definindo:  ZFINANCAS_DB=<caminho>
//   --zerado gera apenas o esquema (banco de fábrica, sem dados).
// ---------------------------------------------------------------------------

var somenteEsquema = args.Any(a => a.Equals("--zerado", StringComparison.OrdinalIgnoreCase));
var argumento = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
var caminho = argumento is not null
    ? Path.GetFullPath(argumento)
    : Path.GetFullPath(Path.Combine("demo", "zfinancas-demo.db"));

Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);

foreach (var sufixo in new[] { string.Empty, "-wal", "-shm" })
{
    var arquivo = caminho + sufixo;
    if (File.Exists(arquivo)) File.Delete(arquivo);
}


Console.WriteLine($"Criando banco de demonstração em: {caminho}");

var contexto = new ContextoBanco(caminho);

// Banco de fábrica: apenas o esquema, sem dados.
if (somenteEsquema)
{
    await new MigradorBanco(contexto).AplicarAsync();
    await contexto.DisposeAsync();
    Console.WriteLine($"Banco zerado (somente esquema): {caminho}");
    return;
}
var relogio = new RelogioSistema();
var hasher = new SenhaHasher(60_000);
var sessao = new SessaoAtual();

await new MigradorBanco(contexto).AplicarAsync();

var usuarios = new UsuarioRepositorio(contexto);
var contasRepo = new ContaCorrenteRepositorio(contexto);
var centrosRepo = new CentroCustoRepositorio(contexto);
var vinculosRepo = new UsuarioContaCorrenteRepositorio(contexto, relogio);
var lancamentosRepo = new LancamentoRepositorio(contexto);
var unidade = new UnidadeDeTrabalho(contexto);

var primeiroAcesso = new PrimeiroAcessoService(usuarios, contasRepo, centrosRepo, unidade, hasher, relogio);
var autenticacao = new AutenticacaoService(usuarios, contasRepo, vinculosRepo, sessao, hasher, relogio);
var usuarioService = new UsuarioService(usuarios, vinculosRepo, unidade, hasher, relogio);
var contasService = new ContaCorrenteService(contasRepo, unidade, relogio);
var centrosService = new CentroCustoService(centrosRepo, unidade, relogio);
var lancamentosService = new LancamentoService(
    lancamentosRepo, unidade, new GeradorDeParcelas(), new PoliticaDeQuitacao(), sessao, relogio);

const string senhaAdmin = "demo1234";
const string senhaAna = "ana1234";

var administrador = await primeiroAcesso.ConfigurarAsync(
    new RequisicaoConfiguracaoInicial("Marco Aurélio", senhaAdmin, "apelido de infância"));

await autenticacao.EntrarAsync(administrador.Administrador.Id, senhaAdmin);

// ------------------------------------------------------------------ contas
var principal = (await contasRepo.ListarAsync())[0];
principal.Alterar("Conta Principal", "#6C5BFF", "🏦", relogio.AgoraUtc);
await contasRepo.SalvarAsync(principal);

var poupanca = await contasService.CriarAsync(
    new RequisicaoConta(0, "Poupança", "#2ED3A0", "🐖", false, true));

var cartao = await contasService.CriarAsync(
    new RequisicaoConta(0, "Cartão Crédito", "#FF5C7A", "💳", false, true));

// ---------------------------------------------------------- centros de custo
var geral = (await centrosRepo.ListarAsync())[0];
geral.Alterar("Geral", "🧾", relogio.AgoraUtc);
await centrosRepo.SalvarAsync(geral);

var moradia = await centrosService.CriarAsync(new RequisicaoCentroCusto(0, "Moradia", "🏠", true, true));
var alimentacao = await centrosService.CriarAsync(new RequisicaoCentroCusto(0, "Alimentação", "🍕", false, true));
var transporte = await centrosService.CriarAsync(new RequisicaoCentroCusto(0, "Transporte", "🚗", false, true));
var saude = await centrosService.CriarAsync(new RequisicaoCentroCusto(0, "Saúde", "💊", false, true));
var lazer = await centrosService.CriarAsync(new RequisicaoCentroCusto(0, "Lazer", "🎮", false, true));

// --------------------------------------------------------------- usuário USR
var (ana, _) = await usuarioService.CriarAsync(new RequisicaoUsuario(
    0, "Ana", senhaAna, "time do coração", PerfilUsuario.Usuario, true, [poupanca.Id]));

// A sessão do administrador precisa enxergar as contas recém-criadas antes de lançar.
await autenticacao.EntrarAsync(administrador.Administrador.Id, senhaAdmin);

// ---------------------------------------------------------------- lançamentos
var hoje = relogio.Hoje;

async Task Lancar(
    long contaId, long centroId, decimal valor, int diasAtras, string descricao,
    bool compensado = true, Recorrencia recorrencia = Recorrencia.Unica, int parcelas = 1)
{
    var data = hoje.AddDays(-diasAtras);

    await lancamentosService.CriarAsync(new RequisicaoLancamento(
        0, contaId, centroId, descricao, valor, data,
        recorrencia, parcelas, compensado, compensado ? data : null, null, data));
}

// receitas
await Lancar(principal.Id, geral.Id, 8_450.00m, 12, "Salário mensal");
await Lancar(principal.Id, geral.Id, 1_280.00m, 5, "Freelance - landing page");
await Lancar(poupanca.Id, geral.Id, 400.00m, 2, "Rendimento CDB");

// despesas compensadas
await Lancar(principal.Id, moradia.Id, -1_950.00m, 11, "Aluguel");
await Lancar(principal.Id, alimentacao.Id, -486.32m, 9, "Mercado do mês");
await Lancar(principal.Id, transporte.Id, -180.00m, 8, "Combustível");
await Lancar(principal.Id, saude.Id, -249.90m, 6, "Plano de saúde");
await Lancar(principal.Id, lazer.Id, -120.00m, 1, "Cinema e jantar");
await Lancar(cartao.Id, lazer.Id, -79.90m, 4, "Streaming anual", recorrencia: Recorrencia.Anual, parcelas: 1);
await Lancar(cartao.Id, alimentacao.Id, -132.45m, 3, "Pizza com a família");

// despesas a compensar (algumas vencidas, para ver o destaque no extrato)
await Lancar(principal.Id, moradia.Id, -1_950.00m, 0, "Aluguel", compensado: false);
await Lancar(principal.Id, transporte.Id, -320.00m, 22, "IPVA parcela 3/4", compensado: false);
await Lancar(cartao.Id, alimentacao.Id, -245.70m, 18, "Supermercado", compensado: false);

// série mensal (agrupador/parcelas)
var serie = await lancamentosService.CriarAsync(new RequisicaoLancamento(
    0, principal.Id, saude.Id, "Dentista - parcelado", -380.00m, hoje.AddDays(-40),
    Recorrencia.Mensal, 4, false, null, null, hoje.AddDays(-40)));

await lancamentosService.CompensarAsync(serie.Lancamentos[0].Id, hoje.AddDays(-40));

// série com quitação consolidada (demonstra a regra de negócio)
var curso = await lancamentosService.CriarAsync(new RequisicaoLancamento(
    0, principal.Id, lazer.Id, "Curso de inglês", -260.00m, hoje.AddDays(-70),
    Recorrencia.Mensal, 6, false, null, null, hoje.AddDays(-70)));

await lancamentosService.CompensarAsync(curso.Lancamentos[0].Id, hoje.AddDays(-70));

var plano = await lancamentosService.PlanejarQuitacaoAsync(
    curso.Lancamentos.Skip(1).Take(3).Select(l => l.Id), hoje.AddDays(-20), null);

await lancamentosService.QuitarAsync(plano);

// --------------------------------------------------------------------- resumo
var todasContas = await contasRepo.ListarAsync(incluirInativas: true);
var todosLancamentos = await lancamentosRepo.ListarTodosIncluindoExcluidosAsync();
var ativos = todosLancamentos.Where(l => !l.Excluido).ToList();

Console.WriteLine();
Console.WriteLine("Banco de demonstração criado com sucesso.");
Console.WriteLine($"  Contas: {todasContas.Count}  |  Centros de custo: {(await centrosRepo.ListarAsync(true)).Count}");
Console.WriteLine($"  Lançamentos ativos: {ativos.Count} (compensados: {ativos.Count(l => l.Compensado)})");
Console.WriteLine($"  Série parcelada: {serie.Lancamentos.Count} parcelas (agrupador {serie.Agrupador})");
Console.WriteLine();
Console.WriteLine("Acessos:");
Console.WriteLine($"  ADM  Marco Aurélio  senha: {senhaAdmin}");
Console.WriteLine($"  USR  Ana             senha: {senhaAna}  (vê apenas a conta Poupança)");
Console.WriteLine();
Console.WriteLine($"Abra o app com:  $env:ZFINANCAS_DB='{caminho}'");

await contexto.DisposeAsync();

_ = ana;
