using System.Text;
using Microsoft.Data.Sqlite;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Data.Repositorios;

namespace zFinancas.Data.Infra;

/// <summary>Arquivo de backup pronto para ser gravado onde o usuário escolher.</summary>
public sealed record BackupBanco(byte[] Conteudo, string NomeArquivo, DateTime GeradoEmUtc, int VersaoEsquema)
{
    public int TamanhoBytes => Conteudo.Length;
}

/// <summary>
/// Backup e restauração do banco inteiro.
/// <para>
/// O backup é uma cópia **consistente** do SQLite (via <c>VACUUM INTO</c>), então não carrega
/// sobras do <c>-wal</c> e pode ser gerada com o app aberto. A restauração valida o arquivo,
/// guarda uma cópia do banco atual antes de substituir e fecha a conexão para trocar o arquivo.
/// </para>
/// </summary>
public sealed class ServicoBackupBanco
{
    private const string CabecalhoSqlite = "SQLite format 3";

    private readonly ContextoBanco _contexto;

    public ServicoBackupBanco(ContextoBanco contexto) => _contexto = contexto;

    /// <summary>Gera o backup completo do banco.</summary>
    public async Task<BackupBanco> CriarAsync(CancellationToken ct = default)
    {
        // O temporário fica na pasta do banco: ela é gravável por definição (o arquivo vive
        // ali) e o %TEMP% pode estar bloqueado em ambientes restritos.
        var temporario = Path.Combine(PastaDoBanco(), $".backup-{Guid.NewGuid():N}.db");

        try
        {
            // VACUUM INTO produz um arquivo limpo (sem -wal/-shm) mesmo com o banco em uso.
            await using (var comando = await _contexto.CriarComandoAsync(
                $"VACUUM INTO '{temporario.Replace("'", "''")}';", ct).ConfigureAwait(false))
            {
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            var conteudo = await File.ReadAllBytesAsync(temporario, ct).ConfigureAwait(false);
            var versao = await LerVersaoAsync(temporario, ct).ConfigureAwait(false);

            var agora = DateTime.UtcNow;

            return new BackupBanco(
                conteudo,
                $"zfinancas-backup-{agora.ToLocalTime():yyyyMMdd-HHmm}.db",
                agora,
                versao);
        }
        catch (Exception ex) when (ex is not DominioException)
        {
            throw new DominioException($"Não foi possível gerar o backup: {ex.Message}", ex);
        }
        finally
        {
            ApagarQuandoPossivel(temporario);
        }
    }

    /// <summary>
    /// Substitui o banco atual pelo conteúdo do backup. Devolve o caminho da cópia de
    /// segurança criada antes da troca.
    /// </summary>
    public async Task<string> RestaurarAsync(byte[] conteudo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conteudo);

        var motivo = await ValidarAsync(conteudo, ct).ConfigureAwait(false);

        if (motivo is not null)
        {
            throw new DominioException(motivo);
        }

        var caminho = _contexto.CaminhoArquivo;
        var pasta = Path.GetDirectoryName(caminho) ?? ".";

        Directory.CreateDirectory(pasta);

        var seguranca = Path.Combine(
            pasta,
            $"zfinancas-antes-restauracao-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        // Fecha a conexão e libera o arquivo antes de trocar.
        await _contexto.FecharConexaoAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();

        if (File.Exists(caminho))
        {
            File.Copy(caminho, seguranca, overwrite: true);
        }

        await File.WriteAllBytesAsync(caminho, conteudo, ct).ConfigureAwait(false);

        // Restos do banco antigo fariam o SQLite misturar páginas velhas com as novas.
        foreach (var sufixo in new[] { "-wal", "-shm" })
        {
            var resto = caminho + sufixo;

            if (File.Exists(resto))
            {
                File.Delete(resto);
            }
        }

        return seguranca;
    }

    /// <summary>
    /// Apaga o banco atual (e os restos -wal/-shm) e recria o esquema vazio.
    /// Antes de apagar, guarda uma cópia de segurança na pasta do banco; o app volta para
    /// o primeiro acesso na próxima tela.
    /// </summary>
    public async Task<string> ZerarAsync(CancellationToken ct = default)
    {
        var caminho = _contexto.CaminhoArquivo;
        var pasta = Path.GetDirectoryName(caminho) ?? ".";

        // Cópia de segurança: o usuário pode restaurar este arquivo se mudar de ideia.
        var seguranca = CaminhoUnico(pasta, $"zfinancas-antes-de-zerar-{DateTime.Now:yyyyMMdd-HHmmss}");

        try
        {
            await using (var comando = await _contexto.CriarComandoAsync(
                $"VACUUM INTO '{seguranca.Replace("'", "''")}';", ct).ConfigureAwait(false))
            {
                await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is SqliteException)
        {
            throw new DominioException($"Não foi possível guardar a cópia de segurança antes de zerar: {ex.Message}", ex);
        }

        await _contexto.FecharConexaoAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();

        foreach (var sufixo in new[] { string.Empty, "-wal", "-shm" })
        {
            var alvo = caminho + sufixo;

            if (File.Exists(alvo))
            {
                File.Delete(alvo);
            }
        }

        // Recria o esquema para o app já poder abrir o primeiro acesso.
        await new Repositorios.MigradorBanco(_contexto).AplicarAsync(ct).ConfigureAwait(false);

        return seguranca;
    }

    /// <summary>Valida cabeçalho, integridade e versão do esquema. Null = pode restaurar.</summary>
    private async Task<string?> ValidarAsync(byte[] conteudo, CancellationToken ct)
    {
        if (conteudo.Length < 512)
        {
            return "O arquivo escolhido não parece um backup do zFinancas (muito pequeno).";
        }

        var cabecalho = Encoding.ASCII.GetString(conteudo, 0, CabecalhoSqlite.Length);

        if (!string.Equals(cabecalho, CabecalhoSqlite, StringComparison.Ordinal))
        {
            return "O arquivo escolhido não é um banco do zFinancas.";
        }

        var temporario = Path.Combine(PastaDoBanco(), $".validar-{Guid.NewGuid():N}.db");

        try
        {
            await File.WriteAllBytesAsync(temporario, conteudo, ct).ConfigureAwait(false);

            var versao = await LerVersaoAsync(temporario, ct).ConfigureAwait(false);

            if (versao > MigradorBanco.VersaoAtual)
            {
                return $"Este backup é de uma versão mais nova do zFinancas (esquema {versao}, " +
                       $"este app entende até {MigradorBanco.VersaoAtual}). Atualize o aplicativo.";
            }

            await using var conexao = new SqliteConnection($"Data Source={temporario};Mode=ReadOnly");
            await conexao.OpenAsync(ct).ConfigureAwait(false);

            await using var comando = conexao.CreateCommand();
            comando.CommandText = "PRAGMA integrity_check;";

            var resultado = (await comando.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString();

            return string.Equals(resultado, "ok", StringComparison.OrdinalIgnoreCase)
                ? null
                : "O backup está corrompido (falha na verificação de integridade).";
        }
        catch (Exception ex) when (ex is not DominioException)
        {
            return $"Não consegui ler o backup: {ex.Message}";
        }
        finally
        {
            ApagarQuandoPossivel(temporario);
        }
    }

    /// <summary>
    /// Remove o temporário tolerando o lock momentâneo do SQLite (o arquivo não é do usuário;
    /// deixá-lo no %TEMP% é inofensivo).
    /// </summary>
    /// <summary>Nome livre na pasta (acrescenta "-2", "-3"… quando o arquivo já existe).</summary>
    private static string CaminhoUnico(string pasta, string baseNome)
    {
        var caminho = Path.Combine(pasta, $"{baseNome}.db");
        var contador = 1;

        while (File.Exists(caminho))
        {
            contador++;
            caminho = Path.Combine(pasta, $"{baseNome}-{contador}.db");
        }

        return caminho;
    }

    private string PastaDoBanco()
        => Path.GetDirectoryName(_contexto.CaminhoArquivo) ?? Directory.GetCurrentDirectory();

    private static void ApagarQuandoPossivel(string caminho)
    {
        for (var tentativa = 0; tentativa < 3; tentativa++)
        {
            try
            {
                if (!File.Exists(caminho))
                {
                    return;
                }

                File.Delete(caminho);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(80);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private static async Task<int> LerVersaoAsync(string caminho, CancellationToken ct)
    {
        await using var conexao = new SqliteConnection($"Data Source={caminho};Mode=ReadOnly");
        await conexao.OpenAsync(ct).ConfigureAwait(false);

        await using var comando = conexao.CreateCommand();
        comando.CommandText = "PRAGMA user_version;";

        var valor = await comando.ExecuteScalarAsync(ct).ConfigureAwait(false);

        return valor is null ? 0 : Convert.ToInt32(valor, System.Globalization.CultureInfo.InvariantCulture);
    }
}
