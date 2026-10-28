using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Tests.Apoio;

namespace zFinancas.Tests;

/// <summary>
/// Séries de parcelas: recorrência gravada, propagação de valor, acréscimo de parcelas
/// e os totais usados no extrato (duplo clique na linha).
/// </summary>
public class SerieParcelasTests
{
    private static readonly DateTime Vencimento = new(2026, 3, 10);

    [Fact]
    public async Task Criar_serie_grava_a_recorrencia_em_todas_as_parcelas()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var criado = await cenario.Lancamentos.CriarAsync(new RequisicaoLancamento(
            0, conta.Id, centro.Id, "Financiamento", -500m, Vencimento,
            Recorrencia.Mensal, 4, false, null, null));

        Assert.Equal(4, criado.Lancamentos.Count);
        Assert.All(criado.Lancamentos, l => Assert.Equal(Recorrencia.Mensal, l.Recorrencia));
        Assert.All(criado.Lancamentos, l => Assert.Equal(4, l.QuantidadeParcelas));

        // Datas espaçadas de mês em mês.
        Assert.Equal(
            [new DateTime(2026, 3, 10), new DateTime(2026, 4, 10), new DateTime(2026, 5, 10), new DateTime(2026, 6, 10)],
            criado.Lancamentos.OrderBy(l => l.Parcela).Select(l => l.DataVencimento).ToArray());
    }

    [Fact]
    public async Task Propagar_valor_ajusta_somente_as_parcelas_em_aberto()
    {
        await using var cenario = await CenarioTeste.CriarAsync(new DateTime(2026, 3, 1));
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var serie = await cenario.Lancamentos.CriarAsync(new RequisicaoLancamento(
            0, conta.Id, centro.Id, "Curso", -300m, Vencimento,
            Recorrencia.Mensal, 3, false, null, null));

        // Compensa a primeira parcela: ela não deve ser tocada.
        await cenario.Lancamentos.CompensarAsync(serie.Lancamentos.OrderBy(l => l.Parcela).First().Id);

        var ajustadas = await cenario.Lancamentos.PropagarValorAsync(serie.Lancamentos[0].Id, -350m);

        Assert.Equal(2, ajustadas);

        var depois = await cenario.Lancamentos.ListarSerieAsync(serie.Lancamentos[0].ChaveDaSerie!);

        Assert.Equal(-300m, depois.Single(l => l.Parcela == 1).Valor);
        Assert.All(depois.Where(l => l.Parcela > 1), l => Assert.Equal(-350m, l.Valor));
    }

    [Fact]
    public async Task Acrescentar_parcelas_renumera_a_serie_e_cria_as_novas()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var serie = await cenario.Lancamentos.CriarAsync(new RequisicaoLancamento(
            0, conta.Id, centro.Id, "Notebook", -1000m, Vencimento,
            Recorrencia.Mensal, 3, false, null, null));

        var criadas = await cenario.Lancamentos.AcrescentarParcelasAsync(serie.Lancamentos[0].Id, 5);

        Assert.Equal(2, criadas.Count);

        var depois = (await cenario.Lancamentos.ListarSerieAsync(serie.Lancamentos[0].ChaveDaSerie!))
            .OrderBy(l => l.Parcela)
            .ToList();

        Assert.Equal(5, depois.Count);
        Assert.All(depois, l => Assert.Equal(5, l.QuantidadeParcelas));
        Assert.Equal(Recorrencia.Mensal, depois[^1].Recorrencia);

        // As novas continuam o espaçamento a partir da última.
        Assert.Equal(new DateTime(2026, 6, 10), depois[3].DataVencimento);
        Assert.Equal(new DateTime(2026, 7, 10), depois[4].DataVencimento);
        Assert.Equal(-1000m, depois[4].Valor);
    }

    [Fact]
    public async Task Acrescentar_parcelas_sem_recorrencia_pede_para_escolher()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var serie = await cenario.Lancamentos.CriarAsync(new RequisicaoLancamento(
            0, conta.Id, centro.Id, "Antigo", -200m, Vencimento,
            Recorrencia.Mensal, 2, false, null, null));

        // Simula os lançamentos antigos, criados antes de existir a coluna RECORRENCIA.
        foreach (var parcela in serie.Lancamentos)
        {
            parcela.Recorrencia = null;
            await cenario.LancamentoRepositorio.SalvarAsync(parcela);
        }

        var erro = await Assert.ThrowsAsync<DominioException>(
            () => cenario.Lancamentos.AcrescentarParcelasAsync(serie.Lancamentos[0].Id, 4));

        Assert.Contains("repetição", erro.Message, StringComparison.OrdinalIgnoreCase);

        // Informando a repetição, funciona.
        var criadas = await cenario.Lancamentos.AcrescentarParcelasAsync(serie.Lancamentos[0].Id, 4, Recorrencia.Mensal);

        Assert.Equal(2, criadas.Count);
    }

    [Fact]
    public async Task Totais_da_serie_separam_pago_e_a_pagar()
    {
        await using var cenario = await CenarioTeste.CriarAsync(new DateTime(2026, 3, 1));
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var serie = await cenario.Lancamentos.CriarAsync(new RequisicaoLancamento(
            0, conta.Id, centro.Id, "Academia", -100m, Vencimento,
            Recorrencia.Mensal, 4, false, null, null));

        await cenario.Lancamentos.CompensarAsync(serie.Lancamentos.OrderBy(l => l.Parcela).First().Id);

        var totais = await cenario.Lancamentos.ObterTotaisDaSerieAsync(serie.Lancamentos[0].ChaveDaSerie);

        Assert.Equal(-400m, totais.Total);
        Assert.Equal(-100m, totais.Pago);
        Assert.Equal(-300m, totais.APagar);
        Assert.Equal(4, totais.Parcelas);
        Assert.Equal(1, totais.Compensadas);
    }
}
