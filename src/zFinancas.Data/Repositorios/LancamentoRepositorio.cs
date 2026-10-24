using Microsoft.Data.Sqlite;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Data.Infra;

namespace zFinancas.Data.Repositorios;

public sealed class LancamentoRepositorio : RepositorioBase, ILancamentoRepositorio
{
    private const string Colunas = """
        ID, CONTACORRENTEID, CENTRODECUSTOID, DATALANCAMENTO, DATAVENCIMENTO, DATACOMPENSADO,
        DESCRICAO, VALOR, COMPENSADO, AGRUPADOR, AGRUPADORUUID, PARCELA, QUANTIDADEPARCELAS,
        OBSERVACAO, UUID, ATUALIZADOEM, EXCLUIDO
        """;

    public LancamentoRepositorio(ContextoBanco contexto) : base(contexto)
    {
    }

    public Task<Lancamento?> ObterPorIdAsync(long id, CancellationToken ct = default)
        => ConsultarUmAsync(
            $"SELECT {Colunas} FROM LANCAMENTO WHERE ID = $id AND EXCLUIDO = 0;",
            c => c.Parameters.AddWithValue("$id", id),
            Mapear,
            ct);

    public async Task<IReadOnlyList<Lancamento>> ListarPorContaAsync(
        long contaId,
        DateTime? inicio = null,
        DateTime? fim = null,
        CancellationToken ct = default)
        => await ListarPorContasAsync([contaId], inicio, fim, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<Lancamento>> ListarPorContasAsync(
        IReadOnlyCollection<long> contasIds,
        DateTime? inicio = null,
        DateTime? fim = null,
        CancellationToken ct = default)
    {
        var ids = (contasIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var filtros = new List<string>
        {
            "EXCLUIDO = 0",
            // Várias contas são uma ALTERNATIVA (OR), nunca uma conjunção.
            "(" + string.Join(" OR ", ids.Select((_, i) => $"CONTACORRENTEID = $conta{i}")) + ")"
        };

        if (inicio is not null) filtros.Add("DATALANCAMENTO >= $inicio");
        if (fim is not null) filtros.Add("DATALANCAMENTO <= $fim");

        var sql = $"""
            SELECT {Colunas}
              FROM LANCAMENTO
             WHERE {string.Join(" AND ", filtros)}
             ORDER BY DATALANCAMENTO, ID;
            """;

        return await ConsultarAsync(sql, c =>
        {
            for (var i = 0; i < ids.Count; i++)
            {
                c.Parameters.AddWithValue($"$conta{i}", ids[i]);
            }

            if (inicio is not null) c.Parameters.AddWithValue("$inicio", Texto(inicio.Value.Date));
            if (fim is not null) c.Parameters.AddWithValue("$fim", Texto(fim.Value.Date.AddDays(1).AddTicks(-1)));
        }, Mapear, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Lancamento>> ListarPorSerieAsync(string chaveDaSerie, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chaveDaSerie))
        {
            return [];
        }

        var texto = chaveDaSerie.Trim();

        if (texto.StartsWith('L') && long.TryParse(texto[1..], out var agrupador))
        {
            return await ConsultarAsync(
                $"SELECT {Colunas} FROM LANCAMENTO WHERE EXCLUIDO = 0 AND AGRUPADOR = $agrupador ORDER BY PARCELA, ID;",
                c => c.Parameters.AddWithValue("$agrupador", agrupador),
                Mapear,
                ct).ConfigureAwait(false);
        }

        return await ConsultarAsync(
            $"SELECT {Colunas} FROM LANCAMENTO WHERE EXCLUIDO = 0 AND AGRUPADORUUID = $uuid ORDER BY PARCELA, ID;",
            c => c.Parameters.AddWithValue("$uuid", texto),
            Mapear,
            ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Lancamento>> ListarUltimosAsync(
        IReadOnlyCollection<long> contasIds,
        DateTime inicio,
        DateTime fim,
        int limite = 20,
        CancellationToken ct = default)
    {
        var ids = (contasIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var filtros = new List<string>
        {
            "EXCLUIDO = 0",
            "DATALANCAMENTO >= $inicio",
            "DATALANCAMENTO <= $fim",
            "(" + string.Join(" OR ", ids.Select((_, i) => $"CONTACORRENTEID = $conta{i}")) + ")"
        };

        var sql = $"""
            SELECT {Colunas}
              FROM LANCAMENTO
             WHERE {string.Join(" AND ", filtros)}
             ORDER BY DATALANCAMENTO DESC, ID DESC
             LIMIT $limite;
            """;

        return await ConsultarAsync(sql, c =>
        {
            for (var i = 0; i < ids.Count; i++)
            {
                c.Parameters.AddWithValue($"$conta{i}", ids[i]);
            }

            c.Parameters.AddWithValue("$inicio", Texto(inicio.Date));
            c.Parameters.AddWithValue("$fim", Texto(fim.Date.AddDays(1).AddTicks(-1)));
            c.Parameters.AddWithValue("$limite", limite);
        }, Mapear, ct).ConfigureAwait(false);
    }

    public async Task<long> ProximoAgrupadorAsync(CancellationToken ct = default)
    {
        var valor = await ExecutarEscalarAsync(
            "SELECT COALESCE(MAX(AGRUPADOR), 0) + 1 FROM LANCAMENTO;", null, ct).ConfigureAwait(false);

        return Convert.ToInt64(valor, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<long> SalvarAsync(Lancamento lancamento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lancamento);
        lancamento.Validar();

        if (lancamento.Id == 0)
        {
            await InserirAsync(lancamento, ct).ConfigureAwait(false);
            var id = await ExecutarEscalarAsync("SELECT last_insert_rowid();", null, ct).ConfigureAwait(false);
            lancamento.Id = Convert.ToInt64(id, System.Globalization.CultureInfo.InvariantCulture);
            return lancamento.Id;
        }

        await ExecutarComandoAsync(
            """
            UPDATE LANCAMENTO SET
                CONTACORRENTEID = $conta, CENTRODECUSTOID = $centro, DATALANCAMENTO = $lancamento,
                DATAVENCIMENTO = $vencimento, DATACOMPENSADO = $compensado, DESCRICAO = $descricao,
                VALOR = $valor, COMPENSADO = $flag, AGRUPADOR = $agrupador, AGRUPADORUUID = $agrupadorUuid,
                PARCELA = $parcela, QUANTIDADEPARCELAS = $quantidade, OBSERVACAO = $observacao,
                ATUALIZADOEM = $atualizado, EXCLUIDO = $excluido
            WHERE ID = $id;
            """,
            c =>
            {
                c.Parameters.AddWithValue("$id", lancamento.Id);
                PreencherParametros(c, lancamento);
            },
            ct).ConfigureAwait(false);

        return lancamento.Id;
    }

    public async Task SalvarVariosAsync(IEnumerable<Lancamento> lancamentos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lancamentos);

        var lista = lancamentos.ToList();
        if (lista.Count == 0) return;

        // Atomicidade: se não há transação ambiente (unidade de trabalho), cria uma aqui.
        if (Contexto.TransacaoAtual is not null)
        {
            foreach (var lancamento in lista)
            {
                await SalvarAsync(lancamento, ct).ConfigureAwait(false);
            }

            return;
        }

        using var _ = await Contexto.EntrarAsync(ct).ConfigureAwait(false);
        Contexto.AssumirEscopoNesteFluxo();
        await Contexto.IniciarTransacaoAsync(ct).ConfigureAwait(false);

        try
        {
            foreach (var lancamento in lista)
            {
                await SalvarAsync(lancamento, ct).ConfigureAwait(false);
            }

            await Contexto.ConfirmarAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await Contexto.DesfazerAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<Lancamento>> ListarTodosIncluindoExcluidosAsync(CancellationToken ct = default)
        => await ConsultarAsync($"SELECT {Colunas} FROM LANCAMENTO ORDER BY ID;", null, Mapear, ct).ConfigureAwait(false);

    private Task InserirAsync(Lancamento lancamento, CancellationToken ct)
        => ExecutarComandoAsync(
            """
            INSERT INTO LANCAMENTO
                (CONTACORRENTEID, CENTRODECUSTOID, DATALANCAMENTO, DATAVENCIMENTO, DATACOMPENSADO,
                 DESCRICAO, VALOR, COMPENSADO, AGRUPADOR, AGRUPADORUUID, PARCELA, QUANTIDADEPARCELAS,
                 OBSERVACAO, UUID, ATUALIZADOEM, EXCLUIDO)
            VALUES
                ($conta, $centro, $lancamento, $vencimento, $compensado,
                 $descricao, $valor, $flag, $agrupador, $agrupadorUuid, $parcela, $quantidade,
                 $observacao, $uuid, $atualizado, $excluido);
            """,
            c => PreencherParametros(c, lancamento),
            ct);

    private static void PreencherParametros(SqliteCommand comando, Lancamento lancamento)
    {
        comando.Parameters.AddWithValue("$conta", lancamento.ContaCorrenteId);
        comando.Parameters.AddWithValue("$centro", lancamento.CentroCustoId);
        comando.Parameters.AddWithValue("$lancamento", Texto(lancamento.DataLancamento));
        comando.Parameters.AddWithValue("$vencimento", Texto(lancamento.DataVencimento));
        comando.Parameters.AddWithValue("$compensado", Texto(lancamento.DataCompensado));
        comando.Parameters.AddWithValue("$descricao", lancamento.Descricao);
        comando.Parameters.AddWithValue("$valor", Texto(lancamento.Valor));
        comando.Parameters.AddWithValue("$flag", lancamento.Compensado ? 1 : 0);
        comando.Parameters.AddWithValue("$agrupador", lancamento.Agrupador);
        comando.Parameters.AddWithValue("$agrupadorUuid", Texto(lancamento.AgrupadorUuid));
        comando.Parameters.AddWithValue("$parcela", lancamento.Parcela);
        comando.Parameters.AddWithValue("$quantidade", lancamento.QuantidadeParcelas);
        comando.Parameters.AddWithValue("$observacao", Texto(lancamento.Observacao));
        comando.Parameters.AddWithValue("$uuid", lancamento.Uuid);
        comando.Parameters.AddWithValue("$atualizado", Texto(lancamento.AtualizadoEm));
        comando.Parameters.AddWithValue("$excluido", lancamento.Excluido ? 1 : 0);
    }

    private static Lancamento Mapear(SqliteDataReader leitor) => new()
    {
        Id = leitor.GetInt64(0),
        ContaCorrenteId = leitor.GetInt64(1),
        CentroCustoId = leitor.GetInt64(2),
        DataLancamento = LerData(leitor, 3),
        DataVencimento = LerData(leitor, 4),
        DataCompensado = LerDataOpcional(leitor, 5),
        Descricao = leitor.GetString(6),
        Valor = LerDecimal(leitor, 7),
        Compensado = LerBooleano(leitor, 8),
        Agrupador = leitor.GetInt64(9),
        AgrupadorUuid = LerTextoOpcional(leitor, 10),
        Parcela = leitor.GetInt64(11),
        QuantidadeParcelas = leitor.GetInt64(12),
        Observacao = LerTextoOpcional(leitor, 13),
        Uuid = leitor.GetString(14),
        AtualizadoEm = LerData(leitor, 15),
        Excluido = LerBooleano(leitor, 16)
    };
}
