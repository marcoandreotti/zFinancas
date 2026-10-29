using zFinancas.Core.Dominio.Entidades;

namespace zFinancas.Core.Dominio.Servicos;

/// <summary>
/// Desfecho do planejamento de quitação de parcelas.
/// <para>
/// Regra de negócio: ao quitar parcelas pendentes, os lançamentos originais são
/// <b>excluídos</b> e um único lançamento compensado é criado com o valor total da quitação.
/// Isso evita um extrato poluído com várias baixas parciais do mesmo compromisso.
/// </para>
/// </summary>
public sealed record PlanoQuitacao
{
    public required long ContaCorrenteId { get; init; }
    public required long? Agrupador { get; init; }
    public required DateTime DataQuitacao { get; init; }
    public required decimal ValorTotal { get; init; }
    public required string Descricao { get; init; }
    public required IReadOnlyList<Lancamento> LancamentosOriginais { get; init; }

    public long QuantidadeParcelas => LancamentosOriginais.Count;
}

/// <summary>
/// Valida e monta o plano de quitação de parcelas não compensadas.
/// </summary>
public sealed class PoliticaDeQuitacao
{
    public PlanoQuitacao Planejar(
        IEnumerable<Lancamento> selecionados,
        DateTime dataQuitacao,
        string? descricaoPersonalizada = null)
    {
        var lancamentos = (selecionados ?? []).ToList();
        var erros = new List<string>();

        if (lancamentos.Count == 0)
        {
            erros.Add("Selecione ao menos uma parcela para quitar.");
        }

        if (lancamentos.Any(l => l.Compensado))
        {
            erros.Add("Somente parcelas não compensadas podem ser quitadas.");
        }

        if (lancamentos.Select(l => l.ContaCorrenteId).Distinct().Count() > 1)
        {
            erros.Add("Todas as parcelas da quitação devem pertencer à mesma conta corrente.");
        }

        if (erros.Count > 0)
        {
            throw new Excecoes.DominioException(erros);
        }

        var total = lancamentos.Sum(l => l.Valor);
        if (total == 0m)
        {
            erros.Add("O valor total da quitação não pode ser zero.");
            throw new Excecoes.DominioException(erros);
        }

        var agrupadores = lancamentos.Select(l => l.Agrupador).Distinct().ToList();
        var agrupador = agrupadores.Count == 1 && agrupadores[0] > 0 ? agrupadores[0] : (long?)null;

        return new PlanoQuitacao
        {
            ContaCorrenteId = lancamentos[0].ContaCorrenteId,
            Agrupador = agrupador,
            DataQuitacao = dataQuitacao.Date,
            ValorTotal = total,
            Descricao = MontarDescricao(lancamentos, descricaoPersonalizada),
            LancamentosOriginais = lancamentos
        };
    }

    private static string MontarDescricao(IReadOnlyList<Lancamento> lancamentos, string? personalizada)
    {
        if (!string.IsNullOrWhiteSpace(personalizada))
        {
            var texto = personalizada.Trim();
            return texto.Length <= Lancamento.TamanhoMaximoDescricao
                ? texto
                : texto[..Lancamento.TamanhoMaximoDescricao];
        }

        var baseDescricao = lancamentos[0].Descricao;
        var rotulo = lancamentos.Count == 1
            ? "Quitação 1 parcela"
            : $"Quitação {lancamentos.Count} parcelas";

        var descricao = $"{rotulo} - {baseDescricao}";
        return descricao.Length <= Lancamento.TamanhoMaximoDescricao
            ? descricao
            : descricao[..Lancamento.TamanhoMaximoDescricao];
    }
}
