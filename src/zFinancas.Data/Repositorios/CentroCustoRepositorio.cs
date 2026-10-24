using Microsoft.Data.Sqlite;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Data.Infra;

namespace zFinancas.Data.Repositorios;

public sealed class CentroCustoRepositorio : RepositorioBase, ICentroCustoRepositorio
{
    private const string Colunas = "ID, DESCRICAO, PRINCIPAL, ICONE, ATIVO, UUID, ATUALIZADOEM, EXCLUIDO";

    public CentroCustoRepositorio(ContextoBanco contexto) : base(contexto)
    {
    }

    public Task<CentroCusto?> ObterPorIdAsync(long id, CancellationToken ct = default)
        => ConsultarUmAsync(
            $"SELECT {Colunas} FROM CENTRODECUSTO WHERE ID = $id AND EXCLUIDO = 0;",
            c => c.Parameters.AddWithValue("$id", id),
            Mapear,
            ct);

    public async Task<IReadOnlyList<CentroCusto>> ListarAsync(bool incluirInativos = false, CancellationToken ct = default)
    {
        var sql = incluirInativos
            ? $"SELECT {Colunas} FROM CENTRODECUSTO WHERE EXCLUIDO = 0 ORDER BY PRINCIPAL DESC, DESCRICAO COLLATE NOCASE;"
            : $"SELECT {Colunas} FROM CENTRODECUSTO WHERE EXCLUIDO = 0 AND ATIVO = 1 ORDER BY PRINCIPAL DESC, DESCRICAO COLLATE NOCASE;";

        return await ConsultarAsync(sql, null, Mapear, ct).ConfigureAwait(false);
    }

    public Task<CentroCusto?> ObterPrincipalAsync(CancellationToken ct = default)
        => ConsultarUmAsync(
            $"SELECT {Colunas} FROM CENTRODECUSTO WHERE EXCLUIDO = 0 AND ATIVO = 1 AND PRINCIPAL = 1 ORDER BY ID LIMIT 1;",
            null,
            Mapear,
            ct);

    public async Task<long> SalvarAsync(CentroCusto centroCusto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(centroCusto);
        centroCusto.Validar();

        if (centroCusto.Id == 0)
        {
            await ExecutarComandoAsync(
                """
                INSERT INTO CENTRODECUSTO
                    (DESCRICAO, PRINCIPAL, ICONE, ATIVO, UUID, ATUALIZADOEM, EXCLUIDO)
                VALUES
                    ($descricao, $principal, $icone, $ativo, $uuid, $atualizado, $excluido);
                """,
                c =>
                {
                    c.Parameters.AddWithValue("$descricao", centroCusto.Descricao);
                    c.Parameters.AddWithValue("$principal", centroCusto.Principal ? 1 : 0);
                    c.Parameters.AddWithValue("$icone", centroCusto.Icone);
                    c.Parameters.AddWithValue("$ativo", centroCusto.Ativo ? 1 : 0);
                    c.Parameters.AddWithValue("$uuid", centroCusto.Uuid);
                    c.Parameters.AddWithValue("$atualizado", Texto(centroCusto.AtualizadoEm));
                    c.Parameters.AddWithValue("$excluido", centroCusto.Excluido ? 1 : 0);
                },
                ct).ConfigureAwait(false);

            var id = await ExecutarEscalarAsync("SELECT last_insert_rowid();", null, ct).ConfigureAwait(false);
            centroCusto.Id = Convert.ToInt64(id, System.Globalization.CultureInfo.InvariantCulture);
            return centroCusto.Id;
        }

        await ExecutarComandoAsync(
            """
            UPDATE CENTRODECUSTO SET
                DESCRICAO = $descricao, PRINCIPAL = $principal, ICONE = $icone, ATIVO = $ativo,
                ATUALIZADOEM = $atualizado, EXCLUIDO = $excluido
            WHERE ID = $id;
            """,
            c =>
            {
                c.Parameters.AddWithValue("$id", centroCusto.Id);
                c.Parameters.AddWithValue("$descricao", centroCusto.Descricao);
                c.Parameters.AddWithValue("$principal", centroCusto.Principal ? 1 : 0);
                c.Parameters.AddWithValue("$icone", centroCusto.Icone);
                c.Parameters.AddWithValue("$ativo", centroCusto.Ativo ? 1 : 0);
                c.Parameters.AddWithValue("$atualizado", Texto(centroCusto.AtualizadoEm));
                c.Parameters.AddWithValue("$excluido", centroCusto.Excluido ? 1 : 0);
            },
            ct).ConfigureAwait(false);

        return centroCusto.Id;
    }

    public Task DefinirComoPrincipalAsync(long centroCustoId, DateTime agoraUtc, CancellationToken ct = default)
        => ExecutarComandoAsync(
            """
            UPDATE CENTRODECUSTO
               SET PRINCIPAL = CASE WHEN ID = $id THEN 1 ELSE 0 END,
                   ATUALIZADOEM = CASE WHEN ID = $id THEN ATUALIZADOEM ELSE $agora END
             WHERE EXCLUIDO = 0;
            """,
            c =>
            {
                c.Parameters.AddWithValue("$id", centroCustoId);
                c.Parameters.AddWithValue("$agora", Texto(agoraUtc));
            },
            ct);

    public async Task<IReadOnlyList<CentroCusto>> ListarTodosIncluindoExcluidosAsync(CancellationToken ct = default)
        => await ConsultarAsync($"SELECT {Colunas} FROM CENTRODECUSTO ORDER BY ID;", null, Mapear, ct).ConfigureAwait(false);

    private static CentroCusto Mapear(SqliteDataReader leitor) => new()
    {
        Id = leitor.GetInt64(0),
        Descricao = leitor.GetString(1),
        Principal = LerBooleano(leitor, 2),
        Icone = leitor.GetString(3),
        Ativo = LerBooleano(leitor, 4),
        Uuid = leitor.GetString(5),
        AtualizadoEm = LerData(leitor, 6),
        Excluido = LerBooleano(leitor, 7)
    };
}
