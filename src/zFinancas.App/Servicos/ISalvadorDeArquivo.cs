namespace zFinancas.App.Servicos;

/// <summary>
/// Abre o diálogo nativo de “Salvar como” e grava o conteúdo no destino escolhido.
/// <para>
/// Cada plataforma tem o seu mecanismo: no Android o seletor devolve um <c>content://</c>
/// (SAF), então a gravação acontece por stream — nunca por caminho de arquivo; no Windows
/// usamos o <c>FileSavePicker</c>. Devolve <c>null</c> quando o usuário cancela.
/// </para>
/// </summary>
public interface ISalvadorDeArquivo
{
    /// <summary>Extensão sugerida (ex.: ".zfsync") e tipo MIME usado no seletor.</summary>
    Task<string?> SalvarTextoAsync(
        string nomeArquivo,
        string conteudo,
        string tipoMime,
        CancellationToken ct = default);
}
