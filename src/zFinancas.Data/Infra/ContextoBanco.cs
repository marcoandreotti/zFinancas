using Microsoft.Data.Sqlite;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.Data.Infra;

/// <summary>
/// Dono da conexão SQLite do aplicativo.
/// <para>
/// Uma única conexão é mantida aberta (o app é single-user) e serializada por semáforo.
/// A reentrância é resolvida por um <b>token de escopo</b>: quem segura o bloqueio publica
/// um objeto de escopo que as chamadas aninhadas (repositórios dentro de uma transação)
/// reconhecem por identidade e, assim, não tentam reentrar no semáforo.
/// </para>
/// <para>
/// Um <c>AsyncLocal</c> de contador não funcionaria aqui: valores gravados dentro de um
/// método assíncrono não voltam para o contexto do chamador, então a profundidade nunca
/// seria vista pelo nível externo. O token comparado com o dono atual resolve isso e ainda
/// evita falsos positivos de outra operação concorrente.
/// </para>
/// </summary>
public sealed class ContextoBanco : IAsyncDisposable
{
    private readonly string _caminhoArquivo;
    private readonly SemaphoreSlim _semaforo = new(1, 1);
    private readonly AsyncLocal<Escopo?> _escopoAtual = new();

    private SqliteConnection? _conexao;
    private SqliteTransaction? _transacao;
    private Escopo? _escopoDono;

    /// <summary>Marca lógica de "este fluxo é o dono do bloqueio".</summary>
    private sealed class Escopo
    {
    }


    public ContextoBanco(string caminhoArquivo)
    {
        if (string.IsNullOrWhiteSpace(caminhoArquivo))
        {
            throw new ArgumentException("Informe o caminho do arquivo do banco.", nameof(caminhoArquivo));
        }

        _caminhoArquivo = caminhoArquivo;
    }

    public string CaminhoArquivo => _caminhoArquivo;

    public SqliteTransaction? TransacaoAtual => _transacao;

    public async Task<SqliteConnection> ObterConexaoAsync(CancellationToken ct = default)
    {
        if (_conexao is not null)
        {
            return _conexao;
        }

        var diretorio = Path.GetDirectoryName(_caminhoArquivo);
        if (!string.IsNullOrWhiteSpace(diretorio) && !Directory.Exists(diretorio))
        {
            Directory.CreateDirectory(diretorio);
        }

        GarantirArquivoGravavel();

        var conexao = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _caminhoArquivo,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString());

        try
        {
            await conexao.OpenAsync(ct).ConfigureAwait(false);
            await AplicarPragmasAsync(conexao, ct).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            await conexao.DisposeAsync().ConfigureAwait(false);
            throw new DominioException(MensagemDeAcessoNegado(ex), ex);
        }

        _conexao = conexao;
        return conexao;
    }

    /// <summary>
    /// Entra na região crítica. Deve ser chamado <b>apenas</b> pelo operador mais externo
    /// (unidade de trabalho ou consulta avulsa), seguido de
    /// <see cref="AssumirEscopoNesteFluxo"/>.
    /// </summary>
    public async Task<IDisposable> EntrarAsync(CancellationToken ct = default)
    {
        await _semaforo.WaitAsync(ct).ConfigureAwait(false);

        var escopo = new Escopo();
        _escopoDono = escopo;
        return new Liberacao(this, escopo);
    }

    /// <summary>
    /// Publica o escopo no fluxo assíncrono atual para que as chamadas aninhadas o herdem.
    /// <para>
    /// Precisa ser invocado pelo <b>chamador</b> logo após <see cref="EntrarAsync"/>: um
    /// <c>AsyncLocal</c> gravado dentro de um método assíncrono não volta para o contexto de
    /// quem chamou, então só assim os repositórios executados depois enxergam o escopo.
    /// </para>
    /// </summary>
    public void AssumirEscopoNesteFluxo() => _escopoAtual.Value = _escopoDono;

    /// <summary>Verdadeiro quando o fluxo atual é o dono do bloqueio (pode entrar sem esperar).</summary>
    public bool SouDonoDoEscopo => _escopoAtual.Value is { } escopo && ReferenceEquals(escopo, _escopoDono);

    public async Task<SqliteTransaction> IniciarTransacaoAsync(CancellationToken ct = default)
    {
        var conexao = await ObterConexaoAsync(ct).ConfigureAwait(false);
        _transacao = (SqliteTransaction)await conexao.BeginTransactionAsync(ct).ConfigureAwait(false);
        return _transacao;
    }

    public async Task ConfirmarAsync(CancellationToken ct = default)
    {
        if (_transacao is null) return;

        await _transacao.CommitAsync(ct).ConfigureAwait(false);
        await _transacao.DisposeAsync().ConfigureAwait(false);
        _transacao = null;
    }

    public async Task DesfazerAsync(CancellationToken ct = default)
    {
        if (_transacao is null) return;

        await _transacao.RollbackAsync(ct).ConfigureAwait(false);
        await _transacao.DisposeAsync().ConfigureAwait(false);
        _transacao = null;
    }

    /// <summary>Cria um comando já associado é transação corrente (se houver).</summary>
    public async Task<SqliteCommand> CriarComandoAsync(string sql, CancellationToken ct = default)
    {
        var conexao = await ObterConexaoAsync(ct).ConfigureAwait(false);
        var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        if (_transacao is not null)
        {
            comando.Transaction = _transacao;
        }

        return comando;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transacao is not null)
        {
            await _transacao.DisposeAsync().ConfigureAwait(false);
            _transacao = null;
        }

        if (_conexao is not null)
        {
            await _conexao.DisposeAsync().ConfigureAwait(false);
            _conexao = null;
        }

        _semaforo.Dispose();
    }

    private sealed class Liberacao : IDisposable
    {
        private readonly ContextoBanco _contexto;
        private readonly Escopo _escopo;
        private bool _descartado;

        public Liberacao(ContextoBanco contexto, Escopo escopo)
        {
            _contexto = contexto;
            _escopo = escopo;
        }

        public void Dispose()
        {
            if (_descartado) return;
            _descartado = true;

            // Só libera se ainda somos o dono (evita liberar duas vezes por escopo aninhado).
            if (ReferenceEquals(_contexto._escopoDono, _escopo))
            {
                _contexto._escopoDono = null;
                _contexto._semaforo.Release();
            }
        }
    }

    // ------------------------------------------------------------ proteção do arquivo

    /// <summary>
    /// Garante que o arquivo existente pode ser gravado. O SQLITE_READONLY (erro 8) quase
    /// sempre vem do atributo “somente leitura” no arquivo — comum quando o banco é copiado
    /// de outro lugar — ou de permissão negada na pasta.
    /// </summary>
    private void GarantirArquivoGravavel()
    {
        if (!File.Exists(_caminhoArquivo))
        {
            return;
        }

        try
        {
            var atributos = File.GetAttributes(_caminhoArquivo);
            if (atributos.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(_caminhoArquivo, atributos & ~FileAttributes.ReadOnly);
            }

            using var fluxo = new FileStream(
                _caminhoArquivo, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

            _ = fluxo.CanWrite;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new DominioException(MensagemDeAcessoNegado(null), ex);
        }
    }

    /// <summary>
    /// WAL é o modo desejado, mas alguns volumes (pastas sincronizadas, rede, mídia removível)
    /// não permitem criar os arquivos <c>-wal</c>/<c>-shm</c>. Nesse caso seguimos com o
    /// journal padrão em vez de derrubar o app.
    /// </summary>
    private static async Task AplicarPragmasAsync(SqliteConnection conexao, CancellationToken ct)
    {
        if (!await TentarPragmaAsync(conexao, "PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false))
        {
            await TentarPragmaAsync(conexao, "PRAGMA journal_mode=DELETE;", ct).ConfigureAwait(false);
        }

        await using var comando = conexao.CreateCommand();
        comando.CommandText = """
            PRAGMA synchronous=NORMAL;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            """;
        await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> TentarPragmaAsync(SqliteConnection conexao, string sql, CancellationToken ct)
    {
        try
        {
            await using var comando = conexao.CreateCommand();
            comando.CommandText = sql;
            await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private string MensagemDeAcessoNegado(SqliteException? ex)
    {
        var detalhe = ex is null ? string.Empty : $" ({ex.SqliteErrorCode}: {ex.Message})";

        return $"""
            Não foi possível gravar no banco de dados{detalhe}.

            Arquivo: {_caminhoArquivo}

            Verifique se:
            • o arquivo não está marcado como somente leitura;
            • a pasta existe e você tem permissão de escrita;
            • nenhum outro programa (ou antivírus) mantém o arquivo bloqueado.
            """;
    }
}