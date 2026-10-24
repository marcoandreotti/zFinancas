namespace zFinancas.Core.Abstracoes;

/// <summary>
/// Relógio injetável: mantém domínio e testes determinísticos
/// (em vez de espalhar <c>DateTime.Now</c> pelo código).
/// </summary>
public interface IRelogio
{
    /// <summary>Agora em UTC (usado em auditoria/sincronização).</summary>
    DateTime AgoraUtc { get; }

    /// <summary>Data local de hoje (usada nas regras de vencimento/extrato).</summary>
    DateTime Hoje { get; }
}
