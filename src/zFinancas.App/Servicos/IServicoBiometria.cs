namespace zFinancas.App.Servicos;

/// <summary>Como terminou uma tentativa de autenticação biométrica.</summary>
public enum ResultadoBiometria
{
    /// <summary>Biometria reconhecida.</summary>
    Sucesso,

    /// <summary>O usuário fechou o prompt (botão do diálogo ou “voltar”).</summary>
    Cancelado,

    /// <summary>Biometria não reconhecida ou bloqueada por tentativas.</summary>
    Falhou,

    /// <summary>Aparelho sem biometria utilizável (ou plataforma sem suporte).</summary>
    Indisponivel
}

/// <summary>
/// Autenticação biométrica (digital/rosto). Só faz sentido no celular; no desktop
/// retorna indisponível para a UI esconder a opção.
/// </summary>
public interface IServicoBiometria
{
    /// <summary>Verdadeiro quando o aparelho tem biometria cadastrada e utilizável.</summary>
    Task<bool> DisponivelAsync();

    /// <summary>Abre o prompt do sistema e informa como a tentativa terminou.</summary>
    Task<ResultadoBiometria> AutenticarAsync(string motivo);
}

/// <summary>Implementação para plataformas sem suporte (desktop).</summary>
public sealed class ServicoBiometriaIndisponivel : IServicoBiometria
{
    public Task<bool> DisponivelAsync() => Task.FromResult(false);

    public Task<ResultadoBiometria> AutenticarAsync(string motivo) =>
        Task.FromResult(ResultadoBiometria.Indisponivel);
}
