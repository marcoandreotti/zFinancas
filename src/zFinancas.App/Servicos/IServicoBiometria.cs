namespace zFinancas.App.Servicos;

/// <summary>
/// Autenticação biométrica (digital/rosto). Só faz sentido no celular; no desktop
/// retorna indisponível para a UI esconder a opção.
/// </summary>
public interface IServicoBiometria
{
    /// <summary>Verdadeiro quando o aparelho tem biometria cadastrada e utilizável.</summary>
    Task<bool> DisponivelAsync();

    /// <summary>Abre o prompt do sistema. Retorna verdadeiro se o usuário se autenticou.</summary>
    Task<bool> AutenticarAsync(string motivo);
}

/// <summary>Implementação para plataformas sem suporte (desktop).</summary>
public sealed class ServicoBiometriaIndisponivel : IServicoBiometria
{
    public Task<bool> DisponivelAsync() => Task.FromResult(false);

    public Task<bool> AutenticarAsync(string motivo) => Task.FromResult(false);
}
