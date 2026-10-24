using System.Globalization;

namespace zFinancas.App.Servicos;

/// <summary>
/// Máscara de moeda no padrão pt-BR (<c>1.234,56</c>), aplicada ao campo de valor.
/// <para>
/// Os dígitos digitados entram como <b>centavos</b>: digitar 3 2 5 0 vira 32,50 e
/// 1 5 0 0 0 0 vira 1.500,00. Assim não existe ambiguidade entre vírgula e ponto —
/// independente do teclado/idioma do aparelho.
/// </para>
/// </summary>
public static class MascaraMoeda
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Aplica a máscara sobre o texto digitado.</summary>
    public static string Aplicar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }

        var digitos = new string(texto.Where(char.IsDigit).ToArray());

        if (digitos.Length == 0)
        {
            return string.Empty;
        }

        if (digitos.Length > 13)
        {
            digitos = digitos[..13];
        }

        return decimal.TryParse(digitos, NumberStyles.None, CultureInfo.InvariantCulture, out var centavos)
            ? Formatar(centavos / 100m)
            : string.Empty;
    }

    /// <summary>Formata um valor já calculado (ex.: ao abrir um lançamento existente).</summary>
    public static string Formatar(decimal valor) => Math.Abs(valor).ToString("N2", PtBr);
}
