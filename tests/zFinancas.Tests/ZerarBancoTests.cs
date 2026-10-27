using zFinancas.Data.Infra;
using zFinancas.Tests.Apoio;

namespace zFinancas.Tests;

/// <summary>
/// "Começar do zero": apaga o banco, recria o esquema vazio e deixa uma cópia de segurança
/// do estado anterior na pasta do banco.
/// </summary>
public class ZerarBancoTests
{
    [Fact]
    public async Task Zerar_apaga_os_dados_e_deixa_copia_de_seguranca()
    {
        await using var cenario = await CenarioTeste.CriarAsync(new DateTime(2025, 5, 20));
        await cenario.ConfigurarAdministradorAsync("Marco", "senha123");

        var conta = (await cenario.ContaRepositorio.ListarAsync())[0];
        var centro = await cenario.CriarCentroAsync("Mercado");

        await cenario.LancarAsync(conta.Id, centro.Id, -80m, new DateTime(2025, 5, 19), "Vai sumir",
            dataLancamento: new DateTime(2025, 5, 19));

        Assert.Single(await cenario.UsuarioRepositorio.ListarAsync());
        Assert.Single(await cenario.LancamentoRepositorio.ListarPorContaAsync(conta.Id));

        var servico = new ServicoBackupBanco(cenario.Contexto);
        var seguranca = await servico.ZerarAsync();

        // A cópia de segurança guarda o estado anterior.
        Assert.True(File.Exists(seguranca), "a cópia de segurança deve existir na pasta do banco");

        var conteudo = await File.ReadAllBytesAsync(seguranca);
        Assert.True(conteudo.Length > 0);

        // O app volta a ser uma base vazia (esquema recriado, sem usuários).
        Assert.Empty(await cenario.UsuarioRepositorio.ListarAsync());
        Assert.Empty(await cenario.ContaRepositorio.ListarAsync());

        // E dá para seguir usando: o primeiro acesso funciona normalmente.
        await cenario.ConfigurarAdministradorAsync("Novo", "senha456");
        Assert.Single(await cenario.UsuarioRepositorio.ListarAsync());

        // Uma segunda zerada também funciona (o arquivo novo é apagado corretamente).
        var segunda = await servico.ZerarAsync();
        Assert.True(File.Exists(segunda));
        Assert.Empty(await cenario.UsuarioRepositorio.ListarAsync());
    }

    [Fact]
    public async Task Copia_de_seguranca_da_zerada_pode_ser_restaurada()
    {
        await using var cenario = await CenarioTeste.CriarAsync(new DateTime(2025, 5, 21));
        await cenario.ConfigurarAdministradorAsync("Marco", "senha123");

        var servico = new ServicoBackupBanco(cenario.Contexto);
        var seguranca = await servico.ZerarAsync();

        Assert.Empty(await cenario.UsuarioRepositorio.ListarAsync());

        var conteudo = await File.ReadAllBytesAsync(seguranca);
        await servico.RestaurarAsync(conteudo);

        var usuarios = await cenario.UsuarioRepositorio.ListarAsync();
        Assert.Single(usuarios);
        Assert.Equal("Marco", usuarios[0].Nome);
    }
}
