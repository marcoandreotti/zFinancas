using Microsoft.Data.Sqlite;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Data.Infra;

namespace zFinancas.Data.Repositorios;

public sealed class UsuarioContaCorrenteRepositorio : RepositorioBase, IUsuarioContaCorrenteRepositorio
{
    private readonly IRelogio _relogio;

    public UsuarioContaCorrenteRepositorio(ContextoBanco contexto, IRelogio relogio) : base(contexto)
    {
        _relogio = relogio;
    }

    public async Task<IReadOnlyList<long>> ListarContasDoUsuarioAsync(long usuarioId, CancellationToken ct = default)
    {
        var itens = await ConsultarAsync(
            "SELECT CONTACORRENTEID FROM USUARIOCONTACORRENTE WHERE USUARIOID = $usuario AND EXCLUIDO = 0;",
            c => c.Parameters.AddWithValue("$usuario", usuarioId),
            r => r.GetInt64(0),
            ct).ConfigureAwait(false);

        return itens;
    }

    /// <summary>
    /// Substitui o conjunto de vínculos do usuário. Remoções viram lápide (EXCLUIDO = 1)
    /// para que a sincronização propague a remoção em vez de ressuscitar o vínculo.
    /// </summary>
    public async Task DefinirContasDoUsuarioAsync(
        long usuarioId,
        IReadOnlyCollection<long> contasIds,
        CancellationToken ct = default)
    {
        var desejadas = new HashSet<long>(contasIds ?? []);
        var agora = Texto(_relogio.AgoraUtc);

        var atuais = await ConsultarAsync(
            "SELECT CONTACORRENTEID, EXCLUIDO FROM USUARIOCONTACORRENTE WHERE USUARIOID = $usuario;",
            c => c.Parameters.AddWithValue("$usuario", usuarioId),
            r => (ContaId: r.GetInt64(0), Excluido: LerBooleano(r, 1)),
            ct).ConfigureAwait(false);

        foreach (var atual in atuais)
        {
            var deveExistir = desejadas.Contains(atual.ContaId);

            if (deveExistir && atual.Excluido)
            {
                await ExecutarComandoAsync(
                    "UPDATE USUARIOCONTACORRENTE SET EXCLUIDO = 0, ATUALIZADOEM = $agora WHERE USUARIOID = $usuario AND CONTACORRENTEID = $conta;",
                    c =>
                    {
                        c.Parameters.AddWithValue("$usuario", usuarioId);
                        c.Parameters.AddWithValue("$conta", atual.ContaId);
                        c.Parameters.AddWithValue("$agora", agora);
                    },
                    ct).ConfigureAwait(false);
            }
            else if (!deveExistir && !atual.Excluido)
            {
                await ExecutarComandoAsync(
                    "UPDATE USUARIOCONTACORRENTE SET EXCLUIDO = 1, ATUALIZADOEM = $agora WHERE USUARIOID = $usuario AND CONTACORRENTEID = $conta;",
                    c =>
                    {
                        c.Parameters.AddWithValue("$usuario", usuarioId);
                        c.Parameters.AddWithValue("$conta", atual.ContaId);
                        c.Parameters.AddWithValue("$agora", agora);
                    },
                    ct).ConfigureAwait(false);
            }

            if (deveExistir)
            {
                desejadas.Remove(atual.ContaId);
            }
        }

        foreach (var contaId in desejadas)
        {
            await ExecutarComandoAsync(
                """
                INSERT INTO USUARIOCONTACORRENTE (USUARIOID, CONTACORRENTEID, ATUALIZADOEM, EXCLUIDO)
                VALUES ($usuario, $conta, $agora, 0)
                ON CONFLICT (USUARIOID, CONTACORRENTEID)
                DO UPDATE SET EXCLUIDO = 0, ATUALIZADOEM = $agora;
                """,
                c =>
                {
                    c.Parameters.AddWithValue("$usuario", usuarioId);
                    c.Parameters.AddWithValue("$conta", contaId);
                    c.Parameters.AddWithValue("$agora", agora);
                },
                ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<long>> ListarUsuariosDaContaAsync(long contaId, CancellationToken ct = default)
    {
        var itens = await ConsultarAsync(
            "SELECT USUARIOID FROM USUARIOCONTACORRENTE WHERE CONTACORRENTEID = $conta AND EXCLUIDO = 0;",
            c => c.Parameters.AddWithValue("$conta", contaId),
            r => r.GetInt64(0),
            ct).ConfigureAwait(false);

        return itens;
    }

    public async Task<IReadOnlyList<UsuarioContaCorrente>> ListarVinculosAsync(
        bool incluirExcluidos = false,
        CancellationToken ct = default)
    {
        var sql = incluirExcluidos
            ? "SELECT USUARIOID, CONTACORRENTEID, ATUALIZADOEM, EXCLUIDO FROM USUARIOCONTACORRENTE;"
            : "SELECT USUARIOID, CONTACORRENTEID, ATUALIZADOEM, EXCLUIDO FROM USUARIOCONTACORRENTE WHERE EXCLUIDO = 0;";

        return await ConsultarAsync(sql, null, Mapear, ct).ConfigureAwait(false);
    }

    public async Task SalvarVinculosAsync(IEnumerable<UsuarioContaCorrente> vinculos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(vinculos);

        foreach (var vinculo in vinculos)
        {
            await ExecutarComandoAsync(
                """
                INSERT INTO USUARIOCONTACORRENTE (USUARIOID, CONTACORRENTEID, ATUALIZADOEM, EXCLUIDO)
                VALUES ($usuario, $conta, $agora, $excluido)
                ON CONFLICT (USUARIOID, CONTACORRENTEID)
                DO UPDATE SET ATUALIZADOEM = $agora, EXCLUIDO = $excluido;
                """,
                c =>
                {
                    c.Parameters.AddWithValue("$usuario", vinculo.UsuarioId);
                    c.Parameters.AddWithValue("$conta", vinculo.ContaCorrenteId);
                    c.Parameters.AddWithValue("$agora", Texto(vinculo.AtualizadoEm));
                    c.Parameters.AddWithValue("$excluido", vinculo.Excluido ? 1 : 0);
                },
                ct).ConfigureAwait(false);
        }
    }

    private static UsuarioContaCorrente Mapear(SqliteDataReader leitor) => new()
    {
        UsuarioId = leitor.GetInt64(0),
        ContaCorrenteId = leitor.GetInt64(1),
        AtualizadoEm = LerData(leitor, 2),
        Excluido = LerBooleano(leitor, 3)
    };
}
