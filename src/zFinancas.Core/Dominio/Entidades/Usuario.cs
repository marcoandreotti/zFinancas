using zFinancas.Core.Dominio.Enums;

namespace zFinancas.Core.Dominio.Entidades;

/// <summary>
/// Usuário do sistema (tabela USUARIO).
/// A senha nunca é armazenada em texto claro: guardamos o hash PBKDF2 e,
/// separadamente, o hash de um código de recuperação gerado no cadastro.
/// </summary>
public class Usuario : EntidadeBase
{
    public const int TamanhoMaximoNome = 30;
    public const int TamanhoMinimoSenha = 4;

    public string Nome { get; set; } = string.Empty;

    /// <summary>Hash PBKDF2 é ver <c>SenhaHasher</c>.</summary>
    public string SenhaHash { get; set; } = string.Empty;

    /// <summary>Dica opcional exibida na tela de login (nunca revela a senha).</summary>
    public string? DicaSenha { get; set; }

    /// <summary>Hash do código de recuperação entregue ao usuário no cadastro.</summary>
    public string? CodigoRecuperacaoHash { get; set; }

    public PerfilUsuario Perfil { get; set; } = PerfilUsuario.Usuario;

    /// <summary>Usuário inativo não acessa o sistema nem as contas relacionadas.</summary>
    public bool Ativo { get; set; } = true;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? UltimoAcessoEm { get; set; }

    public bool EhAdministrador => Perfil == PerfilUsuario.Administrador;

    public static Usuario Novo(string nome, string senhaHash, PerfilUsuario perfil, DateTime agoraUtc)
    {
        var usuario = new Usuario
        {
            Nome = (nome ?? string.Empty).Trim(),
            SenhaHash = senhaHash,
            Perfil = perfil,
            Ativo = true,
            DataCadastro = agoraUtc,
            AtualizadoEm = agoraUtc
        };

        usuario.Validar();
        return usuario;
    }

    /// <summary>Valida invariantes do usuário, lançando <see cref="Excecoes.DominioException"/>.</summary>
    public void Validar()
    {
        var erros = new List<string>();

        if (string.IsNullOrWhiteSpace(Nome))
        {
            erros.Add("O nome do usuário é obrigatório.");
        }
        else if (Nome.Trim().Length > TamanhoMaximoNome)
        {
            erros.Add($"O nome do usuário deve ter no máximo {TamanhoMaximoNome} caracteres.");
        }

        if (string.IsNullOrWhiteSpace(SenhaHash))
        {
            erros.Add("A senha do usuário é obrigatória.");
        }

        if (!Enum.IsDefined(Perfil))
        {
            erros.Add("Perfil do usuário inválido.");
        }

        if (erros.Count > 0)
        {
            throw new Excecoes.DominioException(erros);
        }
    }

    public void RegistrarAcesso(DateTime agoraUtc)
    {
        UltimoAcessoEm = agoraUtc;
        MarcarAlteracao(agoraUtc);
    }

    public void DefinirSenha(string senhaHash, DateTime agoraUtc)
    {
        if (string.IsNullOrWhiteSpace(senhaHash))
        {
            throw new Excecoes.DominioException("Hash de senha inválido.");
        }

        SenhaHash = senhaHash;
        MarcarAlteracao(agoraUtc);
    }

    public void DefinirCodigoRecuperacao(string codigoHash, DateTime agoraUtc)
    {
        CodigoRecuperacaoHash = codigoHash;
        MarcarAlteracao(agoraUtc);
    }

    public void DefinirDicaSenha(string? dica, DateTime agoraUtc)
    {
        DicaSenha = string.IsNullOrWhiteSpace(dica) ? null : dica.Trim();
        MarcarAlteracao(agoraUtc);
    }

    public void AlterarAtivo(bool ativo, DateTime agoraUtc)
    {
        Ativo = ativo;
        MarcarAlteracao(agoraUtc);
    }

    public void AlterarPerfil(PerfilUsuario perfil, DateTime agoraUtc)
    {
        Perfil = perfil;
        MarcarAlteracao(agoraUtc);
    }
}
