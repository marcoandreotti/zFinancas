using System.Text.Encodings.Web;
using System.Text.Json;
using zFinancas.Core.Abstracoes;

namespace zFinancas.Data.Infra;

/// <summary>Relógio real do sistema.</summary>
public sealed class RelogioSistema : IRelogio
{
    public DateTime AgoraUtc => DateTime.UtcNow;

    public DateTime Hoje => DateTime.Today;
}

/// <summary>
/// Serialização JSON dos pacotes de sincronização.
/// <para>
/// O encoder relaxado mantém acentos e emojis legíveis no arquivo — o pacote é
/// inspecionável pelo usuário, o que ajuda a confiar no processo de sincronização.
/// </para>
/// </summary>
public sealed class SerializadorJson : ISerializadorJson
{
    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public string Serializar<T>(T valor) => JsonSerializer.Serialize(valor, Opcoes);

    public T? Desserializar<T>(string json) => JsonSerializer.Deserialize<T>(json, Opcoes);
}
