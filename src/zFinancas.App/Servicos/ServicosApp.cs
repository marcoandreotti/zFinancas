using Microsoft.Extensions.DependencyInjection;

namespace zFinancas.App.Servicos;

/// <summary>
/// Acesso ao container de DI a partir de páginas criadas pelo Shell.
/// <para>
/// O Shell instancia páginas por reflexão (construtor sem parâmetros), então elas
/// obtém seus view models por aqui. é o único ponto de localização de serviços do app;
/// o resto da aplicação usa injeção de dependência normal.
/// </para>
/// </summary>
public static class ServicosApp
{
    private static IServiceProvider? _provedor;

    public static void Inicializar(IServiceProvider provedor)
    {
        _provedor = provedor;
    }

    public static T Obter<T>() where T : notnull
    {
        if (_provedor is null)
        {
            throw new InvalidOperationException(
                "O container de serviços ainda não foi inicializado. Chame ServicosApp.Inicializar no MauiProgram.");
        }

        return _provedor.GetRequiredService<T>();
    }
}
