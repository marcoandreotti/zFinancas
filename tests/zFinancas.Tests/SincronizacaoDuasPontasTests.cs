using zFinancas.Core.Dominio.Enums;
using zFinancas.Tests.Apoio;

namespace zFinancas.Tests;

/// <summary>
/// Cenários de duas pontas: o que acontece quando PC e celular criam, alteram e excluem
/// coisas diferentes antes de sincronizar.
/// </summary>
public class SincronizacaoDuasPontasTests
{
    [Fact]
    public async Task Cada_aparelho_cria_um_lancamento_e_os_dois_terminam_com_os_dois()
    {
        // PC monta a base e o celular entra a partir de um pacote (mesmos UUIDs).
        await using var pc = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await pc.ConfigurarAdministradorAsync("Marco", "senha123");
        var contaPc = await pc.CriarContaAsync("Poupança");
        var centroPc = await pc.CriarCentroAsync("Mercado");
        await pc.SessaoAtualizarContasAsync();

        await using var celular = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await celular.Sincronizacao.ImportarTextoAsync(await pc.Sincronizacao.ExportarTextoAsync());

        var usuarioCelular = (await celular.UsuarioRepositorio.ListarAsync()).Single(u => u.Nome == "Marco");
        await celular.Autenticacao.EntrarAsync(usuarioCelular.Id, "senha123");
        await celular.SessaoAtualizarContasAsync();

        var contaCelular = (await celular.ContaRepositorio.ListarAsync()).Single(c => c.Descricao == "Poupança");
        var centroCelular = (await celular.CentroRepositorio.ListarAsync()).Single(c => c.Descricao == "Mercado");

        // Cada lado cria o seu, sem saber do outro.
        await pc.LancarAsync(contaPc.Id, centroPc.Id, -100m, new DateTime(2025, 3, 14), "Compra no PC",
            dataLancamento: new DateTime(2025, 3, 14));

        await celular.LancarAsync(contaCelular.Id, centroCelular.Id, -50m, new DateTime(2025, 3, 15), "Compra no celular",
            dataLancamento: new DateTime(2025, 3, 15));

        // Mão dupla: PC -> celular, depois celular -> PC.
        await celular.Sincronizacao.ImportarTextoAsync(await pc.Sincronizacao.ExportarTextoAsync());
        await pc.Sincronizacao.ImportarTextoAsync(await celular.Sincronizacao.ExportarTextoAsync());

        var noPc = await pc.LancamentoRepositorio.ListarPorContaAsync(contaPc.Id);
        var noCelular = await celular.LancamentoRepositorio.ListarPorContaAsync(contaCelular.Id);

        // Nada é apagado por "não existir do outro lado": os dois ficam com os dois.
        Assert.Equal(2, noPc.Count);
        Assert.Equal(2, noCelular.Count);

        Assert.Equal(
            ["Compra no PC", "Compra no celular"],
            noPc.Select(l => l.Descricao).OrderBy(d => d, StringComparer.Ordinal).ToArray());

        Assert.Equal(
            noPc.Select(l => l.Uuid).OrderBy(u => u, StringComparer.Ordinal).ToArray(),
            noCelular.Select(l => l.Uuid).OrderBy(u => u, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Mesmo_lancamento_alterado_nos_dois_lados_vale_o_mais_recente()
    {
        await using var pc = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await pc.ConfigurarAdministradorAsync("Marco", "senha123");
        var conta = await pc.CriarContaAsync("Poupança");
        var centro = await pc.CriarCentroAsync("Mercado");
        var lancamento = await pc.LancarAsync(conta.Id, centro.Id, -100m, new DateTime(2025, 3, 14), "Original",
            dataLancamento: new DateTime(2025, 3, 14));

        await using var celular = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await celular.Sincronizacao.ImportarTextoAsync(await pc.Sincronizacao.ExportarTextoAsync());

        var usuarioCelular = (await celular.UsuarioRepositorio.ListarAsync()).Single(u => u.Nome == "Marco");
        await celular.Autenticacao.EntrarAsync(usuarioCelular.Id, "senha123");
        await celular.SessaoAtualizarContasAsync();

        var contaCelular = (await celular.ContaRepositorio.ListarAsync()).Single(c => c.Descricao == "Poupança");
        var lancamentoCelular = (await celular.LancamentoRepositorio.ListarPorContaAsync(contaCelular.Id)).Single();

        // PC edita "ontem" e celular edita "hoje": o celular é mais recente e vence.
        pc.Relogio.AvancarDias(-1);
        await pc.Lancamentos.AtualizarAsync(new Core.Aplicacao.Modelos.RequisicaoLancamento(
            lancamento.Id, conta.Id, centro.Id, "Editado no PC", -100m, new DateTime(2025, 3, 14),
            Recorrencia.Unica, 1, false, null, null, new DateTime(2025, 3, 14)));

        await celular.Lancamentos.AtualizarAsync(new Core.Aplicacao.Modelos.RequisicaoLancamento(
            lancamentoCelular.Id, contaCelular.Id, centro.Id, "Editado no celular", -100m, new DateTime(2025, 3, 14),
            Recorrencia.Unica, 1, false, null, null, new DateTime(2025, 3, 14)));

        await celular.Sincronizacao.ImportarTextoAsync(await pc.Sincronizacao.ExportarTextoAsync());
        await pc.Sincronizacao.ImportarTextoAsync(await celular.Sincronizacao.ExportarTextoAsync());

        var noPc = await pc.LancamentoRepositorio.ObterPorIdAsync(lancamento.Id);
        var noCelular = await celular.LancamentoRepositorio.ListarPorContaAsync(contaCelular.Id);

        Assert.Single(noCelular);
        Assert.Equal("Editado no celular", noPc!.Descricao);
        Assert.Equal("Editado no celular", noCelular[0].Descricao);
    }

    [Fact]
    public async Task Exclusao_feita_num_lado_apaga_no_outro()
    {
        await using var pc = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await pc.ConfigurarAdministradorAsync("Marco", "senha123");
        var conta = await pc.CriarContaAsync("Poupança");
        var centro = await pc.CriarCentroAsync("Mercado");
        await pc.LancarAsync(conta.Id, centro.Id, -70m, new DateTime(2025, 3, 10), "Some daqui",
            dataLancamento: new DateTime(2025, 3, 10));

        var outro = await pc.LancarAsync(conta.Id, centro.Id, -30m, new DateTime(2025, 3, 11), "Continua",
            dataLancamento: new DateTime(2025, 3, 11));

        await using var celular = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await celular.Sincronizacao.ImportarTextoAsync(await pc.Sincronizacao.ExportarTextoAsync());

        var usuarioCelular = (await celular.UsuarioRepositorio.ListarAsync()).Single(u => u.Nome == "Marco");
        await celular.Autenticacao.EntrarAsync(usuarioCelular.Id, "senha123");
        await celular.SessaoAtualizarContasAsync();

        // PC exclui um lançamento; o celular não sabe de nada.
        pc.Relogio.AvancarDias(1);
        var noPc = await pc.LancamentoRepositorio.ListarPorContaAsync(conta.Id);
        var alvo = noPc.Single(l => l.Descricao == "Some daqui");
        await pc.Lancamentos.ExcluirAsync(alvo.Id);

        await celular.Sincronizacao.ImportarTextoAsync(await pc.Sincronizacao.ExportarTextoAsync());
        await pc.Sincronizacao.ImportarTextoAsync(await celular.Sincronizacao.ExportarTextoAsync());

        var contaCelular = (await celular.ContaRepositorio.ListarAsync()).Single(c => c.Descricao == "Poupança");
        var restantesCelular = await celular.LancamentoRepositorio.ListarPorContaAsync(contaCelular.Id);
        var restantesPc = await pc.LancamentoRepositorio.ListarPorContaAsync(conta.Id);

        Assert.Single(restantesCelular);
        Assert.Equal("Continua", restantesCelular[0].Descricao);

        Assert.Single(restantesPc);
        Assert.Equal(outro.Uuid, restantesPc[0].Uuid);
    }
}
