using Microsoft.Data.Sqlite;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Data.Infra;

namespace zFinancas.Data.Repositorios;

public sealed class UsuarioRepositorio : RepositorioBase, IUsuarioRepositorio
{
    private const string Colunas = """
        ID, NOME, SENHA, DICA_SENHA, CODIGO_RECUPERACAO, PERFIL, ATIVO,
        DATACADASTRO, ULTIMOACESSO, UUID, ATUALIZADOEM, EXCLUIDO
        """;

    public UsuarioRepositorio(ContextoBanco contexto) : base(contexto)
    {
    }

    public async Task<bool> ExisteAlgumAsync(CancellationToken ct = default)
    {
        var valor = await ExecutarEscalarAsync(
            "SELECT EXISTS (SELECT 1 FROM USUARIO WHERE EXCLUIDO = 0);", null, ct).ConfigureAwait(false);

        return Convert.ToInt64(valor, System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    public Task<Usuario?> ObterPorIdAsync(long id, CancellationToken ct = default)
        => ConsultarUmAsync(
            $"SELECT {Colunas} FROM USUARIO WHERE ID = $id AND EXCLUIDO = 0;",
            c => c.Parameters.AddWithValue("$id", id),
            Mapear,
            ct);

    public Task<Usuario?> ObterPorNomeAsync(string nome, CancellationToken ct = default)
        => ConsultarUmAsync(
            $"SELECT {Colunas} FROM USUARIO WHERE NOME = $nome COLLATE NOCASE AND EXCLUIDO = 0 LIMIT 1;",
            c => c.Parameters.AddWithValue("$nome", (nome ?? string.Empty).Trim()),
            Mapear,
            ct);

    public async Task<IReadOnlyList<Usuario>> ListarAsync(bool incluirInativos = true, CancellationToken ct = default)
    {
        var sql = incluirInativos
            ? $"SELECT {Colunas} FROM USUARIO WHERE EXCLUIDO = 0 ORDER BY NOME COLLATE NOCASE;"
            : $"SELECT {Colunas} FROM USUARIO WHERE EXCLUIDO = 0 AND ATIVO = 1 ORDER BY NOME COLLATE NOCASE;";

        return await ConsultarAsync(sql, null, Mapear, ct).ConfigureAwait(false);
    }

    public async Task<int> ContarAdministradoresAtivosAsync(CancellationToken ct = default)
    {
        var valor = await ExecutarEscalarAsync(
            "SELECT COUNT(1) FROM USUARIO WHERE EXCLUIDO = 0 AND ATIVO = 1 AND PERFIL = 'ADM';",
            null,
            ct).ConfigureAwait(false);

        return Convert.ToInt32(valor, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<long> SalvarAsync(Usuario usuario, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        usuario.Validar();

        if (usuario.Id == 0)
        {
            await ExecutarComandoAsync(
                """
                INSERT INTO USUARIO
                    (NOME, SENHA, DICA_SENHA, CODIGO_RECUPERACAO, PERFIL, ATIVO,
                     DATACADASTRO, ULTIMOACESSO, UUID, ATUALIZADOEM, EXCLUIDO)
                VALUES
                    ($nome, $senha, $dica, $codigo, $perfil, $ativo,
                     $cadastro, $acesso, $uuid, $atualizado, $excluido);
                """,
                c =>
                {
                    c.Parameters.AddWithValue("$nome", usuario.Nome);
                    c.Parameters.AddWithValue("$senha", usuario.SenhaHash);
                    c.Parameters.AddWithValue("$dica", Texto(usuario.DicaSenha));
                    c.Parameters.AddWithValue("$codigo", Texto(usuario.CodigoRecuperacaoHash));
                    c.Parameters.AddWithValue("$perfil", usuario.Perfil.ParaCodigo());
                    c.Parameters.AddWithValue("$ativo", usuario.Ativo ? 1 : 0);
                    c.Parameters.AddWithValue("$cadastro", Texto(usuario.DataCadastro));
                    c.Parameters.AddWithValue("$acesso", Texto(usuario.UltimoAcessoEm));
                    c.Parameters.AddWithValue("$uuid", usuario.Uuid);
                    c.Parameters.AddWithValue("$atualizado", Texto(usuario.AtualizadoEm));
                    c.Parameters.AddWithValue("$excluido", usuario.Excluido ? 1 : 0);
                },
                ct).ConfigureAwait(false);

            var id = await ExecutarEscalarAsync("SELECT last_insert_rowid();", null, ct).ConfigureAwait(false);
            usuario.Id = Convert.ToInt64(id, System.Globalization.CultureInfo.InvariantCulture);
            return usuario.Id;
        }

        await ExecutarComandoAsync(
            """
            UPDATE USUARIO SET
                NOME = $nome, SENHA = $senha, DICA_SENHA = $dica, CODIGO_RECUPERACAO = $codigo,
                PERFIL = $perfil, ATIVO = $ativo, DATACADASTRO = $cadastro, ULTIMOACESSO = $acesso,
                UUID = $uuid, ATUALIZADOEM = $atualizado, EXCLUIDO = $excluido
            WHERE ID = $id;
            """,
            c =>
            {
                c.Parameters.AddWithValue("$id", usuario.Id);
                c.Parameters.AddWithValue("$uuid", usuario.Uuid);
                c.Parameters.AddWithValue("$nome", usuario.Nome);
                c.Parameters.AddWithValue("$senha", usuario.SenhaHash);
                c.Parameters.AddWithValue("$dica", Texto(usuario.DicaSenha));
                c.Parameters.AddWithValue("$codigo", Texto(usuario.CodigoRecuperacaoHash));
                c.Parameters.AddWithValue("$perfil", usuario.Perfil.ParaCodigo());
                c.Parameters.AddWithValue("$ativo", usuario.Ativo ? 1 : 0);
                c.Parameters.AddWithValue("$cadastro", Texto(usuario.DataCadastro));
                c.Parameters.AddWithValue("$acesso", Texto(usuario.UltimoAcessoEm));
                c.Parameters.AddWithValue("$atualizado", Texto(usuario.AtualizadoEm));
                c.Parameters.AddWithValue("$excluido", usuario.Excluido ? 1 : 0);
            },
            ct).ConfigureAwait(false);

        return usuario.Id;
    }

    public async Task<IReadOnlyList<Usuario>> ListarTodosIncluindoExcluidosAsync(CancellationToken ct = default)
        => await ConsultarAsync($"SELECT {Colunas} FROM USUARIO ORDER BY ID;", null, Mapear, ct).ConfigureAwait(false);

    private static Usuario Mapear(SqliteDataReader leitor) => new()
    {
        Id = leitor.GetInt64(0),
        Nome = leitor.GetString(1),
        SenhaHash = leitor.GetString(2),
        DicaSenha = LerTextoOpcional(leitor, 3),
        CodigoRecuperacaoHash = LerTextoOpcional(leitor, 4),
        Perfil = PerfilUsuarioExtensoes.DeCodigo(leitor.GetString(5)),
        Ativo = LerBooleano(leitor, 6),
        DataCadastro = LerData(leitor, 7),
        UltimoAcessoEm = LerDataOpcional(leitor, 8),
        Uuid = leitor.GetString(9),
        AtualizadoEm = LerData(leitor, 10),
        Excluido = LerBooleano(leitor, 11)
    };
}
