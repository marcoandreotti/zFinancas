using System.Security.Cryptography;

namespace zFinancas.Core.Seguranca;

/// <summary>
/// Gera códigos de recuperação de senha legíveis por humanos.
/// O código é mostrado uma única vez (cadastro/reset) e guardamos apenas o hash.
/// </summary>
public static class GeradorCodigoRecuperacao
{
    // Sem caracteres ambíguos (0/O, 1/I/L) para facilitar a digitação.
    private const string Alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int Blocos = 3;
    private const int TamanhoBloco = 4;

    /// <summary>Cria um código no formato <c>ABCD-EFGH-JKLM</c>.</summary>
    public static string Gerar()
    {
        var bloco = new char[TamanhoBloco];
        var partes = new string[Blocos];

        for (var b = 0; b < Blocos; b++)
        {
            for (var i = 0; i < TamanhoBloco; i++)
            {
                bloco[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
            }

            partes[b] = new string(bloco);
        }

        return string.Join('-', partes);
    }

    /// <summary>Normaliza a entrada do usuário (remove espaços/hífens, maiúsculas).</summary>
    public static string Normalizar(string? codigo)
        => new string((codigo ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
}
