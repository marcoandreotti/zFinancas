using Microsoft.Data.Sqlite;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Data.Infra;

namespace zFinancas.Data.Repositorios;

public sealed class ContaCorrenteRepositorio : RepositorioBase, IContaCorrenteRepositorio
{
    private const string Colunas = "ID, DESCRICAO, PRINCIPAL, ATIVO, CORHEX, ICONE, ORDEM, UUID, ATUALIZADOEM, EXCLUIDO";

    public ContaCorrenteRepositorio(ContextoBanco contexto) : base(contexto)
    {
    }

    public Task<ContaCorrente?> ObterPorIdAsync(long id, CancellationToken ct = default)
        => ConsultarUmAsync(
            $"SELECT {Colunas} FROM CONTACORRENTE WHERE ID = $id AND EXCLUIDO = 0;",
            c => c.Parameters.AddWithValue("$id", id),
            Mapear,
            ct);

    public async Task<IReadOnlyList<ContaCorrente>> ListarAsync(bool incluirInativas = false, CancellationToken ct = default)
    {
        var sql = incluirInativas
            ? $"SELECT {Colunas} FROM CONTACORRENTE WHERE EXCLUIDO = 0 ORDER BY PRINCIPAL DESC, ORDEM, DESCRICAO COLLATE NOCASE;"
            : $"SELECT {Colunas} FROM CONTACORRENTE WHERE EXCLUIDO = 0 AND ATIVO = 1 ORDER BY PRINCIPAL DESC, ORDEM, DESCRICAO COLLATE NOCASE;";

        return await ConsultarAsync(sql, null, Mapear, ct).ConfigureAwait(false);
    }

    public Task<ContaCorrente?> ObterPrincipalAsync(CancellationToken ct = default)
        => ConsultarUmAsync(
            $"SELECT {Colunas} FROM CONTACORRENTE WHERE EXCLUIDO = 0 AND ATIVO = 1 AND PRINCIPAL = 1 ORDER BY ID LIMIT 1;",
            null,
            Mapear,
            ct);

    public async Task<long> SalvarAsync(ContaCorrente conta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conta);
        conta.Validar();

        if (conta.Id == 0)
        {
            await ExecutarComandoAsync(
                """
                INSERT INTO CONTACORRENTE
                    (DESCRICAO, PRINCIPAL, ATIVO, CORHEX, ICONE, ORDEM, UUID, ATUALIZADOEM, EXCLUIDO)
                VALUES
                    ($descricao, $principal, $ativo, $cor, $icone, $ordem, $uuid, $atualizado, $excluido);
                """,
                c =>
                {
                    c.Parameters.AddWithValue("$descricao", conta.Descricao);
                    c.Parameters.AddWithValue("$principal", conta.Principal ? 1 : 0);
                    c.Parameters.AddWithValue("$ativo", conta.Ativo ? 1 : 0);
                    c.Parameters.AddWithValue("$cor", conta.CorHex);
                    c.Parameters.AddWithValue("$icone", conta.Icone);
                    c.Parameters.AddWithValue("$ordem", conta.Ordem);
                    c.Parameters.AddWithValue("$uuid", conta.Uuid);
                    c.Parameters.AddWithValue("$atualizado", Texto(conta.AtualizadoEm));
                    c.Parameters.AddWithValue("$excluido", conta.Excluido ? 1 : 0);
                },
                ct).ConfigureAwait(false);

            var id = await ExecutarEscalarAsync("SELECT last_insert_rowid();", null, ct).ConfigureAwait(false);
            conta.Id = Convert.ToInt64(id, System.Globalization.CultureInfo.InvariantCulture);
            return conta.Id;
        }

        await ExecutarComandoAsync(
            """
            UPDATE CONTACORRENTE SET
                DESCRICAO = $descricao, PRINCIPAL = $principal, ATIVO = $ativo, CORHEX = $cor,
                ICONE = $icone, ORDEM = $ordem, ATUALIZADOEM = $atualizado, EXCLUIDO = $excluido, UUID = $uuid
            WHERE ID = $id;
            """,
            c =>
            {
                c.Parameters.AddWithValue("$id", conta.Id);
                c.Parameters.AddWithValue("$uuid", conta.Uuid);
                c.Parameters.AddWithValue("$descricao", conta.Descricao);
                c.Parameters.AddWithValue("$principal", conta.Principal ? 1 : 0);
                c.Parameters.AddWithValue("$ativo", conta.Ativo ? 1 : 0);
                c.Parameters.AddWithValue("$cor", conta.CorHex);
                c.Parameters.AddWithValue("$icone", conta.Icone);
                c.Parameters.AddWithValue("$ordem", conta.Ordem);
                c.Parameters.AddWithValue("$atualizado", Texto(conta.AtualizadoEm));
                c.Parameters.AddWithValue("$excluido", conta.Excluido ? 1 : 0);
            },
            ct).ConfigureAwait(false);

        return conta.Id;
    }

    public Task DefinirComoPrincipalAsync(long contaId, DateTime agoraUtc, CancellationToken ct = default)
        => ExecutarComandoAsync(
            """
            UPDATE CONTACORRENTE
               SET PRINCIPAL = CASE WHEN ID = $id THEN 1 ELSE 0 END,
                   ATUALIZADOEM = CASE WHEN ID = $id THEN ATUALIZADOEM ELSE $agora END
             WHERE EXCLUIDO = 0;
            """,
            c =>
            {
                c.Parameters.AddWithValue("$id", contaId);
                c.Parameters.AddWithValue("$agora", Texto(agoraUtc));
            },
            ct);

    public async Task<IReadOnlyList<ContaCorrente>> ListarTodosIncluindoExcluidosAsync(CancellationToken ct = default)
        => await ConsultarAsync($"SELECT {Colunas} FROM CONTACORRENTE ORDER BY ID;", null, Mapear, ct).ConfigureAwait(false);

    private static ContaCorrente Mapear(SqliteDataReader leitor) => new()
    {
        Id = leitor.GetInt64(0),
        Descricao = leitor.GetString(1),
        Principal = LerBooleano(leitor, 2),
        Ativo = LerBooleano(leitor, 3),
        CorHex = leitor.GetString(4),
        Icone = leitor.GetString(5),
        Ordem = leitor.GetInt32(6),
        Uuid = leitor.GetString(7),
        AtualizadoEm = LerData(leitor, 8),
        Excluido = LerBooleano(leitor, 9)
    };
}
