using zFinancas.Core.Abstracoes;

namespace zFinancas.App.Servicos;

/// <summary>
/// Preferências locais do aparelho. A entrada por biometria fica vinculada a um usuário
/// específico: ao ativar, guardamos o Id; ao desativar, limpamos.
/// </summary>
public sealed class ServicoPreferencias
{
    private const string ChaveBiometriaUsuario = "seguranca.biometria.usuario";
    private const string ChaveUltimoUsuario = "sessao.ultimo.usuario";

    private readonly IConfiguracaoRepositorio _configuracoes;

    public ServicoPreferencias(IConfiguracaoRepositorio configuracoes)
    {
        _configuracoes = configuracoes;
    }

    public async Task<long?> ObterUsuarioComBiometriaAsync()
    {
        var valor = await _configuracoes.ObterAsync(ChaveBiometriaUsuario).ConfigureAwait(false);
        return long.TryParse(valor, out var id) ? id : null;
    }

    public Task HabilitarBiometriaAsync(long usuarioId)
        => _configuracoes.DefinirAsync(ChaveBiometriaUsuario, usuarioId.ToString());

    public Task DesabilitarBiometriaAsync()
        => _configuracoes.DefinirAsync(ChaveBiometriaUsuario, string.Empty);

    public async Task<long?> ObterUltimoUsuarioAsync()
    {
        var valor = await _configuracoes.ObterAsync(ChaveUltimoUsuario).ConfigureAwait(false);
        return long.TryParse(valor, out var id) ? id : null;
    }

    public Task DefinirUltimoUsuarioAsync(long usuarioId)
        => _configuracoes.DefinirAsync(ChaveUltimoUsuario, usuarioId.ToString());
}
