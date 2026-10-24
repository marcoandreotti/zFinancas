namespace zFinancas.Core.Dominio.Entidades;

/// <summary>
/// Vínculo usuário ↔ conta corrente (tabela USUARIOCONTACORRENTE).
/// <para>
/// Além das duas chaves do modelo original, o vínculo carrega carimbo de tempo e lápide:
/// é o que permite sincronizar "desvinculei a conta X do usuário Y" entre PC e celular
/// sem que o vínculo removido volte no próximo pacote.
/// </para>
/// </summary>
public sealed class UsuarioContaCorrente : IEquatable<UsuarioContaCorrente>
{
    public long UsuarioId { get; set; }

    public long ContaCorrenteId { get; set; }

    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public bool Excluido { get; set; }

    public UsuarioContaCorrente()
    {
    }

    public UsuarioContaCorrente(long usuarioId, long contaCorrenteId, DateTime atualizadoEm)
    {
        UsuarioId = usuarioId;
        ContaCorrenteId = contaCorrenteId;
        AtualizadoEm = atualizadoEm;
    }

    public void MarcarExclusao(DateTime agoraUtc)
    {
        Excluido = true;
        AtualizadoEm = agoraUtc;
    }

    public void Reativar(DateTime agoraUtc)
    {
        Excluido = false;
        AtualizadoEm = agoraUtc;
    }

    public bool Equals(UsuarioContaCorrente? outro)
        => outro is not null && outro.UsuarioId == UsuarioId && outro.ContaCorrenteId == ContaCorrenteId;

    public override bool Equals(object? obj) => obj is UsuarioContaCorrente outro && Equals(outro);

    public override int GetHashCode() => HashCode.Combine(UsuarioId, ContaCorrenteId);
}
