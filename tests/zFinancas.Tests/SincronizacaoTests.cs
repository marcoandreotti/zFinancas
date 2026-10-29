using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Tests.Apoio;

namespace zFinancas.Tests;

public class SincronizacaoTests
{
    [Fact]
    public async Task Pacote_exportado_no_pc_e_importado_no_celular()
    {
        await using var pc = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await pc.ConfigurarAdministradorAsync("Marco", "senha123");

        var poupanca = await pc.CriarContaAsync("Poupança");
        var viagem = await pc.CriarCentroAsync("Viagem");

        await pc.LancarAsync(poupanca.Id, viagem.Id, -250m, new DateTime(2025, 3, 10), "Hotel",
            compensado: true, dataCompensado: new DateTime(2025, 3, 10), dataLancamento: new DateTime(2025, 3, 10));

        var json = await pc.Sincronizacao.ExportarTextoAsync();
        Assert.Contains("Poupança", json);
        Assert.Contains("Marco", json);
        Assert.DoesNotContain("senha123", json); // nunca a senha em claro

        await using var celular = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        var resultado = await celular.Sincronizacao.ImportarTextoAsync(json);

        Assert.True(resultado.Total >= 5, $"esperava ao menos 5 registros, veio {resultado.Total}");
        Assert.Empty(resultado.Avisos);

        // O mesmo usuário/senha funciona no outro aparelho.
        var usuario = (await celular.UsuarioRepositorio.ListarAsync()).Single(u => u.Nome == "Marco");
        await celular.Autenticacao.EntrarAsync(usuario.Id, "senha123");
        Assert.True(celular.Sessao.Autenticado);

        var contaCelular = (await celular.ContaRepositorio.ListarAsync())
            .Single(c => c.Descricao == "Poupança");

        var lancamentosCelular = await celular.LancamentoRepositorio.ListarPorContaAsync(contaCelular.Id);
        Assert.Single(lancamentosCelular);
        Assert.Equal(-250m, lancamentosCelular[0].Valor);
        Assert.True(lancamentosCelular[0].Compensado);
    }

    [Fact]
    public async Task Importar_o_mesmo_pacote_duas_vezes_nao_duplica_dados()
    {
        await using var pc = await CenarioTeste.CriarAsync();
        await pc.ConfigurarAdministradorAsync();
        await pc.CriarContaAsync("Reserva");
        await pc.CriarCentroAsync("Educação");

        var json = await pc.Sincronizacao.ExportarTextoAsync();

        await using var celular = await CenarioTeste.CriarAsync();
        await celular.Sincronizacao.ImportarTextoAsync(json);

        var contasAntes = (await celular.ContaRepositorio.ListarAsync(incluirInativas: true)).Count;
        var usuariosAntes = (await celular.UsuarioRepositorio.ListarAsync()).Count;

        var segundo = await celular.Sincronizacao.ImportarTextoAsync(json);

        Assert.Equal(0, segundo.Total);
        Assert.Equal(contasAntes, (await celular.ContaRepositorio.ListarAsync(incluirInativas: true)).Count);
        Assert.Equal(usuariosAntes, (await celular.UsuarioRepositorio.ListarAsync()).Count);
    }

    [Fact]
    public async Task Alteracao_feita_no_celular_volta_para_o_pc()
    {
        await using var pc = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await pc.ConfigurarAdministradorAsync("Marco", "senha123");

        var conta = (await pc.ContaRepositorio.ListarAsync())[0];
        var centro = await pc.CriarCentroAsync();
        var lancamento = await pc.LancarAsync(conta.Id, centro.Id, -90m, new DateTime(2025, 3, 14), "Mercado",
            dataLancamento: new DateTime(2025, 3, 14));

        await using var celular = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await celular.Sincronizacao.ImportarTextoAsync(await pc.Sincronizacao.ExportarTextoAsync());

        var usuarioCelular = (await celular.UsuarioRepositorio.ListarAsync()).Single();
        await celular.Autenticacao.EntrarAsync(usuarioCelular.Id, "senha123");

        // O celular compensa o lançamento um dia depois.
        celular.Relogio.AvancarDias(1);
        var contaCelular = (await celular.ContaRepositorio.ListarAsync()).Single(c => c.Descricao == conta.Descricao);
        var lancamentoCelular = (await celular.LancamentoRepositorio.ListarPorContaAsync(contaCelular.Id)).Single();
        await celular.Lancamentos.CompensarAsync(lancamentoCelular.Id, new DateTime(2025, 3, 16));

        var retorno = await celular.Sincronizacao.ExportarTextoAsync();
        await pc.Sincronizacao.ImportarTextoAsync(retorno);

        var atualizado = await pc.LancamentoRepositorio.ObterPorIdAsync(lancamento.Id);
        Assert.NotNull(atualizado);
        Assert.True(atualizado!.Compensado);
        Assert.Equal(new DateTime(2025, 3, 16), atualizado.DataCompensado);
    }

    [Fact]
    public async Task Vinculo_de_conta_do_usr_e_sincronizado()
    {
        await using var pc = await CenarioTeste.CriarAsync();
        await pc.ConfigurarAdministradorAsync();

        var conta = await pc.CriarContaAsync("Poupança");
        var (usuario, _) = await pc.Usuarios.CriarAsync(
            new RequisicaoUsuario(0, "Ana", "ana123", null, PerfilUsuario.Usuario, true, [conta.Id]));

        await using var celular = await CenarioTeste.CriarAsync();
        await celular.Sincronizacao.ImportarTextoAsync(await pc.Sincronizacao.ExportarTextoAsync());

        var anaCelular = (await celular.UsuarioRepositorio.ListarAsync()).Single(u => u.Nome == "Ana");
        var contasVinculadas = await celular.VinculoRepositorio.ListarContasDoUsuarioAsync(anaCelular.Id);

        var contaCelular = (await celular.ContaRepositorio.ListarAsync()).Single(c => c.Descricao == "Poupança");
        Assert.Single(contasVinculadas);
        Assert.Equal(contaCelular.Id, contasVinculadas[0]);

        // A identidade global (UUID) é preservada; os Ids locais podem coincidir ou não.
        Assert.Equal(usuario.Uuid, anaCelular.Uuid);
    }

    [Fact]
    public async Task Arquivo_invalido_gera_erro_amigavel()
    {
        await using var cenario = await CenarioTeste.CriarAsync();

        var erro = await Assert.ThrowsAsync<Core.Dominio.Excecoes.DominioException>(
            () => cenario.Sincronizacao.ImportarTextoAsync("{ isso não é json }"));

        Assert.Contains("sincronização", erro.Message, StringComparison.OrdinalIgnoreCase);
    }
}
