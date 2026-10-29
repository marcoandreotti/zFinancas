using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Voz;

namespace zFinancas.Tests;

/// <summary>
/// O interpretador de voz é puro: recebe texto e devolve um rascunho de lançamento.
/// Testar aqui significa que a parte "difícil" da voz (entender a frase) não depende
/// de microfone nem de aparelho.
/// </summary>
public class InterpretadorDeVozTests
{
    private static readonly DateTime Hoje = new(2026, 10, 23);

    private readonly InterpretadorDeVoz _interpretador = new();

    private static List<CentroCusto> Centros() =>
    [
        CentroCusto.Novo("Geral", true, DateTime.UtcNow, "🧾"),
        CentroCusto.Novo("Alimentação", false, DateTime.UtcNow, "🍕"),
        CentroCusto.Novo("Transporte", false, DateTime.UtcNow, "🚗"),
        CentroCusto.Novo("Moradia", false, DateTime.UtcNow, "🏠"),
        CentroCusto.Novo("Saúde", false, DateTime.UtcNow, "💊"),
        CentroCusto.Novo("Lazer", false, DateTime.UtcNow, "🎮")
    ];

    private static List<ContaCorrente> Contas() =>
    [
        ContaCorrente.Nova("Conta Principal", true, DateTime.UtcNow, "#6C5BFF", "🏦"),
        ContaCorrente.Nova("Poupança", false, DateTime.UtcNow, "#2ED3A0", "🐖")
    ];

    private ResultadoInterpretacaoVoz Interpretar(string frase)
        => _interpretador.Interpretar(frase, Centros(), Contas(), Hoje);

    [Fact]
    public void Despesa_simples_com_valor_decimal_e_centro_por_palavra_chave()
    {
        var resultado = Interpretar("gastei 32,50 no mercado");

        Assert.True(resultado.Sucesso);
        var lancamento = resultado.Lancamento!;

        Assert.True(lancamento.EhDespesa);
        Assert.Equal(-32.50m, lancamento.ValorComSinal);
        Assert.Equal("Mercado", lancamento.Descricao);
        Assert.Equal("Alimentação", lancamento.CentroCustoDescricao);
        Assert.True(lancamento.Compensado);
        Assert.Equal(Hoje, lancamento.DataVencimento);
    }

    [Fact]
    public void Recebimento_com_valor_inteiro()
    {
        var resultado = Interpretar("recebi 1200 de freelance");

        Assert.True(resultado.Sucesso);
        var lancamento = resultado.Lancamento!;

        Assert.False(lancamento.EhDespesa);
        Assert.Equal(1200m, lancamento.ValorComSinal);
        Assert.Equal("Freelance", lancamento.Descricao);
        Assert.True(lancamento.Compensado);
    }

    [Fact]
    public void Valor_por_extenso_e_data_de_ontem()
    {
        var resultado = Interpretar("paguei cinquenta reais de aluguel ontem");

        Assert.True(resultado.Sucesso);
        var lancamento = resultado.Lancamento!;

        Assert.Equal(-50m, lancamento.ValorComSinal);
        Assert.Equal("Aluguel", lancamento.Descricao);
        Assert.Equal("Moradia", lancamento.CentroCustoDescricao);
        Assert.Equal(Hoje.AddDays(-1), lancamento.DataVencimento);
    }

    [Fact]
    public void Valor_composto_por_extenso()
    {
        var resultado = Interpretar("gastei cento e vinte e cinco na farmácia");

        Assert.True(resultado.Sucesso);
        Assert.Equal(-125m, resultado.Lancamento!.ValorComSinal);
        Assert.Equal("Saúde", resultado.Lancamento.CentroCustoDescricao);
    }

    [Fact]
    public void Frase_com_mil_multiplica_o_valor()
    {
        var resultado = Interpretar("recebi 2 mil de bônus");

        Assert.True(resultado.Sucesso);
        Assert.Equal(2000m, resultado.Lancamento!.ValorComSinal);
    }

    [Fact]
    public void Frase_no_futuro_gera_lancamento_pendente()
    {
        var resultado = Interpretar("vou pagar 300 de condomínio");

        Assert.True(resultado.Sucesso);
        var lancamento = resultado.Lancamento!;

        Assert.False(lancamento.Compensado);
        Assert.Equal(-300m, lancamento.ValorComSinal);
        Assert.Equal("Moradia", lancamento.CentroCustoDescricao);
    }

    [Fact]
    public void Centro_de_custo_dito_pelo_nome_tem_prioridade()
    {
        var resultado = Interpretar("gastei 40 no lazer");

        Assert.True(resultado.Sucesso);
        Assert.Equal("Lazer", resultado.Lancamento!.CentroCustoDescricao);
        Assert.Equal("Lazer", resultado.Lancamento.Descricao);
    }

    [Fact]
    public void Conta_mencionada_e_reconhecida()
    {
        var centros = Centros();
        var contas = Contas();

        var resultado = _interpretador.Interpretar("gastei 90 na poupança", centros, contas, Hoje);

        Assert.True(resultado.Sucesso);
        Assert.Contains("Poupança", resultado.Mensagem);
    }

    [Fact]
    public void Frase_sem_valor_falha_com_mensagem_amigavel()
    {
        var resultado = Interpretar("gastei no mercado");

        Assert.False(resultado.Sucesso);
        Assert.Contains("valor", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Texto_vazio_falha_sem_estourar()
    {
        var resultado = Interpretar("   ");

        Assert.False(resultado.Sucesso);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Mensagem));
    }

    [Fact]
    public void Descricao_respeita_o_limite_da_coluna()
    {
        var frase = "gastei 15 em " + new string('a', 120);
        var resultado = Interpretar(frase);

        Assert.True(resultado.Sucesso);
        Assert.True(resultado.Lancamento!.Descricao.Length <= Lancamento.TamanhoMaximoDescricao);
    }

    [Fact]
    public void Normalizacao_remove_acentos_e_pontuacao()
    {
        Assert.Equal("saude alimentacao", InterpretadorDeVoz.Normalizar("Saúde, alimentação!"));
    }

    [Fact]
    public void Frase_recebimento_com_moeda_falada_limpa_a_descricao()
    {
        var resultado = Interpretar("recebimento pagamento mil reias");

        Assert.True(resultado.Sucesso);
        var lancamento = resultado.Lancamento!;

        Assert.False(lancamento.EhDespesa);
        Assert.Equal(1000m, lancamento.ValorComSinal);
        Assert.Equal("Pagamento", lancamento.Descricao);
    }

    [Fact]
    public void Frase_despesa_com_moeda_falada_limpa_a_descricao()
    {
        var resultado = Interpretar("despesa pagamento mil reais");

        Assert.True(resultado.Sucesso);
        Assert.True(resultado.Lancamento!.EhDespesa);
        Assert.Equal(-1000m, resultado.Lancamento.ValorComSinal);
        Assert.Equal("Pagamento", resultado.Lancamento.Descricao);
    }

    [Fact]
    public void Mil_reais_sem_digito_vale_mil()
    {
        var resultado = Interpretar("gastei mil reais no mercado");

        Assert.True(resultado.Sucesso);
        Assert.Equal(-1000m, resultado.Lancamento!.ValorComSinal);
        Assert.Equal("Mercado", resultado.Lancamento.Descricao);
    }

    [Fact]
    public void Letra_solta_nao_entra_na_descricao()
    {
        var resultado = Interpretar("recebi mil R$ de freelas");

        Assert.True(resultado.Sucesso);
        Assert.Equal(1000m, resultado.Lancamento!.ValorComSinal);
        Assert.DoesNotContain(" r", resultado.Lancamento.Descricao, StringComparison.OrdinalIgnoreCase);
    }
}
