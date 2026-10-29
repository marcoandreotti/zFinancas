namespace zFinancas.Core.Dominio.Entidades;

/// <summary>
/// Base para as entidades sincronizáveis.
/// <para>
/// <see cref="Id"/> é a chave local (autoincremento, conforme o modelo relacional);
/// <see cref="Uuid"/> é a identidade global usada na sincronização entre dispositivos,
/// porque identidades locais podem colidir entre PC e celular.
/// </para>
/// <para>
/// <see cref="AtualizadoEm"/> (UTC) e <see cref="Excluido"/> (lápide/tombstone)
/// implementam a resolução de conflito "última escrita vence" e permitem propagar
/// exclusões é sem lápides, um registro apagado no celular "ressuscitaria" no PC.
/// </para>
/// </summary>
public abstract class EntidadeBase
{
    public long Id { get; set; }

    public string Uuid { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Momento da última alteração, sempre em UTC.</summary>
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    /// <summary>Lápide usada pela sincronização.</summary>
    public bool Excluido { get; set; }

    /// <summary>Marca a entidade como alterada agora (chamado antes de persistir).</summary>
    public void MarcarAlteracao(DateTime agoraUtc) => AtualizadoEm = agoraUtc;

    /// <summary>Exclusão lógica: preserva o histórico para sincronização.</summary>
    public void MarcarExclusao(DateTime agoraUtc)
    {
        Excluido = true;
        AtualizadoEm = agoraUtc;
    }
}
