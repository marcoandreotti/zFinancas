namespace zFinancas.Core.Dominio.Entidades;

/// <summary>
/// Centro de custo (tabela CENTRODECUSTO) é agrupador de despesas/receitas.
/// O ícone é um emoji curto, o que evita dependência de bibliotecas de ícones
/// e funciona igual no Windows e no Android.
/// </summary>
public class CentroCusto : EntidadeBase
{
    public const int TamanhoMaximoDescricao = 50;
    public const int TamanhoMaximoIcone = 30;

    public static readonly string[] PaletaIcones =
    [
"🏠", "🍕", "🚗", "💊", "🎓", "🛒", "✈️", "🎮", "💡", "📱", "🐾", "💼",
        "🏦", "💳", "🎁", "⚽", "👶", "🧾", "⛽", "☕", "🍺", "🛠️", "🎵", "💍"
    ];

    public string Descricao { get; set; } = string.Empty;

    public bool Principal { get; set; }

    public string Icone { get; set; } = "🧾";

    public bool Ativo { get; set; } = true;

    public static CentroCusto Novo(string descricao, bool principal, DateTime agoraUtc, string? icone = null)
    {
        var centro = new CentroCusto
        {
            Descricao = (descricao ?? string.Empty).Trim(),
            Principal = principal,
            Ativo = true,
            Icone = string.IsNullOrWhiteSpace(icone) ? "🧾" : icone.Trim(),
            AtualizadoEm = agoraUtc
        };

        centro.Validar();
        return centro;
    }

    public void Validar()
    {
        var erros = new List<string>();

        if (string.IsNullOrWhiteSpace(Descricao))
        {
            erros.Add("A descrição do centro de custo é obrigatória.");
        }
        else if (Descricao.Trim().Length > TamanhoMaximoDescricao)
        {
            erros.Add($"A descrição do centro de custo deve ter no máximo {TamanhoMaximoDescricao} caracteres.");
        }

        if (!string.IsNullOrWhiteSpace(Icone) && Icone.Trim().Length > TamanhoMaximoIcone)
        {
            erros.Add($"O ícone deve ter no máximo {TamanhoMaximoIcone} caracteres.");
        }

        if (erros.Count > 0)
        {
            throw new Excecoes.DominioException(erros);
        }
    }

    public void Alterar(string descricao, string? icone, DateTime agoraUtc)
    {
        Descricao = (descricao ?? string.Empty).Trim();
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
}
