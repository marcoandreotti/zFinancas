namespace zFinancas.Core.Dominio.Enums;

/// <summary>
/// Periodicidade de repetição de um lançamento.
/// Define como as parcelas de uma mesma série são geradas (AGRUPADOR / PARCELA / QUANTIDADEPARCELAS).
/// </summary>
public enum Recorrencia
{
    /// <summary>Lançamento avulso: uma única parcela (1/1).</summary>
    Unica = 0,

    Semanal = 1,

    Quinzenal = 2,

    Mensal = 3,

    Anual = 4
}

public static class RecorrenciaExtensoes
{
    public static string Descrever(this Recorrencia recorrencia) => recorrencia switch
    {
        Recorrencia.Unica => "Não repetir",
        Recorrencia.Semanal => "Semanal",
        Recorrencia.Quinzenal => "Quinzenal",
        Recorrencia.Mensal => "Mensal",
        Recorrencia.Anual => "Anual",
        _ => recorrencia.ToString()
    };

    /// <summary>Avança uma data conforme a periodicidade (sempre a partir da parcela anterior).</summary>
    public static DateTime Avancar(this Recorrencia recorrencia, DateTime data) => recorrencia switch
    {
        Recorrencia.Semanal => data.AddDays(7),
        Recorrencia.Quinzenal => data.AddDays(15),
        Recorrencia.Mensal => data.AddMonths(1),
        Recorrencia.Anual => data.AddYears(1),
        _ => data
    };

    public static bool EhRepeticao(this Recorrencia recorrencia) => recorrencia != Recorrencia.Unica;
}
