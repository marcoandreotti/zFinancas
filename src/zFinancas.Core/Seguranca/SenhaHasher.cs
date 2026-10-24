using System.Security.Cryptography;
using System.Text;

namespace zFinancas.Core.Seguranca;

/// <summary>
/// Hash de senha com PBKDF2-SHA256 (salt aleatório por senha).
/// <para>
/// O requisito é "criptografia simples, mas com possibilidade de recuperação":
/// por isso a senha é apenas hash (irreversível), e a recuperação acontece por um
/// código de recuperação de uso único gerado no cadastro é não guardamos a senha em claro.
/// </para>
/// Formato persistido: <c>PBKDF2$iteracoes$saltBase64$hashBase64</c>.
/// </summary>
public sealed class SenhaHasher
{
    private const string Prefixo = "PBKDF2";
    private const int IteracoesPadrao = 100_000;
    private const int TamanhoSalt = 16;
    private const int TamanhoHash = 32;

    private readonly int _iteracoes;

    public SenhaHasher(int iteracoes = IteracoesPadrao)
    {
        // Piso de segurança: nunca aceitar uma configuração que enfraqueça o hash.
        _iteracoes = Math.Max(10_000, iteracoes);
    }

    public string Hash(string senha)
    {
        ArgumentNullException.ThrowIfNull(senha);

        var salt = RandomNumberGenerator.GetBytes(TamanhoSalt);
        var hash = Derivar(senha, salt, _iteracoes);

        return string.Join('$',
            Prefixo,
            _iteracoes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verificar(string senha, string hashArmazenado)
    {
        if (string.IsNullOrEmpty(senha) || string.IsNullOrWhiteSpace(hashArmazenado))
        {
            return false;
        }

        var partes = hashArmazenado.Split('$');
        if (partes.Length != 4 || partes[0] != Prefixo)
        {
            return false;
        }

        if (!int.TryParse(partes[1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var iteracoes) || iteracoes <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var atual = Derivar(senha, salt, iteracoes);

            // Comparação em tempo constante para não vazar informação por timing.
            return CryptographicOperations.FixedTimeEquals(esperado, atual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Indica se o hash está no formato atual e se deve ser reescrito no próximo login.</summary>
    public bool PrecisaRehash(string hashArmazenado)
    {
        if (string.IsNullOrWhiteSpace(hashArmazenado)) return true;

        var partes = hashArmazenado.Split('$');
        return partes.Length != 4
               || partes[0] != Prefixo
               || !int.TryParse(partes[1], out var iteracoes)
               || iteracoes < _iteracoes;
    }

    private static byte[] Derivar(string senha, byte[] salt, int iteracoes)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(senha),
            salt,
            iteracoes,
            HashAlgorithmName.SHA256,
            TamanhoHash);
}
