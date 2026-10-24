using zFinancas.Core.Dominio.Enums;

namespace zFinancas.Core.Dominio.Servicos;

/// <summary>Uma parcela calculada de uma série de lançamentos.</summary>
public readonly record struct ParcelaGerada(long Numero, long Total, DateTime Vencimento);

/// <summary>
/// Calcula os vencimentos de um lançamento repetido (semanal/quinzenal/mensal/anual).
/// <para>
/// Regra: as datas são sempre derivadas da <b>primeira</b> data de vencimento
/// (<c>primeira + n * período</c>). Derivar da parcela anterior acumularia o ajuste
/// de fim de mês: 31/01 ? 28/02 ? 28/03, quando o correto é 31/01 ? 28/02 ? 31/03.
/// </para>
/// </summary>
public sealed class GeradorDeParcelas
{
    public const int MaximoParcelas = 360;

    public IReadOnlyList<ParcelaGerada> Gerar(DateTime primeiroVencimento, Recorrencia recorrencia, int quantidadeParcelas)
    {
        if (quantidadeParcelas < 1)
        {
            throw new Excecoes.DominioException("A quantidade de parcelas deve ser no mínimo 1.");
        }

        if (quantidadeParcelas > MaximoParcelas)
        {
            throw new Excecoes.DominioException($"A quantidade de parcelas não pode passar de {MaximoParcelas}.");
        }

        // Lançamento avulso ignora a quantidade informada.
        var total = recorrencia == Recorrencia.Unica ? 1 : quantidadeParcelas;
        var parcelas = new List<ParcelaGerada>(total);

        for (var i = 0; i < total; i++)
        {
            var vencimento = recorrencia switch
            {
                Recorrencia.Semanal => primeiroVencimento.AddDays(7 * i),
                Recorrencia.Quinzenal => primeiroVencimento.AddDays(15 * i),
                Recorrencia.Mensal => primeiroVencimento.AddMonths(i),
                Recorrencia.Anual => primeiroVencimento.AddYears(i),
                _ => primeiroVencimento
            };

            parcelas.Add(new ParcelaGerada(i + 1, total, vencimento.Date));
        }

        return parcelas;
    }
}
