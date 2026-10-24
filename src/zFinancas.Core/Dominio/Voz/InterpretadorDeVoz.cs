using System.Globalization;
using System.Text;
using zFinancas.Core.Dominio.Entidades;

namespace zFinancas.Core.Dominio.Voz;

/// <summary>
/// Rascunho de lançamento extraído de uma frase falada
/// (ex.: “gastei trinta e dois reais no mercado”). A UI usa isso para pré-preencher o
/// formulário — o usuário sempre confirma antes de gravar.
/// </summary>
public sealed record LancamentoFalado(
    bool EhDespesa,
    decimal Valor,
    string Descricao,
    DateTime DataVencimento,
    bool Compensado,
    long? CentroCustoId,
    string? CentroCustoDescricao,
    string TextoOriginal)
{
    /// <summary>Valor já com sinal (negativo = despesa).</summary>
    public decimal ValorComSinal => EhDespesa ? -Math.Abs(Valor) : Math.Abs(Valor);

    public string Resumo =>
        $"{(EhDespesa ? "Despesa" : "Recebimento")} de {Valor.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"))}" +
        (string.IsNullOrWhiteSpace(Descricao) ? string.Empty : $" • {Descricao}") +
        (string.IsNullOrWhiteSpace(CentroCustoDescricao) ? string.Empty : $" • {CentroCustoDescricao}");
}

/// <summary>Resultado da interpretação: rascunho pronto ou mensagem amigável do porquê não deu.</summary>
public sealed record ResultadoInterpretacaoVoz(LancamentoFalado? Lancamento, string Mensagem)
{
    public bool Sucesso => Lancamento is not null;
}

/// <summary>
/// Converte texto falado (pt-BR) em um lançamento.
/// <para>
/// É puro e determinístico: sem microfone, sem plataforma — por isso é testável.
/// Cobre o vocabulário do dia a dia: tipo (gastei/recebi), valor em dígitos ou por extenso,
/// data relativa (hoje/ontem/anteontem), centro de custo por palavras-chave e descrição.
/// </para>
/// </summary>
public sealed class InterpretadorDeVoz
{
    private static readonly string[] PalavrasDespesa =
    [
        "gastei", "gaste", "gasto", "paguei", "pague", "pago", "comprei", "compre", "compra",
        "despesa", "almocei", "jantei", "abasteci", "saiu", "debito"
    ];

    private static readonly string[] PalavrasRecebimento =
    [
        "recebi", "recebe", "recebimento", "ganhei", "ganho", "entrou", "entrou", "receita",
        "vendi", "venda", "salario", "caiu", "deposito", "credito", "reembolso"
    ];

    private static readonly string[] PalavrasPendente =
    [
        "vou pagar", "vou receber", "vence", "vencimento", "agendar", "agendado", "pendente", "a pagar", "a receber"
    ];

    private static readonly HashSet<string> Ruido =
    [
        "o", "a", "os", "as", "um", "uma", "uns", "umas", "de", "do", "da", "dos", "das",
        "no", "na", "nos", "nas", "em", "por", "para", "pra", "com", "e", "que", "meu", "minha",

        // moeda (inclui grafias que o reconhecedor costuma devolver: "reias", "mil réis")
        "reais", "real", "reias", "reias", "conto", "contos", "pila", "pilas", "mil", "milhao",
        "milhoes", "brl", "rs", "r",

        // muletas de fala
        "ai", "ah", "entao", "bem", "so", "tipo", "meio", "cerca", "mais", "menos",
        "hoje", "ontem", "anteontem", "amanha", "agora"
    ];

    /// <summary>Palavras que ajudam a achar o centro de custo quando o nome não é dito.</summary>
    private static readonly (string Chave, string[] Termos)[] PistasCentro =
    [
        ("alimenta", ["mercado", "supermercado", "padaria", "restaurante", "pizza", "almoco", "janta", "jantar", "lanche", "cafe", "feira", "acougue", "hamburguer", "delivery"]),
        ("transporte", ["gasolina", "combustivel", "posto", "uber", "onibus", "metro", "taxi", "estacionamento", "pedagio", "ipva", "passagem"]),
        ("saude", ["farmacia", "remedio", "medico", "dentista", "exame", "consulta", "hospital", "plano de saude"]),
        ("moradia", ["aluguel", "condominio", "luz", "agua", "internet", "energia", "faxina", "gas", "iptu"]),
        ("lazer", ["cinema", "bar", "jogo", "viagem", "netflix", "spotify", "streaming", "show", "festa", "cerveja"]),
        ("educacao", ["curso", "escola", "faculdade", "livro", "mensalidade", "apostila"]),
        ("vestuario", ["roupa", "tenis", "sapato", "camisa", "calca"]),
        ("pets", ["racao", "veterinario", "pet", "banho e tosa"])
    ];

    private static readonly Dictionary<string, decimal> NumerosPorExtenso = new(StringComparer.Ordinal)
    {
        ["zero"] = 0, ["um"] = 1, ["uma"] = 1, ["dois"] = 2, ["duas"] = 2, ["tres"] = 3,
        ["quatro"] = 4, ["cinco"] = 5, ["seis"] = 6, ["sete"] = 7, ["oito"] = 8, ["nove"] = 9,
        ["dez"] = 10, ["onze"] = 11, ["doze"] = 12, ["treze"] = 13, ["quatorze"] = 14, ["catorze"] = 14,
        ["quinze"] = 15, ["dezesseis"] = 16, ["dezessete"] = 17, ["dezoito"] = 18, ["dezenove"] = 19,
        ["vinte"] = 20, ["trinta"] = 30, ["quarenta"] = 40, ["cinquenta"] = 50,
        ["sessenta"] = 60, ["setenta"] = 70, ["oitenta"] = 80, ["noventa"] = 90,
        ["cem"] = 100, ["cento"] = 100, ["duzentos"] = 200, ["trezentos"] = 300,
        ["quatrocentos"] = 400, ["quinhentos"] = 500, ["seiscentos"] = 600,
        ["setecentos"] = 700, ["oitocentos"] = 800, ["novecentos"] = 900
    };

    private readonly CultureInfo _ptBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Interpreta a frase. <paramref name="centros"/> e <paramref name="contas"/> orientam o
    /// reconhecimento de nomes já cadastrados (ex.: a pessoa fala “lazer” e existe “Lazer”).
    /// </summary>
    public ResultadoInterpretacaoVoz Interpretar(
        string? texto,
        IReadOnlyList<CentroCusto> centros,
        IReadOnlyList<ContaCorrente> contas,
        DateTime hoje)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return new ResultadoInterpretacaoVoz(null, "Não ouvi nada. Toque no microfone e fale o lançamento.");
        }

        var original = texto.Trim();
        var normalizado = Normalizar(original);
        var tokens = normalizado.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (tokens.Count == 0)
        {
            return new ResultadoInterpretacaoVoz(null, "Não consegui entender a frase. Tente: “gastei 30 no mercado”.");
        }

        // ------------------------------------------------------------- tipo
        var ehRecebimento = tokens.Any(t => PalavrasRecebimento.Contains(t));
        var ehDespesa = tokens.Any(t => PalavrasDespesa.Contains(t)) || !ehRecebimento;

        // ------------------------------------------------------------ valor
        var valor = ExtrairValor(original, tokens, out var tokensDoValor);
        if (valor <= 0m)
        {
            return new ResultadoInterpretacaoVoz(
                null,
                "Não identifiquei o valor. Diga algo como “gastei 45 e 90 no mercado” ou “recebi 1.200 de freelance”.");
        }

        // ------------------------------------------------------------- data
        var data = hoje.Date;
        if (tokens.Contains("anteontem")) data = data.AddDays(-2);
        else if (tokens.Contains("ontem")) data = data.AddDays(-1);
        else if (tokens.Contains("amanha")) data = data.AddDays(1);

        // ------------------------------------------------------ compensação
        var pendente = PalavrasPendente.Any(p => normalizado.Contains(p, StringComparison.Ordinal));
        var compensado = !pendente;

        // -------------------------------------------- centro de custo e conta
        var centro = AcharCentro(normalizado, centros);
        var conta = AcharConta(normalizado, contas);

        // -------------------------------------------------------- descrição
        var descricao = MontarDescricao(tokens, tokensDoValor, centro);

        var lancamento = new LancamentoFalado(
            ehDespesa && !ehRecebimento,
            decimal.Round(valor, 2),
            descricao,
            data,
            compensado,
            centro?.Id,
            centro?.Descricao,
            original);

        var mensagem = lancamento.Resumo +
                       (conta is null ? string.Empty : $" • {conta.Descricao}") +
                       (compensado ? string.Empty : " • pendente");

        return new ResultadoInterpretacaoVoz(lancamento, mensagem);
    }

    // ------------------------------------------------------------------ valor

    private decimal ExtrairValor(string original, List<string> tokens, out HashSet<string> tokensConsumidos)
    {
        tokensConsumidos = [];

        // 0) "2 mil" / "mil e duzentos" — multiplicador falado
        var comMil = System.Text.RegularExpressions.Regex.Match(original, @"(\d[\d\.\,]*)\s*mil", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (comMil.Success && TentarLerNumero(comMil.Groups[1].Value, out var baseMil) && baseMil > 0)
        {
            tokensConsumidos.Add(comMil.Groups[1].Value.Replace(",", string.Empty).Replace(".", string.Empty));
            tokensConsumidos.Add("mil");
            return baseMil * 1000m;
        }

        // 1) dígitos (aceita 32,50 / 1.234,56 / 1200 / 32.50)
        var candidatos = System.Text.RegularExpressions.Regex.Matches(original, @"\d[\d\.\,]*");
        foreach (System.Text.RegularExpressions.Match candidato in candidatos)
        {
            if (TentarLerNumero(candidato.Value, out var numero) && numero > 0)
            {
                tokensConsumidos.Add(candidato.Value.Replace(",", string.Empty).Replace(".", string.Empty));
                return numero;
            }
        }

        // 2) por extenso, somando na ordem falada ("vinte e cinco", "cento e vinte")
        decimal total = 0m;
        var achou = false;

        foreach (var token in tokens)
        {
            if (token == "mil")
            {
                total = total == 0m ? 1000m : total * 1000m;
                achou = true;
                tokensConsumidos.Add(token);
                continue;
            }

            if (NumerosPorExtenso.TryGetValue(token, out var parte))
            {
                total += parte;
                achou = true;
                tokensConsumidos.Add(token);
            }
        }

        return achou ? total : 0m;
    }

    private bool TentarLerNumero(string bruto, out decimal valor)
    {
        valor = 0m;
        var texto = bruto.Trim().TrimEnd('.', ',');

        // 32,50 e 1.234,56 (padrão pt-BR)
        if (decimal.TryParse(texto, NumberStyles.Number, _ptBr, out valor) && valor > 0)
        {
            return true;
        }

        // 32.50 (ponto decimal) — só quando não parece milhar
        var temVirgula = texto.Contains(',');
        var pontos = texto.Count(c => c == '.');

        if (!temVirgula && pontos == 1 && texto.Split('.')[1].Length is 1 or 2)
        {
            if (decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out valor) && valor > 0)
            {
                return true;
            }
        }

        // 1.200 / 1200
        var apenasDigitos = texto.Replace(".", string.Empty).Replace(",", string.Empty);
        if (!temVirgula && long.TryParse(apenasDigitos, NumberStyles.Integer, CultureInfo.InvariantCulture, out var inteiro))
        {
            valor = inteiro;
            return valor > 0;
        }

        return false;
    }

    // ------------------------------------------------------ centro e conta

    private static CentroCusto? AcharCentro(string normalizado, IReadOnlyList<CentroCusto> centros)
    {
        // nome cadastrado dito na frase
        foreach (var centro in centros)
        {
            var nome = Normalizar(centro.Descricao);
            if (nome.Length >= 3 && normalizado.Contains(nome, StringComparison.Ordinal))
            {
                return centro;
            }
        }

        // palavra-chave do dia a dia → centro cujo nome contenha a pista
        foreach (var (chave, termos) in PistasCentro)
        {
            if (!termos.Any(t => normalizado.Contains(t, StringComparison.Ordinal)))
            {
                continue;
            }

            var porPista = centros.FirstOrDefault(c => Normalizar(c.Descricao).Contains(chave, StringComparison.Ordinal));
            if (porPista is not null)
            {
                return porPista;
            }
        }

        return null;
    }

    private static ContaCorrente? AcharConta(string normalizado, IReadOnlyList<ContaCorrente> contas)
    {
        foreach (var conta in contas)
        {
            var nome = Normalizar(conta.Descricao);
            if (nome.Length >= 3 && normalizado.Contains(nome, StringComparison.Ordinal))
            {
                return conta;
            }
        }

        return contas.FirstOrDefault(c => c.Principal);
    }

    // ------------------------------------------------------------ descrição

    private static string MontarDescricao(List<string> tokens, HashSet<string> tokensDoValor, CentroCusto? centro)
    {
        var restantes = tokens
            .Where(t => !tokensDoValor.Contains(t))
            .Where(t => t.Length > 1)   // restos como "r" de "R$" ou letra solta
            .Where(t => !Ruido.Contains(t))
            .Where(t => !PalavrasDespesa.Contains(t) && !PalavrasRecebimento.Contains(t))
            .Where(t => !NumerosPorExtenso.ContainsKey(t) && t != "mil" && t != "que")
            .Where(t => !t.All(char.IsDigit))
            .ToList();

        if (restantes.Count == 0)
        {
            return centro?.Descricao ?? "Lançamento por voz";
        }

        var descricao = string.Join(' ', restantes);
        descricao = char.ToUpperInvariant(descricao[0]) + descricao[1..];

        return descricao.Length <= Lancamento.TamanhoMaximoDescricao
            ? descricao
            : descricao[..Lancamento.TamanhoMaximoDescricao].TrimEnd();
    }

    // ------------------------------------------------------------- utilidades

    /// <summary>Minúsculas, sem acentos e sem pontuação — para casar palavras com segurança.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var decomposto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var construtor = new StringBuilder(decomposto.Length);

        foreach (var caractere in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            construtor.Append(char.IsLetterOrDigit(caractere) ? caractere : ' ');
        }

        return System.Text.RegularExpressions.Regex
            .Replace(construtor.ToString(), @"\s+", " ")
            .Trim();
    }
}
