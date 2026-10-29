namespace zFinancas.Core.Dominio.Enums;

/// <summary>Perfil de acesso do usuário (coluna PERFIL, char(3)).</summary>
public enum PerfilUsuario
{
    /// <summary>ADM — administra usuários, contas, centros de custo e vê todas as contas.</summary>
    Administrador = 0,

    /// <summary>USR — opera apenas as contas vinculadas em USUARIOCONTACORRENTE.</summary>
    Usuario = 1
}

public static class PerfilUsuarioExtensoes
{
    public const string CodigoAdministrador = "ADM";
    public const string CodigoUsuario = "USR";

    public static string ParaCodigo(this PerfilUsuario perfil)
        => perfil == PerfilUsuario.Administrador ? CodigoAdministrador : CodigoUsuario;

    public static PerfilUsuario DeCodigo(string? codigo)
        => (codigo ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            CodigoAdministrador => PerfilUsuario.Administrador,
            CodigoUsuario => PerfilUsuario.Usuario,
            _ => throw new Excecoes.DominioException($"Perfil inválido: '{codigo}'. Utilize ADM ou USR.")
        };

    public static string Descrever(this PerfilUsuario perfil)
        => perfil == PerfilUsuario.Administrador ? "Administrador" : "Usuário";

    public static bool EhAdministrador(this PerfilUsuario perfil) => perfil == PerfilUsuario.Administrador;
}
