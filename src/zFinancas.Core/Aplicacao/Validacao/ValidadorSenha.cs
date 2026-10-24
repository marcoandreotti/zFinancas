using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.Core.Aplicacao.Validacao;

/// <summary>Regras mínimas de senha é simples por decisão de produto, porém consistentes.</summary>
public static class ValidadorSenha
{
    public const int TamanhoMinimo = 4;
    public const int TamanhoMaximo = 50;

    public static void Validar(string? senha)
    {
        if (string.IsNullOrWhiteSpace(senha))
        {
            throw new DominioException("Informe uma senha.");
        }

        if (senha.Length < TamanhoMinimo)
        {
            throw new DominioException($"A senha deve ter ao menos {TamanhoMinimo} caracteres.");
        }

        if (senha.Length > TamanhoMaximo)
        {
            throw new DominioException($"A senha deve ter no máximo {TamanhoMaximo} caracteres.");
        }
    }

    public static void ValidarConfirmacao(string? senha, string? confirmacao)
    {
        Validar(senha);

        if (!string.Equals(senha, confirmacao, StringComparison.Ordinal))
        {
            throw new DominioException("A confirmação da senha não confere.");
        }
    }

    /// <summary>Força da senha de 0 a 4 (usada apenas como indicador visual).</summary>
    public static int Forca(string? senha)
    {
        if (string.IsNullOrWhiteSpace(senha)) return 0;

        var pontos = 0;
        if (senha.Length >= 8) pontos++;
        if (senha.Length >= 12) pontos++;
        if (senha.Any(char.IsDigit)) pontos++;
        if (senha.Any(c => !char.IsLetterOrDigit(c))) pontos++;

        return Math.Min(4, pontos);
    }
}
