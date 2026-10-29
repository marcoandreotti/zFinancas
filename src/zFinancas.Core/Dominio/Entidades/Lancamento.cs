namespace zFinancas.Core.Dominio.Entidades;

/// <summary>
/// Lançamento financeiro (tabela LANCAMENTO).
/// <para>
/// Convenção de sinal: <see cref="Valor"/> negativo = despesa (saída), positivo = recebimento (entrada).
/// Assim o saldo é sempre a soma simples dos valores, sem precisar de uma coluna de tipo.
/// </para>
/// <para>
/// Séries de parcelas compartilham o mesmo <see cref="Agrupador"/> e variam em
/// <see cref="Parcela"/> / <see cref="QuantidadeParcelas"/> (ex.: parcela 3/10).
/// Lançamento avulso tem <see cref="Agrupador"/> = 0.
/// </para>
/// </summary>
public class Lancamento : EntidadeBase
{
    public const int TamanhoMaximoDescricao = 50;

    public long ContaCorrenteId { get; set; }

    public long CentroCustoId { get; set; }

    /// <summary>Data em que o lançamento foi criado/registrado.</summary>
    public DateTime DataLancamento { get; set; } = DateTime.Today;

    /// <summary>Data de vencimento da parcela.</summary>
    public DateTime DataVencimento { get; set; } = DateTime.Today;

    /// <summary>Data de compensação (baixa) é nula enquanto não compensado.</summary>
    public DateTime? DataCompensado { get; set; }

    public string Descricao { get; set; } = string.Empty;

    /// <summary>Negativo = despesa, positivo = recebimento.</summary>
    public decimal Valor { get; set; }

    public bool Compensado { get; set; }

    /// <summary>Identificador da série de parcelas; 0 quando avulso.</summary>
    public long Agrupador { get; set; }

    /// <summary>
    /// Identidade global da série. O <see cref="Agrupador"/> numérico é local do dispositivo,
    /// então duas séries criadas em aparelhos diferentes poderiam colidir após a sincronização.
    /// O UUID da série mantém o agrupamento correto entre PC e celular.
    /// </summary>
    public string? AgrupadorUuid { get; set; }

    /// <summary>Número da parcela (1..N).</summary>
    public long Parcela { get; set; } = 1;

    public long QuantidadeParcelas { get; set; } = 1;

    /// <summary>
    /// Periodicidade da série (mensal, semanal...). Nula nos lançamentos criados antes
    /// desta informação existir — nesse caso a edição pede para escolher.
    /// </summary>
    public Enums.Recorrencia? Recorrencia { get; set; }

    public string? Observacao { get; set; }

    // ------------------------------------------------------------------ leitura

    public bool EhDespesa => Valor < 0m;

    public bool EhRecebimento => Valor > 0m;

    public bool Pendente => !Compensado;

    /// <summary>Parte do valor que já entrou no saldo compensado.</summary>
    public decimal ValorCompensado => Compensado ? Valor : 0m;

    /// <summary>Parte do valor que ainda não foi compensada.</summary>
    public decimal ValorPendente => Compensado ? 0m : Valor;

    /// <summary>
    /// Só é série quando existe mais de uma parcela. O agrupamento sozinho não serve:
    /// lançamentos importados de bases antigas podem trazer um identificador de grupo
    /// mesmo sendo únicos.
    /// </summary>
    public bool FazParteDeSerie => QuantidadeParcelas > 1;

    /// <summary>Chave usada para operar a série inteira (quitação, exclusão das futuras).</summary>
    public string? ChaveDaSerie => !FazParteDeSerie
        ? null
        : !string.IsNullOrWhiteSpace(AgrupadorUuid)
            ? AgrupadorUuid
            : Agrupador > 0 ? $"L{Agrupador}" : null;

    /// <summary>Rótulo "3/10" (vazio para lançamentos avulsos).</summary>
    public string RotuloParcela => QuantidadeParcelas > 1 ? $"{Parcela}/{QuantidadeParcelas}" : string.Empty;

    public bool EstaVencido(DateTime hoje) => !Compensado && DataVencimento.Date < hoje.Date;

    // ---------------------------------------------------------------- comportamento

    public void Compensar(DateTime dataCompensacao, DateTime agoraUtc)
    {
        Compensado = true;
        DataCompensado = dataCompensacao.Date;
        MarcarAlteracao(agoraUtc);
    }

    public void Descompensar(DateTime agoraUtc)
    {
        Compensado = false;
        DataCompensado = null;
        MarcarAlteracao(agoraUtc);
    }

    public void AlterarDados(string descricao, decimal valor, DateTime dataVencimento, long centroCustoId, string? observacao, DateTime agoraUtc)
    {
        Descricao = (descricao ?? string.Empty).Trim();
        Valor = valor;
        DataVencimento = dataVencimento.Date;
        CentroCustoId = centroCustoId;
        Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim();
        MarcarAlteracao(agoraUtc);
        Validar();
    }

    /// <summary>
    /// Ajusta a série: nova quantidade de parcelas e a periodicidade. Usado ao acrescentar
    /// parcelas (todas as existentes passam a "X/Y" com o novo total).
    /// </summary>
    public void AjustarSerie(long quantidadeParcelas, Enums.Recorrencia recorrencia, DateTime agoraUtc)
    {
        QuantidadeParcelas = quantidadeParcelas;
        Recorrencia = recorrencia;
        MarcarAlteracao(agoraUtc);
        Validar();
    }

    public void Validar()
    {
        var erros = new List<string>();

        if (string.IsNullOrWhiteSpace(Descricao))
        {
            erros.Add("A descrição do lançamento é obrigatória.");
        }
        else if (Descricao.Trim().Length > TamanhoMaximoDescricao)
        {
            erros.Add($"A descrição do lançamento deve ter no máximo {TamanhoMaximoDescricao} caracteres.");
        }

        if (Valor == 0m)
        {
            erros.Add("O valor do lançamento não pode ser zero.");
        }

        if (ContaCorrenteId <= 0)
        {
            erros.Add("Selecione a conta corrente do lançamento.");
        }

        if (CentroCustoId <= 0)
        {
            erros.Add("Selecione o centro de custo do lançamento.");
        }

        if (QuantidadeParcelas < 1)
        {
            erros.Add("A quantidade de parcelas deve ser maior que zero.");
        }

        if (Parcela < 0 || (QuantidadeParcelas > 1 && Parcela < 1) || Parcela > QuantidadeParcelas)
        {
            erros.Add("Número de parcela inválido para a quantidade de parcelas informada.");
        }

        if (Compensado && DataCompensado is null)
        {
            erros.Add("Lançamento compensado precisa da data de compensação.");
        }

        if (!Compensado && DataCompensado is not null)
        {
            erros.Add("Lançamento não compensado não pode ter data de compensação.");
        }

        if (erros.Count > 0)
        {
            throw new Excecoes.DominioException(erros);
        }
    }
}
