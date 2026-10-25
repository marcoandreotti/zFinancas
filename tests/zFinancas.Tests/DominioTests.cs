using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Core.Dominio.Servicos;
using zFinancas.Core.Seguranca;

namespace zFinancas.Tests;

public class SenhaHasherTests
{
    private readonly SenhaHasher _hasher = new(10_000);

    [Fact]
    public void Hash_nao_armazena_a_senha_em_claro()
    {
        var hash = _hasher.Hash("senha123");

        Assert.DoesNotContain("senha123", hash);
        Assert.StartsWith("PBKDF2$", hash);
    }

    [Fact]
    public void Verificar_aceita_a_senha_correta_e_rejeita_a_incorreta()
    {
        var hash = _hasher.Hash("senha123");

        Assert.True(_hasher.Verificar("senha123", hash));
        Assert.False(_hasher.Verificar("senha124", hash));
        Assert.False(_hasher.Verificar(string.Empty, hash));
    }

    [Fact]
    public void Hashes_da_mesma_senha_sao_diferentes_por_causa_do_salt()
    {
        var primeiro = _hasher.Hash("senha123");
        var segundo = _hasher.Hash("senha123");

        Assert.NotEqual(primeiro, segundo);
        Assert.True(_hasher.Verificar("senha123", primeiro));
        Assert.True(_hasher.Verificar("senha123", segundo));
    }

    [Fact]
    public void Hash_invalido_nao_autentica()
    {
        Assert.False(_hasher.Verificar("senha123", "texto-qualquer"));
        Assert.False(_hasher.Verificar("senha123", "PBKDF2$abc$x$y"));
    }

    [Fact]
    public void PrecisaRehash_identifica_hash_de_custo_menor()
    {
        var antigo = new SenhaHasher(10_000).Hash("senha123");
        var novo = new SenhaHasher(20_000);

        Assert.True(novo.PrecisaRehash(antigo));
        Assert.False(novo.PrecisaRehash(novo.Hash("senha123")));
    }

    [Fact]
    public void Codigo_de_recuperacao_e_normalizado_para_validacao()
    {
        var codigo = GeradorCodigoRecuperacao.Gerar();
        var normalizado = GeradorCodigoRecuperacao.Normalizar(codigo.ToLowerInvariant().Replace("-", " "));

        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", codigo);
        Assert.Equal(GeradorCodigoRecuperacao.Normalizar(codigo), normalizado);
    }
}

public class GeradorDeParcelasTests
{
    private readonly GeradorDeParcelas _gerador = new();

    [Fact]
    public void Recorrencia_unica_gera_sempre_uma_parcela()
    {
        var parcelas = _gerador.Gerar(new DateTime(2025, 1, 10), Recorrencia.Unica, 10);

        Assert.Single(parcelas);
        Assert.Equal(new DateTime(2025, 1, 10), parcelas[0].Vencimento);
        Assert.Equal(1, parcelas[0].Total);
    }

    [Fact]
    public void Mensal_respeita_fim_de_mes_sem_acumular_desvio()
    {
        // 31/01 + 1 mês = 28/02; 31/01 + 2 meses = 31/03 (e não 28/03).
        var parcelas = _gerador.Gerar(new DateTime(2025, 1, 31), Recorrencia.Mensal, 3);

        Assert.Equal(new DateTime(2025, 1, 31), parcelas[0].Vencimento);
        Assert.Equal(new DateTime(2025, 2, 28), parcelas[1].Vencimento);
        Assert.Equal(new DateTime(2025, 3, 31), parcelas[2].Vencimento);
        Assert.Equal([1L, 2L, 3L], parcelas.Select(p => p.Numero));
        Assert.All(parcelas, p => Assert.Equal(3, p.Total));
    }

    [Fact]
    public void Semanal_e_quinzenal_avancam_em_dias()
    {
        var semanal = _gerador.Gerar(new DateTime(2025, 3, 1), Recorrencia.Semanal, 3);
        var quinzenal = _gerador.Gerar(new DateTime(2025, 3, 1), Recorrencia.Quinzenal, 3);

        Assert.Equal(new DateTime(2025, 3, 8), semanal[1].Vencimento);
        Assert.Equal(new DateTime(2025, 3, 15), semanal[2].Vencimento);

        Assert.Equal(new DateTime(2025, 3, 16), quinzenal[1].Vencimento);
        Assert.Equal(new DateTime(2025, 3, 31), quinzenal[2].Vencimento);
    }

    [Fact]
    public void Anual_avanca_em_anos()
    {
        var parcelas = _gerador.Gerar(new DateTime(2025, 6, 15), Recorrencia.Anual, 2);

        Assert.Equal(new DateTime(2026, 6, 15), parcelas[1].Vencimento);
    }

    [Fact]
    public void Quantidade_invalida_e_rejeitada()
    {
        Assert.Throws<DominioException>(() => _gerador.Gerar(DateTime.Today, Recorrencia.Mensal, 0));
        Assert.Throws<DominioException>(() => _gerador.Gerar(DateTime.Today, Recorrencia.Mensal, 361));
    }
}

public class CalculadoraDeSaldoTests
{
    private readonly CalculadoraDeSaldo _calculadora = new();
    private readonly DateTime _hoje = new(2025, 3, 15);

    [Fact]
    public void Separa_compensado_nao_compensado_e_vencido()
    {
        var lancamentos = new[]
        {
            CriarLancamento(1000m, new DateTime(2025, 3, 1), compensado: true),
            CriarLancamento(-200m, new DateTime(2025, 3, 5), compensado: false, vencimento: new DateTime(2025, 3, 20)),
            CriarLancamento(-150m, new DateTime(2025, 1, 10), compensado: false)
        };

        var saldo = _calculadora.Calcular(lancamentos, _hoje);

        Assert.Equal(1000m, saldo.Compensado);
        Assert.Equal(-350m, saldo.NaoCompensado);
        Assert.Equal(-150m, saldo.NaoCompensadoVencido);
        Assert.Equal(650m, saldo.Total);
    }

    [Fact]
    public void Saldo_segue_a_data_de_compensacao_e_nao_o_vencimento()
    {
        var salario = CriarLancamento(1000m, new DateTime(2026, 1, 5), compensado: true, compensadoEm: new DateTime(2026, 1, 5));

        // Vencimento dentro do período, mas compensado ANTES: já está no saldo anterior.
        var pagoAntes = CriarLancamento(-100m, new DateTime(2026, 3, 3), compensado: true, compensadoEm: new DateTime(2026, 2, 10));

        // Vencimento dentro do período e compensado DENTRO: move o saldo.
        var pagoNoPeriodo = CriarLancamento(-50m, new DateTime(2026, 3, 20), compensado: true, compensadoEm: new DateTime(2026, 3, 21));

        var extrato = _calculadora.GerarExtrato(
            1, [salario, pagoAntes, pagoNoPeriodo],
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), new DateTime(2026, 3, 31));

        Assert.Equal(900m, extrato.SaldoAnterior);   // 1000 - 100 (compensados antes de 01/03)

        var linhaAntes = extrato.Itens.Single(i => i.Valor == pagoAntes.Valor);
        Assert.False(linhaAntes.AfetaSaldo);
        Assert.Equal(900m, linhaAntes.SaldoApos);

        var linhaPeriodo = extrato.Itens.Single(i => i.Valor == pagoNoPeriodo.Valor);
        Assert.True(linhaPeriodo.AfetaSaldo);
        Assert.Equal(850m, extrato.SaldoCompensadoFinal);
        Assert.Equal(850m, extrato.SaldoFinal);
    }

    [Fact]
    public void Saldo_anterior_do_extrato_ignora_nao_compensados()
    {
        var lancamentos = new[]
        {
            CriarLancamento(1000m, new DateTime(2025, 1, 10), compensado: true, compensadoEm: new DateTime(2025, 1, 10)),
            CriarLancamento(-200m, new DateTime(2025, 2, 5), compensado: true, compensadoEm: new DateTime(2025, 2, 5)),
            CriarLancamento(-150m, new DateTime(2025, 2, 20), compensado: false),
            CriarLancamento(300m, new DateTime(2025, 3, 10), compensado: true, compensadoEm: new DateTime(2025, 3, 10))
        };

        var extrato = _calculadora.GerarExtrato(1, lancamentos, new DateTime(2025, 3, 1), _hoje, _hoje);

        Assert.Equal(800m, extrato.SaldoAnterior);
        Assert.Equal(2, extrato.Itens.Count);

        // O pendente anterior ao período aparece destacado, mas NÃO move o saldo real
        // (só compensação muda dinheiro); o projetado é que sente o efeito.
        var pendente = extrato.Itens[0];
        Assert.True(pendente.ForaDoPeriodo);
        Assert.True(pendente.Pendente);
        Assert.False(pendente.AfetaSaldo);
        Assert.Equal(800m, pendente.SaldoApos);
        Assert.Equal(650m, pendente.SaldoProjetadoApos);

        // O compensado dentro do período move o saldo real.
        Assert.True(extrato.Itens[1].AfetaSaldo);
        Assert.Equal(1100m, extrato.Itens[1].SaldoApos);
        Assert.Equal(1100m, extrato.SaldoCompensadoFinal);
        Assert.Equal(950m, extrato.SaldoFinal);
        Assert.Equal(300m, extrato.TotalEntradas);
        Assert.Equal(-150m, extrato.TotalSaidas);
    }

    [Fact]
    public void Periodo_invertido_e_rejeitado()
    {
        Assert.Throws<DominioException>(() =>
            _calculadora.GerarExtrato(1, [], new DateTime(2025, 3, 10), new DateTime(2025, 3, 1), _hoje));
    }

    private static Lancamento CriarLancamento(
        decimal valor,
        DateTime data,
        bool compensado,
        DateTime? compensadoEm = null,
        DateTime? vencimento = null)
    {
        var lancamento = new Lancamento
        {
            Id = 1,
            ContaCorrenteId = 1,
            CentroCustoId = 1,
            Descricao = "Teste",
            Valor = valor,
            DataLancamento = data,
            DataVencimento = vencimento ?? data
        };

        if (compensado)
        {
            lancamento.Compensar(compensadoEm ?? data, DateTime.UtcNow);
        }

        return lancamento;
    }
}

public class PoliticaDeQuitacaoTests
{
    private readonly PoliticaDeQuitacao _politica = new();

    [Fact]
    public void Soma_as_parcelas_e_monta_descricao()
    {
        var parcelas = new[]
        {
            CriarParcela(-100m, 1, 3),
            CriarParcela(-50m, 2, 3)
        };

        var plano = _politica.Planejar(parcelas, new DateTime(2025, 3, 20));

        Assert.Equal(-150m, plano.ValorTotal);
        Assert.Equal(2, plano.QuantidadeParcelas);
        Assert.Contains("Quitação 2 parcelas", plano.Descricao);
        Assert.Equal(7, plano.Agrupador);
    }

    [Fact]
    public void Recusa_parcela_ja_compensada()
    {
        var compensada = CriarParcela(-100m, 1, 3);
        compensada.Compensar(new DateTime(2025, 3, 1), DateTime.UtcNow);

        Assert.Throws<DominioException>(() => _politica.Planejar([compensada], DateTime.Today));
    }

    [Fact]
    public void Recusa_parcelas_de_contas_diferentes()
    {
        var primeira = CriarParcela(-100m, 1, 2);
        var segunda = CriarParcela(-100m, 2, 2);
        segunda.ContaCorrenteId = 99;

        Assert.Throws<DominioException>(() => _politica.Planejar([primeira, segunda], DateTime.Today));
    }

    [Fact]
    public void Recusa_quitacao_de_valor_zero()
    {
        var primeira = CriarParcela(-100m, 1, 2);
        var segunda = CriarParcela(100m, 2, 2);

        Assert.Throws<DominioException>(() => _politica.Planejar([primeira, segunda], DateTime.Today));
    }

    private static Lancamento CriarParcela(decimal valor, long parcela, long total)
    {
        return new Lancamento
        {
            Id = parcela,
            ContaCorrenteId = 1,
            CentroCustoId = 1,
            Descricao = "Aluguel",
            Valor = valor,
            DataLancamento = new DateTime(2025, 3, 1),
            DataVencimento = new DateTime(2025, 3, 10).AddMonths((int)parcela - 1),
            Agrupador = 7,
            AgrupadorUuid = "serie-7",
            Parcela = parcela,
            QuantidadeParcelas = total
        };
    }
}
