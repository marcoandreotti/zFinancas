using zFinancas.Core.Abstracoes;

namespace zFinancas.Tests.Apoio;

/// <summary>Relógio controlável para testes determinísticos.</summary>
public sealed class RelogioFake : IRelogio
{
    public RelogioFake(DateTime hoje)
    {
        Hoje = hoje.Date;
        AgoraUtc = DateTime.SpecifyKind(hoje.Date.AddHours(12), DateTimeKind.Utc);
    }

    public DateTime AgoraUtc { get; set; }

    public DateTime Hoje { get; set; }

    public void AvancarDias(int dias)
    {
        Hoje = Hoje.AddDays(dias);
        AgoraUtc = AgoraUtc.AddDays(dias);
    }
}

/// <summary>Dispositivo de teste.</summary>
public sealed class DispositivoFake : IInfoDispositivo
{
    public string Nome { get; set; } = "Teste";

    public string Plataforma { get; set; } = "Test";

    public bool EhMovel { get; set; }
}
