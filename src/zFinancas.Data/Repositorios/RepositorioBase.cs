using System.Globalization;
using Microsoft.Data.Sqlite;
using zFinancas.Data.Infra;

namespace zFinancas.Data.Repositorios;

/// <summary>Utilidades comuns de acesso a dados: execução, conversão e leitura.</summary>
public abstract class RepositorioBase
{
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    protected RepositorioBase(ContextoBanco contexto)
    {
        Contexto = contexto;
    }

    protected ContextoBanco Contexto { get; }

    /// <summary>Executa dentro da região crítica e da transação corrente.</summary>
    protected async Task<T> ExecutarAsync<T>(
        Func<SqliteConnection, CancellationToken, Task<T>> operacao,
        CancellationToken ct)
    {
        // Dentro de uma unidade de trabalho o escopo já pertence a este fluxo:
        // usar a mesma conexão/transação sem tentar reentrar no semáforo.
        if (Contexto.SouDonoDoEscopo)
        {
            var conexaoDoEscopo = await Contexto.ObterConexaoAsync(ct).ConfigureAwait(false);
            return await operacao(conexaoDoEscopo, ct).ConfigureAwait(false);
        }

        using var _ = await Contexto.EntrarAsync(ct).ConfigureAwait(false);
        Contexto.AssumirEscopoNesteFluxo();

        var conexao = await Contexto.ObterConexaoAsync(ct).ConfigureAwait(false);
        return await operacao(conexao, ct).ConfigureAwait(false);
    }

    protected async Task<int> ExecutarComandoAsync(
        string sql,
        Action<SqliteCommand>? parametros,
        CancellationToken ct)
    {
        return await ExecutarAsync(async (_, token) =>
        {
            await using var comando = await Contexto.CriarComandoAsync(sql, token).ConfigureAwait(false);
            parametros?.Invoke(comando);
            return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    protected async Task<object?> ExecutarEscalarAsync(
        string sql,
        Action<SqliteCommand>? parametros,
        CancellationToken ct)
    {
        return await ExecutarAsync(async (_, token) =>
        {
            await using var comando = await Contexto.CriarComandoAsync(sql, token).ConfigureAwait(false);
            parametros?.Invoke(comando);
            return await comando.ExecuteScalarAsync(token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    protected async Task<List<T>> ConsultarAsync<T>(
        string sql,
        Action<SqliteCommand>? parametros,
        Func<SqliteDataReader, T> mapear,
        CancellationToken ct)
    {
        return await ExecutarAsync(async (_, token) =>
        {
            await using var comando = await Contexto.CriarComandoAsync(sql, token).ConfigureAwait(false);
            parametros?.Invoke(comando);

            var itens = new List<T>();
            await using var leitor = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await leitor.ReadAsync(token).ConfigureAwait(false))
            {
                itens.Add(mapear(leitor));
            }

            return itens;
        }, ct).ConfigureAwait(false);
    }

    protected async Task<T?> ConsultarUmAsync<T>(
        string sql,
        Action<SqliteCommand>? parametros,
        Func<SqliteDataReader, T> mapear,
        CancellationToken ct)
        where T : class
    {
        var itens = await ConsultarAsync(sql, parametros, mapear, ct).ConfigureAwait(false);
        return itens.Count > 0 ? itens[0] : null;
    }

    // ------------------------------------------------------------------ conversões

    /// <summary>Datas em texto ISO-8601 preservando o <see cref="DateTimeKind"/>.</summary>
    protected static string Texto(DateTime valor) => valor.ToString("o", Invariante);

    protected static object Texto(object? valor) => valor switch
    {
        null => DBNull.Value,
        DateTime data => data.ToString("o", Invariante),
        decimal numero => numero.ToString(Invariante),
        bool flag => flag ? 1 : 0,
        string texto => texto,
        _ => valor.ToString() ?? string.Empty
    };

    protected static DateTime LerData(SqliteDataReader leitor, int indice)
        => DateTime.Parse(leitor.GetString(indice), Invariante, DateTimeStyles.RoundtripKind);

    protected static DateTime? LerDataOpcional(SqliteDataReader leitor, int indice)
        => leitor.IsDBNull(indice)
            ? null
            : DateTime.Parse(leitor.GetString(indice), Invariante, DateTimeStyles.RoundtripKind);

    protected static decimal LerDecimal(SqliteDataReader leitor, int indice)
        => leitor.IsDBNull(indice)
            ? 0m
            : decimal.Parse(leitor.GetString(indice), NumberStyles.Any, Invariante);

    protected static bool LerBooleano(SqliteDataReader leitor, int indice)
        => !leitor.IsDBNull(indice) && leitor.GetInt64(indice) != 0;

    protected static string? LerTextoOpcional(SqliteDataReader leitor, int indice)
        => leitor.IsDBNull(indice) ? null : leitor.GetString(indice);
}
