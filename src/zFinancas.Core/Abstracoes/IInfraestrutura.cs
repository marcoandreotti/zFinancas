namespace zFinancas.Core.Abstracoes;

/// <summary>Seriação de pacotes de sincronização (implementação concreta na camada de dados).</summary>
public interface ISerializadorJson
{
    string Serializar<T>(T valor);

    T? Desserializar<T>(string json);
}

/// <summary>
/// Troca de arquivos com o mundo externo (compartilhar/abrir) usada na sincronização.
/// A implementação concreta vive na camada MAUI (Share/FilePicker), mantendo o Core testável.
/// </summary>
public interface IArmazenamentoCompartilhado
{
    /// <summary>Compartilha um arquivo de texto com o seletor nativo do sistema; devolve o caminho gravado.</summary>
    Task<string> CompartilharTextoAsync(string nomeArquivo, string conteudo, string titulo, CancellationToken ct = default);

    /// <summary>Abre o seletor de arquivos e devolve nome + conteúdo do arquivo escolhido.</summary>
    Task<ArquivoTexto?> EscolherArquivoTextoAsync(string extensao, CancellationToken ct = default);
}

public sealed record ArquivoTexto(string Nome, string Conteudo);

/// <summary>Identidade do aparelho usada nos pacotes de sincronização.</summary>
public interface IInfoDispositivo
{
    /// <summary>Nome amigável (ex.: "PC do Marco", "Galaxy S23").</summary>
    string Nome { get; }

    /// <summary>Plataforma: Windows, Android, etc.</summary>
    string Plataforma { get; }

    /// <summary>Verdadeiro no celular é habilita entrada por biometria.</summary>
    bool EhMovel { get; }
}
