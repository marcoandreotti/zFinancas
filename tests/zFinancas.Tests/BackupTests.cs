using Microsoft.Data.Sqlite;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Data.Infra;
using zFinancas.Data.Repositorios;
using zFinancas.Tests.Apoio;

namespace zFinancas.Tests;

/// <summary>
/// Backup e restauração do banco completo: o arquivo gerado precisa devolver exatamente o
/// estado do momento do backup, e arquivos inválidos não podem ser aceitos.
/// </summary>
public class BackupTests
{
    [Fact]
    public async Task Backup_restaura_o_estado_do_momento_em_que_foi_gerado()
    {
        await using var cenario = await CenarioTeste.CriarAsync(new DateTime(2025, 4, 10));
        await cenario.ConfigurarAdministradorAsync("Marco", "senha123");

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync("Mercado");

        await cenario.LancarAsync(conta.Id, centro.Id, -120m, new DateTime(2025, 4, 5), "Antes do backup",
            dataLancamento: new DateTime(2025, 4, 5));

        var servico = new ServicoBackupBanco(cenario.Contexto);
        var backup = await servico.CriarAsync();

        Assert.True(backup.Conteudo.Length > 0);
        Assert.EndsWith(".db", backup.NomeArquivo, StringComparison.Ordinal);
        Assert.Equal(MigradorBanco.VersaoAtual, backup.VersaoEsquema);

        var antes = await cenario.LancamentoRepositorio.ListarPorContaAsync(conta.Id);
        Assert.Single(antes);

        // Depois do backup o usuário continua trabalhando.
        await cenario.LancarAsync(conta.Id, centro.Id, -50m, new DateTime(2025, 4, 6), "Depois do backup",
            dataLancamento: new DateTime(2025, 4, 6));

        Assert.Equal(2, (await cenario.LancamentoRepositorio.ListarPorContaAsync(conta.Id)).Count);

        var seguranca = await servico.RestaurarAsync(backup.Conteudo);

        Assert.True(File.Exists(seguranca), "a cópia de segurança do banco anterior deve existir");

        var restaurados = await cenario.LancamentoRepositorio.ListarPorContaAsync(conta.Id);

        Assert.Single(restaurados);
        Assert.Equal("Antes do backup", restaurados[0].Descricao);
    }

    [Fact]
    public async Task Arquivo_que_nao_e_banco_e_recusado()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var servico = new ServicoBackupBanco(cenario.Contexto);

        var erro = await Assert.ThrowsAsync<DominioException>(
            () => servico.RestaurarAsync("isto não é um banco"u8.ToArray()));

        Assert.Contains("não", erro.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("backup", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Backup_de_versao_mais_nova_e_recusado()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        // Um SQLite válido, mas com esquema do futuro.
        var futuro = Path.Combine(Path.GetTempPath(), $"zfinancas-futuro-{Guid.NewGuid():N}.db");

        try
        {
            await using (var conexao = new SqliteConnection($"Data Source={futuro};Pooling=False"))
            {
                await conexao.OpenAsync();

                await using var comando = conexao.CreateCommand();
                comando.CommandText = "CREATE TABLE TESTE (ID INTEGER PRIMARY KEY); PRAGMA user_version=99;";
                await comando.ExecuteNonQueryAsync();
            }

            var conteudo = await File.ReadAllBytesAsync(futuro);
            var servico = new ServicoBackupBanco(cenario.Contexto);

            var erro = await Assert.ThrowsAsync<DominioException>(() => servico.RestaurarAsync(conteudo));

            Assert.Contains("mais nova", erro.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            try
            {
                if (File.Exists(futuro))
                {
                    File.Delete(futuro);
                }
            }
            catch (IOException)
            {
                // arquivo temporário de teste: fica no %TEMP% sem prejuízo
            }
        }
    }

    [Fact]
    public async Task Restauracao_nao_deixa_restos_do_banco_antigo()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var servico = new ServicoBackupBanco(cenario.Contexto);
        var backup = await servico.CriarAsync();

        await servico.RestaurarAsync(backup.Conteudo);

        var caminho = cenario.Contexto.CaminhoArquivo;

        // O -wal/-shm antigos invalidariam a leitura; o contexto quebra se existirem.
        Assert.False(File.Exists(caminho + "-wal"));
        Assert.False(File.Exists(caminho + "-shm"));

        Assert.Single(await cenario.UsuarioRepositorio.ListarAsync());
    }
}
