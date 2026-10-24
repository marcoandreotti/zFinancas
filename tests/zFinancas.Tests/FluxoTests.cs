using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Tests.Apoio;

namespace zFinancas.Tests;

public class PrimeiroAcessoTests
{
    [Fact]
    public async Task Primeiro_acesso_cria_administrador_e_semeia_conta_e_centro()
    {
        await using var cenario = await CenarioTeste.CriarAsync();

        Assert.True(await cenario.PrimeiroAcesso.PrecisaConfigurarAsync());

        var resultado = await cenario.PrimeiroAcesso.ConfigurarAsync(
            new RequisicaoConfiguracaoInicial("Marco", "senha123", "apelido da infância"));

        Assert.True(resultado.Administrador.EhAdministrador);
        Assert.Equal("Marco", resultado.Administrador.Nome);
        Assert.True(resultado.Administrador.Ativo);
        Assert.True(resultado.ContaPrincipal.Principal);
        Assert.True(resultado.CentroPrincipal.Principal);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", resultado.CodigoRecuperacao);

        Assert.False(await cenario.PrimeiroAcesso.PrecisaConfigurarAsync());

        // O código de recuperação é guardado apenas como hash.
        var usuario = await cenario.UsuarioRepositorio.ObterPorIdAsync(resultado.Administrador.Id);
        Assert.NotNull(usuario);
        Assert.DoesNotContain(resultado.CodigoRecuperacao, usuario!.CodigoRecuperacaoHash);
        Assert.Equal("apelido da infância", usuario.DicaSenha);
    }

    [Fact]
    public async Task Configuracao_inicial_nao_pode_ser_repetida()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        await Assert.ThrowsAsync<DominioException>(() => cenario.PrimeiroAcesso.ConfigurarAsync(
            new RequisicaoConfiguracaoInicial("Outro", "senha123")));
    }

    [Fact]
    public async Task Senha_muito_curta_e_rejeitada_no_cadastro()
    {
        await using var cenario = await CenarioTeste.CriarAsync();

        await Assert.ThrowsAsync<DominioException>(() => cenario.PrimeiroAcesso.ConfigurarAsync(
            new RequisicaoConfiguracaoInicial("Marco", "123")));
    }
}

public class AutenticacaoTests
{
    [Fact]
    public async Task Login_com_senha_correta_inicia_sessao_com_contas_do_administrador()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        var administrador = await cenario.ConfigurarAdministradorAsync();

        Assert.True(cenario.Sessao.Autenticado);
        Assert.True(cenario.Sessao.EhAdministrador);
        Assert.Single(cenario.Sessao.ContasPermitidas);
        Assert.Equal(administrador.Id, cenario.Sessao.Usuario!.Id);
        Assert.NotNull(cenario.Sessao.Usuario.UltimoAcessoEm);
    }

    [Fact]
    public async Task Login_com_senha_incorreta_falha_sem_abrir_sessao()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        var administrador = await cenario.ConfigurarAdministradorAsync();

        cenario.Autenticacao.Sair();
        await Assert.ThrowsAsync<DominioException>(() => cenario.Autenticacao.EntrarAsync(administrador.Id, "errada"));
        Assert.False(cenario.Sessao.Autenticado);
    }

    [Fact]
    public async Task Usuario_inativo_nao_entra()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var (usuario, _) = await cenario.Usuarios.CriarAsync(
            new RequisicaoUsuario(0, "Ana", "ana123", null, PerfilUsuario.Usuario, true, []));

        await cenario.Usuarios.AlterarAtivoAsync(usuario.Id, false);

        await Assert.ThrowsAsync<DominioException>(() => cenario.Autenticacao.EntrarAsync(usuario.Id, "ana123"));
    }

    [Fact]
    public async Task Perfil_usr_acessa_somente_as_contas_vinculadas()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var contaPrincipal = (await cenario.ContaRepositorio.ListarAsync())[0];
        var poupanca = await cenario.CriarContaAsync("Poupança");

        var (usuario, _) = await cenario.Usuarios.CriarAsync(
            new RequisicaoUsuario(0, "Ana", "ana123", null, PerfilUsuario.Usuario, true, [poupanca.Id]));

        await cenario.Autenticacao.EntrarAsync(usuario.Id, "ana123");

        Assert.False(cenario.Sessao.EhAdministrador);
        Assert.Single(cenario.Sessao.ContasPermitidas);
        Assert.Equal(poupanca.Id, cenario.Sessao.ContasPermitidas[0].Id);
        Assert.False(cenario.Sessao.PodeAcessarConta(contaPrincipal.Id));

        // Regra aplicada também no serviço de extrato.
        await Assert.ThrowsAsync<DominioException>(() =>
            cenario.Extrato.ObterAsync(contaPrincipal.Id, new DateTime(2025, 3, 1), new DateTime(2025, 3, 31)));
    }

    [Fact]
    public async Task Recuperacao_de_senha_usa_codigo_de_uso_unico()
    {
        await using var cenario = await CenarioTeste.CriarAsync();

        var resultado = await cenario.PrimeiroAcesso.ConfigurarAsync(
            new RequisicaoConfiguracaoInicial("Marco", "senha123"));

        var novoCodigo = await cenario.Autenticacao.RecuperarSenhaAsync(
            "Marco", resultado.CodigoRecuperacao, "novaSenha1");

        Assert.NotEqual(resultado.CodigoRecuperacao, novoCodigo);

        cenario.Autenticacao.Sair();
        await cenario.Autenticacao.EntrarAsync(resultado.Administrador.Id, "novaSenha1");

        // O código antigo não funciona mais.
        await Assert.ThrowsAsync<DominioException>(() =>
            cenario.Autenticacao.RecuperarSenhaAsync("Marco", resultado.CodigoRecuperacao, "outraSenha1"));
    }

    [Fact]
    public async Task Administrador_nao_pode_ser_desativado_quando_e_o_unico()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        var administrador = await cenario.ConfigurarAdministradorAsync();

        await Assert.ThrowsAsync<DominioException>(() =>
            cenario.Usuarios.AlterarAtivoAsync(administrador.Id, false));
    }
}

public class LancamentoTests
{
    [Fact]
    public async Task Serie_mensal_gera_parcelas_com_mesmo_agrupador_e_agrupador_uuid()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var resultado = await cenario.Lancamentos.CriarAsync(new RequisicaoLancamento(
            0, conta.Id, centro.Id, "Aluguel", -1200m, new DateTime(2025, 1, 31),
            Recorrencia.Mensal, 3, false, null, null));

        Assert.Equal(3, resultado.Lancamentos.Count);
        Assert.True(resultado.Agrupador > 0);
        Assert.All(resultado.Lancamentos, l => Assert.Equal(resultado.Agrupador, l.Agrupador));
        Assert.All(resultado.Lancamentos, l => Assert.False(string.IsNullOrWhiteSpace(l.AgrupadorUuid)));
        Assert.Equal([1L, 2L, 3L], resultado.Lancamentos.Select(l => l.Parcela));
        Assert.All(resultado.Lancamentos, l => Assert.Equal(3, l.QuantidadeParcelas));
        Assert.Equal(new DateTime(2025, 2, 28), resultado.Lancamentos[1].DataVencimento);
    }

    [Fact]
    public async Task Lancamento_avulso_nao_tem_agrupador()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();
        var lancamento = await cenario.LancarAsync(conta.Id, centro.Id, -35.90m, new DateTime(2025, 3, 10), "Pizza");

        Assert.Equal(0, lancamento.Agrupador);
        Assert.Null(lancamento.AgrupadorUuid);
        Assert.Equal(1, lancamento.QuantidadeParcelas);
        Assert.True(lancamento.EhDespesa);
    }

    [Fact]
    public async Task Quitacao_exclui_parcelas_escolhidas_e_cria_um_unico_registro_compensado()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var resultado = await cenario.Lancamentos.CriarAsync(new RequisicaoLancamento(
            0, conta.Id, centro.Id, "Curso", -100m, new DateTime(2025, 4, 10),
            Recorrencia.Mensal, 3, false, null, null));

        var chave = resultado.Lancamentos[0].ChaveDaSerie;
        Assert.NotNull(chave);

        var plano = await cenario.Lancamentos.PlanejarQuitacaoAsync(
            resultado.Lancamentos.Take(2).Select(l => l.Id),
            new DateTime(2025, 3, 20));

        Assert.Equal(-200m, plano.ValorTotal);

        var quitacao = await cenario.Lancamentos.QuitarAsync(plano);

        Assert.True(quitacao.Compensado);
        Assert.Equal(new DateTime(2025, 3, 20), quitacao.DataCompensado);
        Assert.Equal(-200m, quitacao.Valor);
        Assert.Equal(0, quitacao.Agrupador);

        // As duas parcelas quitadas saíram da série; a terceira continua pendente.
        var serieRestante = await cenario.Lancamentos.ListarSerieAsync(chave!);
        Assert.Single(serieRestante);
        Assert.Equal(3, serieRestante[0].Parcela);

        var todos = await cenario.LancamentoRepositorio.ListarPorContaAsync(conta.Id);
        Assert.Contains(todos, l => l.Id == quitacao.Id);
        Assert.DoesNotContain(todos, l => l.Id == resultado.Lancamentos[0].Id);
    }

    [Fact]
    public async Task Lancamento_ja_compensado_nao_pode_ser_editado()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var lancamento = await cenario.LancarAsync(
            conta.Id, centro.Id, -50m, new DateTime(2025, 3, 10), "Farmácia",
            compensado: true, dataCompensado: new DateTime(2025, 3, 10), dataLancamento: new DateTime(2025, 3, 10));

        Assert.True(lancamento.Compensado);

        await Assert.ThrowsAsync<DominioException>(() => cenario.Lancamentos.AtualizarAsync(
            new RequisicaoLancamento(lancamento.Id, conta.Id, centro.Id, "Farmácia", -70m,
                new DateTime(2025, 3, 10), Recorrencia.Unica, 1, false, null, null)));
    }

    [Fact]
    public async Task Compensar_e_descompensar_ajustam_o_saldo()
    {
        await using var cenario = await CenarioTeste.CriarAsync();
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var lancamento = await cenario.LancarAsync(
            conta.Id, centro.Id, 500m, new DateTime(2025, 3, 12), "Freelance",
            dataLancamento: new DateTime(2025, 3, 12));

        var extratoAntes = await cenario.Extrato.ObterAsync(conta.Id, new DateTime(2025, 3, 1), new DateTime(2025, 3, 31));
        Assert.Equal(0m, extratoAntes.SaldoAnterior);
        Assert.Equal(500m, extratoAntes.SaldoFinal);
        Assert.Equal(0m, extratoAntes.SaldoCompensadoFinal);

        await cenario.Lancamentos.CompensarAsync(lancamento.Id, new DateTime(2025, 3, 13));

        var extratoDepois = await cenario.Extrato.ObterAsync(conta.Id, new DateTime(2025, 3, 1), new DateTime(2025, 3, 31));
        Assert.Equal(500m, extratoDepois.SaldoCompensadoFinal);

        await cenario.Lancamentos.DescompensarAsync(lancamento.Id);

        var extratoFinal = await cenario.Extrato.ObterAsync(conta.Id, new DateTime(2025, 3, 1), new DateTime(2025, 3, 31));
        Assert.Equal(0m, extratoFinal.SaldoCompensadoFinal);
    }
}

public class ExtratoEDashboardTests
{
    [Fact]
    public async Task Extrato_marca_pendente_fora_do_periodo_e_calcula_saldo_anterior()
    {
        await using var cenario = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        await cenario.LancarAsync(conta.Id, centro.Id, 1000m, new DateTime(2025, 1, 10), "Salário",
            compensado: true, dataCompensado: new DateTime(2025, 1, 10), dataLancamento: new DateTime(2025, 1, 10));

        await cenario.LancarAsync(conta.Id, centro.Id, -200m, new DateTime(2025, 2, 5), "Conta de luz",
            compensado: true, dataCompensado: new DateTime(2025, 2, 5), dataLancamento: new DateTime(2025, 2, 5));

        await cenario.LancarAsync(conta.Id, centro.Id, -150m, new DateTime(2025, 2, 20), "Internet",
            dataLancamento: new DateTime(2025, 2, 20));

        await cenario.LancarAsync(conta.Id, centro.Id, 300m, new DateTime(2025, 3, 10), "Venda",
            compensado: true, dataCompensado: new DateTime(2025, 3, 10), dataLancamento: new DateTime(2025, 3, 10));

        var extrato = await cenario.Extrato.ObterAsync(conta.Id, new DateTime(2025, 3, 1), new DateTime(2025, 3, 15));

        Assert.Equal(800m, extrato.SaldoAnterior);
        Assert.Equal(2, extrato.Itens.Count);
        Assert.True(extrato.Itens[0].ForaDoPeriodo);
        Assert.True(extrato.Itens[0].Pendente);
        Assert.Equal(-150m, extrato.Itens[0].Valor);
        Assert.Equal(650m, extrato.Itens[0].SaldoApos);
        Assert.Equal(950m, extrato.SaldoFinal);
    }

    [Fact]
    public async Task Dashboard_agrega_contas_e_ultimos_sete_dias()
    {
        await using var cenario = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await cenario.ConfigurarAdministradorAsync();

        var contaPrincipal = (await cenario.ContaRepositorio.ListarAsync())[0];
        var outras = await cenario.CriarContaAsync("Investimentos");
        var centro = await cenario.CriarCentroAsync();

        await cenario.LancarAsync(contaPrincipal.Id, centro.Id, 2000m, new DateTime(2025, 3, 1), "Salário",
            compensado: true, dataCompensado: new DateTime(2025, 3, 1), dataLancamento: new DateTime(2025, 3, 1));

        await cenario.LancarAsync(outras.Id, centro.Id, -300m, new DateTime(2025, 3, 14), "Aporte",
            dataLancamento: new DateTime(2025, 3, 14));

        var resumo = await cenario.Dashboard.ObterAsync();

        Assert.Equal(2, resumo.Contas.Count);
        Assert.Equal(2000m, resumo.Total.Compensado);
        Assert.Equal(-300m, resumo.Total.NaoCompensado);
        Assert.Equal(1700m, resumo.Total.Total);

        // Janela de 7 dias: apenas o lançamento de 14/03.
        Assert.Single(resumo.UltimosLancamentos);
        Assert.Equal(-300m, resumo.UltimosLancamentos[0].Valor);
    }

    [Fact]
    public async Task Exclusao_de_parcela_futura_preserva_as_anteriores()
    {
        await using var cenario = await CenarioTeste.CriarAsync(new DateTime(2025, 3, 15));
        await cenario.ConfigurarAdministradorAsync();

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync();

        var resultado = await cenario.Lancamentos.CriarAsync(new RequisicaoLancamento(
            0, conta.Id, centro.Id, "Streaming", -30m, new DateTime(2025, 3, 5),
            Recorrencia.Mensal, 4, false, null, null));

        await cenario.Lancamentos.ExcluirAsync(resultado.Lancamentos[1].Id, aPartirDaParcela: true);

        var restantes = await cenario.Lancamentos.ListarSerieAsync(resultado.Lancamentos[0].ChaveDaSerie!);

        Assert.Single(restantes);
        Assert.Equal(1, restantes[0].Parcela);
    }
}
