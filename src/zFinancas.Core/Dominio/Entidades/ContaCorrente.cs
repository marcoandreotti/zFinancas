namespace zFinancas.Core.Dominio.Entidades;

/// <summary>
/// Conta corrente / carteira (tabela CONTACORRENTE).
/// A conta principal é a selecionada por padrão na tela de lançamento.
/// </summary>
public class ContaCorrente : EntidadeBase
{
    public const int TamanhoMaximoDescricao = 50;

    /// <summary>Paleta usada nos cartões do dashboard e nos seletores.</summary>
    public static readonly string[] PaletaCores =
    [
        "#6C5BFF", "#22D3A6", "#F5A524", "#F2545B", "#38BDF8", "#A855F7", "#F97316", "#14B8A6"
    ];

    public string Descricao { get; set; } = string.Empty;

    public bool Principal { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>Cor do cartão no dashboard (hex #RRGGBB).</summary>
    public string CorHex { get; set; } = PaletaCores[0];

    /// <summary>ícone exibido junto é conta (emoji, sem dependência de assets).</summary>
    public string Icone { get; set; } = "🏦";

    /// <summary>Ordem de exibição no carrossel.</summary>
    public int Ordem { get; set; }

    public static ContaCorrente Nova(string descricao, bool principal, DateTime agoraUtc, string? corHex = null, string? icone = null)
    {
        var conta = new ContaCorrente
        {
            Descricao = (descricao ?? string.Empty).Trim(),
            Principal = principal,
            Ativo = true,
            CorHex = corHex ?? PaletaCores[0],
            Icone = string.IsNullOrWhiteSpace(icone) ? "🏦" : icone.Trim(),
            AtualizadoEm = agoraUtc
        };

        conta.Validar();
        return conta;
    }

    public void Validar()
    {
        var erros = new List<string>();

        if (string.IsNullOrWhiteSpace(Descricao))
        {
            erros.Add("A descrição da conta é obrigatória.");
        }
        else if (Descricao.Trim().Length > TamanhoMaximoDescricao)
        {
            erros.Add($"A descrição da conta deve ter no máximo {TamanhoMaximoDescricao} caracteres.");
        }

        if (!string.IsNullOrWhiteSpace(CorHex) && !EhCorHexadecimal(CorHex))
        {
            erros.Add("A cor da conta deve estar no formato #RRGGBB.");
        }

        if (erros.Count > 0)
        {
            throw new Excecoes.DominioException(erros);
        }
    }

    public void Alterar(string descricao, string? corHex, string? icone, DateTime agoraUtc)
    {
        Descricao = (descricao ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(corHex)) CorHex = corHex.Trim();
        if (!string.IsNullOrWhiteSpace(icone)) Icone = icone.Trim();
        MarcarAlteracao(agoraUtc);
        Validar();
    }

    public void DefinirPrincipal(bool principal, DateTime agoraUtc)
    {
        Principal = principal;
        MarcarAlteracao(agoraUtc);
    }

    public void AlterarAtivo(bool ativo, DateTime agoraUtc)
    {
        Ativo = ativo;
        if (!ativo) Principal = false;
        MarcarAlteracao(agoraUtc);
    }

    public static bool EhCorHexadecimal(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Length != 7 || valor[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < valor.Length; i++)
        {
            if (!Uri.IsHexDigit(valor[i])) return false;
        }

        return true;
    }
}
