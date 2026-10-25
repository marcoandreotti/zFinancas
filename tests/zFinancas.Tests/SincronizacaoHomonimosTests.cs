using zFinancas.Tests.Apoio;

namespace zFinancas.Tests;

/// <summary>
/// Dois aparelhos configurados de forma independente criam a mesma conta/centro/usuário
/// com UUIDs diferentes. Como DESCRICAO/NOME são únicos, a importação precisa reconhecer
/// que é a mesma entidade e unir os lançamentos nela — antes isso estourava
/// "UNIQUE constraint failed: CONTACORRENTE.DESCRICAO".
/// </summary>
public class SincronizacaoHomonimosTests
{
    [Fact]
    public async Task Contas_com_mesmo_nome_em_bases_independentes_sao_unificadas()
    {
        // PC configurado sozinho: cria "Conta Principal" com o UUID dele.
        await using var pc = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await pc.ConfigurarAdministradorAsync("Marco", "senha123");

        // Celular configurado sozinho: cria OUTRA "Conta Principal", com outro UUID.
        await using var celular = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await celular.ConfigurarAdministradorAsync("Marco", "senha123");

        var contaPc = (await pc.ContaRepositorio.ListarAsync()).Single();
        var contaCelular = (await celular.ContaRepositorio.ListarAsync()).Single();

        Assert.Equal("Conta Principal", contaPc.Descricao);
        Assert.Equal("Conta Principal", contaCelular.Descricao);
        Assert.NotEqual(contaPc.Uuid, contaCelular.Uuid);

        var centroPc = await pc.CriarCentroAsync("Mercado");
        var centroCelular = await celular.CriarCentroAsync("Mercado");

        await pc.SessaoAtualizarContasAsync();
        await celular.SessaoAtualizarContasAsync();

        // Cada lado registra algo.
        await pc.LancarAsync(contaPc.Id, centroPc.Id, -100m, new DateTime(2025, 3, 14), "Compra no PC",
            dataLancamento: new DateTime(2025, 3, 14));

        await celular.LancarAsync(contaCelular.Id, centroCelular.Id, -50m, new DateTime(2025, 3, 15), "Compra no celular",
            dataLancamento: new DateTime(2025, 3, 15));

        // Importar não pode estourar o UNIQUE, e os lançamentos caem na conta local.
        var resultado = await celular.Sincronizacao.ImportarTextoAsync(await pc.Sincronizacao.ExportarTextoAsync());

        Assert.NotEmpty(resultado.Avisos);
        Assert.Contains(resultado.Avisos, a => a.Contains("Conta Principal", StringComparison.OrdinalIgnoreCase));

        var contasCelular = await celular.ContaRepositorio.ListarAsync();
        Assert.Single(contasCelular); // continua uma só, sem duplicar

        var lancamentosCelular = await celular.LancamentoRepositorio.ListarPorContaAsync(contaCelular.Id);
        Assert.Equal(2, lancamentosCelular.Count);
        Assert.Contains(lancamentosCelular, l => l.Descricao == "Compra no PC");
        Assert.Contains(lancamentosCelular, l => l.Descricao == "Compra no celular");

        // E o caminho de volta também funciona.
        var volta = await pc.Sincronizacao.ImportarTextoAsync(await celular.Sincronizacao.ExportarTextoAsync());

        Assert.Single(await pc.ContaRepositorio.ListarAsync());
        var lancamentosPc = await pc.LancamentoRepositorio.ListarPorContaAsync(contaPc.Id);
        Assert.Equal(2, lancamentosPc.Count);
        Assert.Empty(volta.Avisos);
    }
}
