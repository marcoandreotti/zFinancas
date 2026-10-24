using zFinancas.Core.Dominio.Entidades;

namespace zFinancas.Core.Dominio.Servicos;

/// <summary>Saldos de uma conta em um instante.</summary>
public readonly record struct SaldoConta(
    decimal Compensado,
    decimal NaoCompensado,
    decimal NaoCompensadoVencido)
{
    /// <summary>Saldo total (compensado + a compensar), que é o "valor total" do dashboard.</summary>
    public decimal Total => Compensado + NaoCompensado;
}

/// <summary>Linha do extrato, com o saldo acumulado após o lançamento.</summary>
public sealed record LancamentoExtrato
{
    public required Lancamento Lancamento { get; init; }

    /// <summary>Saldo acumulado após esta linha (inclui não compensados, isto é, projetado).</summary>
    public required decimal SaldoApos { get; init; }

    /// <summary>Saldo acumulado considerando apenas lançamentos compensados.</summary>
    public required decimal SaldoCompensadoApos { get; init; }

    /// <summary>Verdadeiro para lançamento não compensado anterior ao período (exibido destacado).</summary>
    public required bool ForaDoPeriodo { get; init; }

    public decimal Valor => Lancamento.Valor;
    public bool Negativo => Lancamento.Valor < 0m;
    public bool Pendente => Lancamento.Pendente;
}

/// <summary>Resultado completo do extrato de uma conta em um período.</summary>
public sealed record ExtratoConta
{
    public required long ContaCorrenteId { get; init; }
    public required DateTime Inicio { get; init; }
    public required DateTime Fim { get; init; }

    /// <summary>
    /// Saldo antes do início do período considerando <b>somente</b> lançamentos compensados
    /// (os não compensados ficam de fora, conforme a regra de negócio).
    /// </summary>
    public required decimal SaldoAnterior { get; init; }

    public required IReadOnlyList<LancamentoExtrato> Itens { get; init; }

    /// <summary>Saldo após a última linha do extrato (projetado, inclui pendentes).</summary>
    public decimal SaldoFinal => Itens.Count > 0 ? Itens[^1].SaldoApos : SaldoAnterior;

    /// <summary>Saldo compensado ao final do período.</summary>
    public decimal SaldoCompensadoFinal => Itens.Count > 0 ? Itens[^1].SaldoCompensadoApos : SaldoAnterior;

    public decimal TotalEntradas => Itens.Where(i => i.Valor > 0m).Sum(i => i.Valor);

    public decimal TotalSaidas => Itens.Where(i => i.Valor < 0m).Sum(i => i.Valor);
}

/// <summary>
/// Cálculos de saldo e extrato. Puro: recebe lançamentos, devolve números é sem banco, sem UI.
/// </summary>
public sealed class CalculadoraDeSaldo
{
    public SaldoConta Calcular(IEnumerable<Lancamento> lancamentos, DateTime hoje)
    {
        decimal compensado = 0m, naoCompensado = 0m, vencido = 0m;

        foreach (var lancamento in lancamentos)
        {
            if (lancamento.Compensado)
            {
                compensado += lancamento.Valor;
                continue;
            }

            naoCompensado += lancamento.Valor;
            if (lancamento.EstaVencido(hoje))
            {
                vencido += lancamento.Valor;
            }
        }

        return new SaldoConta(compensado, naoCompensado, vencido);
    }

    /// <summary>
    /// Monta o extrato do período.
    /// <para>
    /// Entram no resultado: lançamentos com data dentro do período <b>e</b> lançamentos ainda
    /// não compensados anteriores ao início do período (que aparecem destacados para não
    /// "sumirem" da visão do usuário).
    /// </para>
    /// </summary>
    public ExtratoConta GerarExtrato(
        long contaCorrenteId,
        IEnumerable<Lancamento> lancamentosDaConta,
        DateTime inicio,
        DateTime fim,
        DateTime hoje)
    {
        if (fim.Date < inicio.Date)
        {
            throw new Excecoes.DominioException("A data final do período não pode ser anterior à inicial.");
        }

        var todos = lancamentosDaConta.OrderBy(l => l.DataVencimento).ThenBy(l => l.Id).ToList();

        // Saldo inicial: somente compensados, com data de compensação anterior ao período.
        var saldoAnterior = todos
            .Where(l => l.Compensado && l.DataVencimento.Date < inicio.Date)
            .Sum(l => l.Valor);

        var selecionados = todos
            .Where(l => EstaNoPeriodo(l, inicio, fim) || EstaPendenteAnterior(l, inicio))
            .ToList();

        var itens = new List<LancamentoExtrato>(selecionados.Count);
        var saldo = saldoAnterior;
        var saldoCompensado = saldoAnterior;

        foreach (var lancamento in selecionados)
        {
            saldo += lancamento.Valor;
            if (lancamento.Compensado)
            {
                saldoCompensado += lancamento.Valor;
            }

            itens.Add(new LancamentoExtrato
            {
                Lancamento = lancamento,
                SaldoApos = saldo,
                SaldoCompensadoApos = saldoCompensado,
                ForaDoPeriodo = lancamento.DataVencimento.Date < inicio.Date
            });
        }

        return new ExtratoConta
        {
            ContaCorrenteId = contaCorrenteId,
            Inicio = inicio.Date,
            Fim = fim.Date,
            SaldoAnterior = saldoAnterior,
            Itens = itens
        };
    }

    private static bool EstaNoPeriodo(Lancamento lancamento, DateTime inicio, DateTime fim)
        => lancamento.DataVencimento.Date >= inicio.Date && lancamento.DataVencimento.Date <= fim.Date;

    private static bool EstaPendenteAnterior(Lancamento lancamento, DateTime inicio)
        => !lancamento.Compensado && lancamento.DataVencimento.Date < inicio.Date;
}

