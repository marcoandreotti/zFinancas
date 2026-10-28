using System.Globalization;
using System.Text;
using System.Text.Json;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Seguranca;
using zFinancas.Data.Infra;
using zFinancas.Data.Repositorios;

// ---------------------------------------------------------------------------
// Importa a base do finanzas antigo (Access .mdb) para uma base do zFinancas.
//
// O Access é lido por um dump JSON (o driver ACE só existe em 32 bits):
//   powershell 32-bit + ACE -> pasta com Banco.json, ContaCorrente.json, ...
//   dotnet run --project tools/zFinancas.ImportadorAccess -- <pasta> <saida.db>
//
// Depois abra o app com ZFINANCAS_DB=<saida.db> ou gere um .zfsync pela tela de
// Sincronização para levar ao celular.
// ---------------------------------------------------------------------------

var origem = args.Length > 0
    ? Path.GetFullPath(args[0])
    : throw new ArgumentException("Informe a pasta com os JSON do Access.");

var destino = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.GetFullPath(Path.Combine("migracao", "zfinancas-migrado.db"));

var pacoteSync = args.Length > 2 ? Path.GetFullPath(args[2]) : null;

if (!Directory.Exists(origem))
{
    throw new DirectoryNotFoundException($"Pasta não encontrada: {origem}");
}

foreach (var sufixo in new[] { string.Empty, "-wal", "-shm" })
{
    var arquivo = destino + sufixo;

    if (File.Exists(arquivo))
    {
        File.Delete(arquivo);
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

Console.WriteLine($"Origem : {origem}");
Console.WriteLine($"Destino: {destino}");

var contexto = new ContextoBanco(destino);
await new MigradorBanco(contexto).AplicarAsync();

var contasRepo = new ContaCorrenteRepositorio(contexto);
var centrosRepo = new CentroCustoRepositorio(contexto);
var usuariosRepo = new UsuarioRepositorio(contexto);
var vinculosRepo = new UsuarioContaCorrenteRepositorio(contexto, new RelogioSistema());
var lancamentosRepo = new LancamentoRepositorio(contexto);

// ------------------------------------------------------------------ leitura
var bancos = Ler<Banco>(Path.Combine(origem, "Banco.json"));
var contasAccess = Ler<ContaAccess>(Path.Combine(origem, "ContaCorrente.json"));
var centrosAccess = Ler<CentroAccess>(Path.Combine(origem, "CentroDeCusto.json"));
var usuariosAccess = Ler<UsuarioAccess>(Path.Combine(origem, "Usuario.json"));
var vinculosAccess = Ler<VinculoAccess>(Path.Combine(origem, "UsuarioConta.json"));
var lancamentosAccess = Ler<LancamentoAccess>(Path.Combine(origem, "Lancamentos.json"));

Console.WriteLine($"Lido: {bancos.Count} bancos, {contasAccess.Count} contas, {centrosAccess.Count} centros, " +
                  $"{lancamentosAccess.Count} lançamentos, {usuariosAccess.Count} usuários.");

// ------------------------------------------------------------------- contas
var contasPorAccessId = new Dictionary<int, long>();
var cor = 0;

foreach (var conta in contasAccess.OrderBy(c => c.contaID))
{
    var banco = bancos.FirstOrDefault(b => b.bancoID == conta.bancoID)?.descricao ?? "Banco";
    var numero = (conta.descricao ?? string.Empty).Trim();
    var descricao = Truncar($"{banco} {numero}".Trim(), 40);

    var entidade = new ContaCorrente
    {
        Descricao = descricao,
        Principal = conta.padrao,
        // "exibir" no Access = conta habilitada. As demais entram no histórico, mas inativas.
        Ativo = conta.exibir,
        CorHex = ContaCorrente.PaletaCores[cor++ % ContaCorrente.PaletaCores.Length],
        Icone = IconeDoBanco(banco),
        Ordem = conta.contaID,
        AtualizadoEm = DateTime.UtcNow
    };

    entidade.Id = await contasRepo.SalvarAsync(entidade);
    contasPorAccessId[conta.contaID] = entidade.Id;

    Console.WriteLine($"  conta  {conta.contaID,3} -> {descricao}");
}

// ------------------------------------------------------------------ centros
var centrosPorAccessId = new Dictionary<int, long>();

foreach (var centro in centrosAccess.OrderBy(c => c.custoID))
{
    var entidade = new CentroCusto
    {
        Descricao = Truncar(Titulo(centro.descricao), 40),
        Principal = centro.padrao,
        // No Access todos vinham com exibir=False; dentro do app o centro precisa ser usável.
        Ativo = true,
        Icone = IconeDoCentro(centro.descricao),
        AtualizadoEm = DateTime.UtcNow
    };

    entidade.Id = await centrosRepo.SalvarAsync(entidade);
    centrosPorAccessId[centro.custoID] = entidade.Id;

    Console.WriteLine($"  centro {centro.custoID,3} -> {entidade.Icone} {entidade.Descricao}");
}

// ----------------------------------------------------------------- usuários
var hasher = new SenhaHasher(60_000);
var usuariosPorAccessId = new Dictionary<int, long>();

foreach (var usuario in usuariosAccess)
{
    var senha = (usuario.senha ?? string.Empty).Trim();

    if (senha.Length < 4)
    {
        Console.WriteLine($"  ! senha de '{usuario.nome}' tem menos de 4 caracteres: usando '123456'.");
        senha = "123456";
    }

    var entidade = new Usuario
    {
        Nome = Truncar(Titulo(usuario.nome), 30),
        SenhaHash = hasher.Hash(senha),
        Perfil = string.Equals(usuario.perfil, "A", StringComparison.OrdinalIgnoreCase)
            ? PerfilUsuario.Administrador
            : PerfilUsuario.Usuario,
        Ativo = usuario.ativo,
        DataCadastro = DateTime.UtcNow,
        AtualizadoEm = DateTime.UtcNow
    };

    entidade.Id = await usuariosRepo.SalvarAsync(entidade);
    usuariosPorAccessId[usuario.usuarioID] = entidade.Id;

    Console.WriteLine($"  usuário {usuario.usuarioID} -> {entidade.Nome} ({entidade.Perfil}) senha='{senha}'");
}

// ----------------------------------------------------------------- vínculos
var adicionados = new HashSet<(long, long)>();

foreach (var vinculo in vinculosAccess)
{
    if (!usuariosPorAccessId.TryGetValue(vinculo.usuarioID, out var usuarioId) ||
        !contasPorAccessId.TryGetValue(vinculo.contaID, out var contaId))
    {
        continue;
    }

    if (!adicionados.Add((usuarioId, contaId)))
    {
        continue;
    }

    await vinculosRepo.SalvarVinculosAsync(
    [
        new UsuarioContaCorrente
        {
            UsuarioId = usuarioId,
            ContaCorrenteId = contaId,
            AtualizadoEm = DateTime.UtcNow
        }
    ]);
}

Console.WriteLine($"  vínculos: {adicionados.Count}");

// -------------------------------------------------------------- lançamentos
var agrupadores = new Dictionary<int, string>();
var lote = new List<Lancamento>(512);
var importados = 0;
var ignorados = 0;
var problemas = new List<string>();

foreach (var l in lancamentosAccess.OrderBy(l => l.lancamentoID))
{
    if (!contasPorAccessId.TryGetValue(l.contaID, out var contaId) ||
        !centrosPorAccessId.TryGetValue(l.custoID, out var centroId))
    {
        ignorados++;
        continue;
    }

    var descricao = Truncar(Limpar(l.historico), Lancamento.TamanhoMaximoDescricao);

    if (string.IsNullOrWhiteSpace(descricao))
    {
        descricao = "Lançamento importado";
    }

    if (l.valor == 0)
    {
        ignorados++;
        continue;
    }

    var total = Math.Max(1, (int)l.totalParcelas);
    var parcela = Math.Clamp(l.parcela <= 0 ? 1 : l.parcela, 1, total);

    // No app antigo a data que manda é a da COMPENSAÇÃO (é ela que ordena a lista e
    // alimenta o saldo). Para o que ainda não foi compensado, fica o vencimento.
    var vencimentoOriginal = l.dataVencimento ?? l.dataLancamento ?? DateTime.Today;
    var dataCompensado = l.dataCompensado ?? vencimentoOriginal;
    var dataBase = l.compensado ? dataCompensado : vencimentoOriginal;

    var entidade = new Lancamento
    {
        ContaCorrenteId = contaId,
        CentroCustoId = centroId,
        Descricao = descricao,
        Valor = (decimal)Math.Round(l.valor, 2),
        DataLancamento = l.compensado ? dataCompensado : (l.dataLancamento ?? dataBase),
        DataVencimento = dataBase,
        Compensado = l.compensado,
        DataCompensado = l.compensado ? dataCompensado : null,
        Parcela = parcela,
        QuantidadeParcelas = total,
        Agrupador = l.agrupador,
        // UUID só faz sentido em série; avulso fica sem (senão vira "série" de 1 parcela).
        AgrupadorUuid = total > 1 ? UuidDoAgrupador(l.agrupador, agrupadores) : null,
        AtualizadoEm = DateTime.UtcNow
    };

    try
    {
        entidade.Validar();
    }
    catch (Exception ex)
    {
        if (problemas.Count < 10)
        {
            problemas.Add($"#{l.lancamentoID}: {ex.Message}");
        }

        ignorados++;
        continue;
    }

    lote.Add(entidade);
    importados++;

    if (lote.Count >= 500)
    {
        await lancamentosRepo.SalvarVariosAsync(lote);
        lote.Clear();
    }
}

if (lote.Count > 0)
{
    await lancamentosRepo.SalvarVariosAsync(lote);
}

Console.WriteLine();
Console.WriteLine($"Lançamentos importados: {importados}");
Console.WriteLine($"Ignorados (conta/centro inexistente ou inválido): {ignorados}");

foreach (var problema in problemas)
{
    Console.WriteLine($"  ! {problema}");
}

// ------------------------------------------------------------- conferência
var totalImportado = (await lancamentosRepo.ListarTodosIncluindoExcluidosAsync()).Sum(l => l.Valor);
var totalAccess = lancamentosAccess.Sum(l => (decimal)Math.Round(l.valor, 2));

Console.WriteLine();
Console.WriteLine($"Soma dos valores no Access : {totalAccess:N2}");
Console.WriteLine($"Soma dos valores importados: {totalImportado:N2}");
Console.WriteLine(totalAccess == totalImportado ? "OK: totais batem." : "ATENÇÃO: totais diferentes.");

// Pacote .zfsync: permite importar no celular pela tela de Sincronizacao.
if (pacoteSync is not null)
{
    var sincronizacao = new zFinancas.Core.Aplicacao.Servicos.SincronizacaoService(
        usuariosRepo, contasRepo, centrosRepo, lancamentosRepo, vinculosRepo,
        new ConfiguracaoRepositorio(contexto, new RelogioSistema()), new SerializadorJson(),
        new DispositivoMigracao(), new UnidadeDeTrabalho(contexto), new RelogioSistema());

    var json = await sincronizacao.ExportarTextoAsync();

    Directory.CreateDirectory(Path.GetDirectoryName(pacoteSync)!);
    await File.WriteAllTextAsync(pacoteSync, json);

    Console.WriteLine();
    Console.WriteLine($"Pacote de sincronizacao: {pacoteSync} ({new FileInfo(pacoteSync).Length / 1024} KB)");
}

try
{
    await contexto.DisposeAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"! ao fechar o contexto: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine($"Pronto: {destino}");
Console.WriteLine("Abra o app com  ZFINANCAS_DB=" + destino);

// ------------------------------------------------------------------ helpers
static string UuidDoAgrupador(int agrupador, Dictionary<int, string> cache)
{
    if (agrupador == 0)
    {
        return Guid.NewGuid().ToString("N");
    }

    if (!cache.TryGetValue(agrupador, out var uuid))
    {
        uuid = Guid.NewGuid().ToString("N");
        cache[agrupador] = uuid;
    }

    return uuid;
}

static string Limpar(string? texto)
{
    if (string.IsNullOrWhiteSpace(texto))
    {
        return string.Empty;
    }

    var sb = new StringBuilder(texto.Length);

    foreach (var c in texto)
    {
        sb.Append(char.IsControl(c) ? ' ' : c);
    }

    return RepararAcentos(sb.ToString().Trim());
}

/// <summary>O Access antigo gravou alguns textos em ANSI/Latin-1 dentro de UTF-8.</summary>
static string RepararAcentos(string texto)
{
    if (!texto.Contains('Ã') && !texto.Contains('Â'))
    {
        return texto;
    }

    try
    {
        var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(texto);
        var tentativa = Encoding.UTF8.GetString(bytes);

        return tentativa.Contains('\uFFFD') ? texto : tentativa;
    }
    catch
    {
        return texto;
    }
}

static string Titulo(string? texto)
{
    if (string.IsNullOrWhiteSpace(texto))
    {
        return string.Empty;
    }

    var minusculo = RepararAcentos(texto.Trim()).ToLower(new CultureInfo("pt-BR"));
    return new CultureInfo("pt-BR").TextInfo.ToTitleCase(minusculo);
}

static string Truncar(string texto, int limite)
    => texto.Length <= limite ? texto : texto[..limite];

static string IconeDoBanco(string banco) => banco.ToUpperInvariant() switch
{
    var b when b.Contains("NU") => "💜",
    var b when b.Contains("INTER") => "🧡",
    var b when b.Contains("BRADESCO") => "🔴",
    var b when b.Contains("SANTANDER") => "🟥",
    var b when b.Contains("CAIXA") => "🔵",
    var b when b.Contains("BRASIL") => "🟡",
    var b when b.Contains("BTG") => "⚫",
    var b when b.Contains("HSBC") => "🟥",
    _ => "🏦"
};

static string IconeDoCentro(string? nome)
{
    var n = (nome ?? string.Empty).ToUpperInvariant();

    return n switch
    {
        _ when n.Contains("AGUA") || n.Contains("ÁGUA") => "💧",
        _ when n.Contains("LUZ") => "💡",
        _ when n.Contains("TELEFONE") || n.Contains("INTERNET") => "📱",
        _ when n.Contains("ALUGUEL") || n.Contains("ALUGUÉL") => "🏠",
        _ when n.Contains("CARTAO") || n.Contains("CARTÃO") => "💳",
        _ when n.Contains("SALARIO") || n.Contains("SALÁRIO") || n.Contains("RECEBIMENTO") => "💰",
        _ when n.Contains("ALMOCO") || n.Contains("ALMOÇO") => "🍽️",
        _ when n.Contains("VIAGEM") => "✈️",
        _ when n.Contains("GAS") || n.Contains("GÁS") => "🔥",
        _ when n.Contains("MERCADO") => "🛒",
        _ when n.Contains("SAUDE") || n.Contains("SAÚDE") => "💊",
        _ when n.Contains("VEICULO") || n.Contains("VEÍCULO") => "🚗",
        _ when n.Contains("EDUCA") || n.Contains("EDUCAÇÃO") => "🎓",
        _ when n.Contains("FINANCIAMENTO") => "🏦",
        _ when n.Contains("ASSINATURA") => "📄",
        _ when n.Contains("TAXA") || n.Contains("CPMF") => "🧾",
        _ when n.Contains("LAZER") || n.Contains("EDUARDO") => "🎮",
        _ => "🧾"
    };
}

static List<T> Ler<T>(string caminho) where T : new()
{
    if (!File.Exists(caminho))
    {
        Console.WriteLine($"! arquivo ausente: {Path.GetFileName(caminho)}");
        return [];
    }

    using var fluxo = File.OpenRead(caminho);
    var pacote = JsonSerializer.Deserialize<Dump<T>>(fluxo, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });

    return pacote?.linhas ?? [];
}

internal sealed class DispositivoMigracao : zFinancas.Core.Abstracoes.IInfoDispositivo
{
    public string Nome => "Importação Access";

    public string Plataforma => "Windows";

    public bool EhMovel => false;
}

// ------------------------------------------------------------ modelos JSON
internal sealed class Dump<T>
{
    public string? tabela { get; set; }

    public int total { get; set; }

    public List<T>? linhas { get; set; }
}

internal sealed class Banco
{
    public int bancoID { get; set; }

    public string? descricao { get; set; }
}

internal sealed class ContaAccess
{
    public int contaID { get; set; }

    public int bancoID { get; set; }

    public string? descricao { get; set; }

    public bool padrao { get; set; }

    public bool exibir { get; set; }
}

internal sealed class CentroAccess
{
    public int custoID { get; set; }

    public string? descricao { get; set; }

    public bool padrao { get; set; }

    public bool exibir { get; set; }
}

internal sealed class UsuarioAccess
{
    public int usuarioID { get; set; }

    public string? nome { get; set; }

    public string? senha { get; set; }

    public bool ativo { get; set; }

    public string? perfil { get; set; }
}

internal sealed class VinculoAccess
{
    public int usuarioID { get; set; }

    public int contaID { get; set; }
}

internal sealed class LancamentoAccess
{
    public int lancamentoID { get; set; }

    public int custoID { get; set; }

    public int contaID { get; set; }

    public DateTime? dataLancamento { get; set; }

    public DateTime? dataVencimento { get; set; }

    public DateTime? dataCompensado { get; set; }

    public string? historico { get; set; }

    public double valor { get; set; }

    public bool compensado { get; set; }

    public int agrupador { get; set; }

    public short parcela { get; set; }

    public short totalParcelas { get; set; }
}
