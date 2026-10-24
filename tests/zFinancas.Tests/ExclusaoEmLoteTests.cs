using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.Tests;

/// <summary>
/// Exclusão em lote e quitação com seleção: o usuário escolhe quais parcelas em aberto
/// entram na operação (painel de seleção) e todas são tratadas de uma vez.
/// </summary>
public class ExclusaoEmLoteTests
{
    private static readonly DateTime Hoje = new(2026, 10, 24);

    private static async Task<(long ContaId, ResultadoCriacaoLancamentos Serie)> PrepararSerieAsync(
        Apoio.CenarioTeste cenario)
    {
        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var serie = await cenario.Lancamentos.CriarAsync(new RequisicaoLancamento(
            0, conta.Id, centro.Id, "Curso de inglês", -260m, Hoje.AddDays(10),
            Recorrencia.Mensal, 6, false, null, null));

        return (conta.Id, serie);
    }

    [Fact]
    public async Task Exclui_varias_parcelas_escolhidas_de_uma_vez()
    {
        await using var cenario = await Apoio.CenarioTeste.CriarAsync(Hoje);
        await cenario.ConfigurarAdministradorAsync();

        var (contaId, serie) = await PrepararSerieAsync(cenario);
        Assert.Equal(6, serie.Lancamentos.Count);

        var escolhidas = serie.Lancamentos
            .Where(l => l.Parcela is 2 or 4 or 6)
            .Select(l => l.Id)
            .ToList();

        await cenario.Lancamentos.ExcluirVariasAsync(escolhidas);

        var restantes = await cenario.LancamentoRepositorio.ListarPorContaAsync(contaId);

        Assert.Equal(3, restantes.Count);
        Assert.Equal([1L, 3L, 5L], restantes.Select(l => l.Parcela).OrderBy(p => p));
    }

    [Fact]
    public async Task Nao_exclui_parcela_compensada()
    {
        await using var cenario = await Apoio.CenarioTeste.CriarAsync(Hoje);
        await cenario.ConfigurarAdministradorAsync();

        var (contaId, serie) = await PrepararSerieAsync(cenario);

        var compensada = serie.Lancamentos.First(l => l.Parcela == 1);
        await cenario.Lancamentos.CompensarAsync(compensada.Id, Hoje);

        var outra = serie.Lancamentos.First(l => l.Parcela == 2);

        var erro = await Assert.ThrowsAsync<DominioException>(
            () => cenario.Lancamentos.ExcluirVariasAsync([compensada.Id, outra.Id]));

        Assert.Contains("compensado", erro.Message, StringComparison.OrdinalIgnoreCase);

        // tudo ou nada: a parcela 2 continua lá
        var restantes = await cenario.LancamentoRepositorio.ListarPorContaAsync(contaId);
        Assert.Equal(6, restantes.Count);
    }

    [Fact]
    public async Task Lista_vazia_e_rejeitada()
    {
        await using var cenario = await Apoio.CenarioTeste.CriarAsync(Hoje);
        await cenario.ConfigurarAdministradorAsync();

        await Assert.ThrowsAsync<DominioException>(() => cenario.Lancamentos.ExcluirVariasAsync([]));
    }

    [Fact]
    public async Task Quitacao_com_selecao_parcial_consolida_somente_as_escolhidas()
    {
        await using var cenario = await Apoio.CenarioTeste.CriarAsync(Hoje);
        await cenario.ConfigurarAdministradorAsync();

        var (contaId, serie) = await PrepararSerieAsync(cenario);

        var escolhidas = serie.Lancamentos
            .Where(l => l.Parcela >= 4)
            .Select(l => l.Id)
            .ToList();

        var plano = await cenario.Lancamentos.PlanejarQuitacaoAsync(escolhidas, Hoje);

        Assert.Equal(3, plano.QuantidadeParcelas);
        Assert.Equal(-780m, plano.ValorTotal);

        await cenario.Lancamentos.QuitarAsync(plano);

        var restantes = await cenario.LancamentoRepositorio.ListarPorContaAsync(contaId);

        // sobram as 3 primeiras em aberto + o lançamento consolidado (compensado)
        Assert.Equal(4, restantes.Count);
        Assert.Equal([1L, 2L, 3L], restantes.Where(l => !l.Compensado).Select(l => l.Parcela).OrderBy(p => p));

        var quitacao = Assert.Single(restantes.Where(l => l.Compensado));
        Assert.Equal(-780m, quitacao.Valor);
        Assert.Contains("Quitação", quitacao.Descricao, StringComparison.OrdinalIgnoreCase);
    }
}
