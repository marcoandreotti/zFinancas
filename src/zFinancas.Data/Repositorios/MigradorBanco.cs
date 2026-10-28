using Microsoft.Data.Sqlite;
using zFinancas.Data.Infra;

namespace zFinancas.Data.Repositorios;

/// <summary>
/// Versionamento do esquema usando <c>PRAGMA user_version</c>.
/// <para>
/// Sem ORM e sem migrations geradas: cada versão é um script SQL idempotente aplicado
/// em ordem. Bancos criados por versões anteriores do app são atualizados no start.
/// </para>
/// </summary>
public sealed class MigradorBanco
{
    public const int VersaoAtual = 2;

    private readonly ContextoBanco _contexto;

    public MigradorBanco(ContextoBanco contexto)
    {
        _contexto = contexto;
    }

    public async Task<int> AplicarAsync(CancellationToken ct = default)
    {
        using var _ = await _contexto.EntrarAsync(ct).ConfigureAwait(false);
        _contexto.AssumirEscopoNesteFluxo();

        var versao = await ObterVersaoAsync(ct).ConfigureAwait(false);
        if (versao >= VersaoAtual)
        {
            return versao;
        }

        if (versao < 1)
        {
            await ExecutarScriptAsync(ScriptVersao1, ct).ConfigureAwait(false);
        }

        if (versao < 2)
        {
            await ExecutarScriptAsync(ScriptVersao2, ct).ConfigureAwait(false);
        }

        await DefinirVersaoAsync(VersaoAtual, ct).ConfigureAwait(false);
        return VersaoAtual;
    }

    private async Task<int> ObterVersaoAsync(CancellationToken ct)
    {
        await using var comando = await _contexto.CriarComandoAsync("PRAGMA user_version;", ct).ConfigureAwait(false);
        var valor = await comando.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return valor is null ? 0 : Convert.ToInt32(valor, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task DefinirVersaoAsync(int versao, CancellationToken ct)
    {
        // PRAGMA não aceita parâmetros; a versão é um inteiro controlado pelo código.
        await using var comando = await _contexto.CriarComandoAsync($"PRAGMA user_version = {versao};", ct)
            .ConfigureAwait(false);
        await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ExecutarScriptAsync(string script, CancellationToken ct)
    {
        await using var comando = await _contexto.CriarComandoAsync(script, ct).ConfigureAwait(false);
        await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Versão 1 — modelo do zFinancas.
    /// <para>
    /// Desvios conscientes em relação ao rascunho original:
    /// SENHA é VARCHAR(200) porque guarda o hash PBKDF2 (<c>PBKDF2$iteracoes$salt$hash</c>),
    /// nunca a senha em claro; e todas as tabelas sincronizáveis ganharam UUID, ATUALIZADOEM
    /// e EXCLUIDO para permitir sincronização PC ? celular com resolução de conflito.
    /// </para>
    /// </summary>
    /// <summary>
    /// Versão 2 — periodicidade da série.
    /// <para>
    /// A recorrência passa a ser gravada em cada parcela (nula nos lançamentos antigos,
    /// que foram criados antes desta informação existir). Ela é o que permite propagar
    /// ajustes e acrescentar novas parcelas mantendo o espaçamento das datas.
    /// </para>
    /// </summary>
    private const string ScriptVersao2 = """
        ALTER TABLE LANCAMENTO ADD COLUMN RECORRENCIA INTEGER;
        """;

    private const string ScriptVersao1 = """
        CREATE TABLE IF NOT EXISTS USUARIO (
            ID                 INTEGER PRIMARY KEY AUTOINCREMENT,
            NOME               VARCHAR(30)  NOT NULL,
            SENHA              VARCHAR(200) NOT NULL,
            DICA_SENHA         VARCHAR(50),
            CODIGO_RECUPERACAO VARCHAR(200),
            PERFIL             CHAR(3)      NOT NULL,
            ATIVO              INTEGER      NOT NULL DEFAULT 1,
            DATACADASTRO       TEXT         NOT NULL,
            ULTIMOACESSO       TEXT,
            UUID               CHAR(32)     NOT NULL,
            ATUALIZADOEM       TEXT         NOT NULL,
            EXCLUIDO           INTEGER      NOT NULL DEFAULT 0
        );

        CREATE UNIQUE INDEX IF NOT EXISTS UX_USUARIO_UUID ON USUARIO (UUID);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_USUARIO_NOME ON USUARIO (NOME COLLATE NOCASE);

        CREATE TABLE IF NOT EXISTS CONTACORRENTE (
            ID           INTEGER PRIMARY KEY AUTOINCREMENT,
            DESCRICAO    VARCHAR(50) NOT NULL,
            PRINCIPAL    INTEGER     NOT NULL DEFAULT 0,
            ATIVO        INTEGER     NOT NULL DEFAULT 1,
            CORHEX       CHAR(7)     NOT NULL DEFAULT '#6C5BFF',
            ICONE        VARCHAR(30) NOT NULL DEFAULT '??',
            ORDEM        INTEGER     NOT NULL DEFAULT 0,
            UUID         CHAR(32)    NOT NULL,
            ATUALIZADOEM TEXT        NOT NULL,
            EXCLUIDO     INTEGER     NOT NULL DEFAULT 0
        );

        CREATE UNIQUE INDEX IF NOT EXISTS UX_CONTACORRENTE_UUID ON CONTACORRENTE (UUID);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_CONTACORRENTE_DESCRICAO ON CONTACORRENTE (DESCRICAO COLLATE NOCASE);

        CREATE TABLE IF NOT EXISTS CENTRODECUSTO (
            ID           INTEGER PRIMARY KEY AUTOINCREMENT,
            DESCRICAO    VARCHAR(50) NOT NULL,
            PRINCIPAL    INTEGER     NOT NULL DEFAULT 0,
            ICONE        VARCHAR(30) NOT NULL DEFAULT '??',
            ATIVO        INTEGER     NOT NULL DEFAULT 1,
            UUID         CHAR(32)    NOT NULL,
            ATUALIZADOEM TEXT        NOT NULL,
            EXCLUIDO     INTEGER     NOT NULL DEFAULT 0
        );

        CREATE UNIQUE INDEX IF NOT EXISTS UX_CENTRODECUSTO_UUID ON CENTRODECUSTO (UUID);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_CENTRODECUSTO_DESCRICAO ON CENTRODECUSTO (DESCRICAO COLLATE NOCASE);

        CREATE TABLE IF NOT EXISTS USUARIOCONTACORRENTE (
            USUARIOID       INTEGER NOT NULL REFERENCES USUARIO (ID),
            CONTACORRENTEID INTEGER NOT NULL REFERENCES CONTACORRENTE (ID),
            ATUALIZADOEM    TEXT    NOT NULL,
            EXCLUIDO        INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (USUARIOID, CONTACORRENTEID)
        );

        CREATE TABLE IF NOT EXISTS LANCAMENTO (
            ID                 INTEGER PRIMARY KEY AUTOINCREMENT,
            CONTACORRENTEID    INTEGER      NOT NULL REFERENCES CONTACORRENTE (ID),
            CENTRODECUSTOID    INTEGER      NOT NULL REFERENCES CENTRODECUSTO (ID),
            DATALANCAMENTO     TEXT         NOT NULL,
            DATAVENCIMENTO     TEXT         NOT NULL,
            DATACOMPENSADO     TEXT,
            DESCRICAO          VARCHAR(50)  NOT NULL,
            VALOR              TEXT         NOT NULL,
            COMPENSADO         INTEGER      NOT NULL DEFAULT 0,
            AGRUPADOR          INTEGER      NOT NULL DEFAULT 0,
            AGRUPADORUUID      CHAR(32),
            PARCELA            INTEGER      NOT NULL DEFAULT 1,
            QUANTIDADEPARCELAS INTEGER      NOT NULL DEFAULT 1,
            OBSERVACAO         VARCHAR(200),
            UUID               CHAR(32)     NOT NULL,
            ATUALIZADOEM       TEXT         NOT NULL,
            EXCLUIDO           INTEGER      NOT NULL DEFAULT 0
        );

        CREATE UNIQUE INDEX IF NOT EXISTS UX_LANCAMENTO_UUID ON LANCAMENTO (UUID);
        CREATE INDEX IF NOT EXISTS IX_LANCAMENTO_CONTA_DATA ON LANCAMENTO (CONTACORRENTEID, DATALANCAMENTO);
        CREATE INDEX IF NOT EXISTS IX_LANCAMENTO_SERIE ON LANCAMENTO (AGRUPADORUUID);
        CREATE INDEX IF NOT EXISTS IX_LANCAMENTO_COMPENSADO ON LANCAMENTO (COMPENSADO);

        CREATE TABLE IF NOT EXISTS CONFIGURACAO (
            CHAVE        VARCHAR(60) PRIMARY KEY,
            VALOR        TEXT        NOT NULL,
            ATUALIZADOEM TEXT        NOT NULL
        );
        """;
}
