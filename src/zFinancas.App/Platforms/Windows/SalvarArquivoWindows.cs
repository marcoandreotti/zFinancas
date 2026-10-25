namespace zFinancas.App.Plataformas;

/// <summary>
/// Exportação no Windows: grava direto na pasta <b>Downloads</b> do usuário, que é sempre
/// gravável e fica à mão no Explorer.
/// <para>
/// Não usamos o seletor de pastas do toolkit: no Windows ele não retorna (o diálogo não
/// chega a abrir) e a exportação ficava presa. O caminho aparece na tela de sincronização.
/// </para>
/// </summary>
public static class SalvarArquivoWindows
{
    public static Task<string?> SalvarAsync(
        string nomeArquivo,
        string conteudo,
        string tipoMime,
        CancellationToken ct)
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        return TentarGravarAsync(downloads, nomeArquivo, conteudo, ct);
    }

    private static async Task<string?> TentarGravarAsync(
        string pasta,
        string nomeArquivo,
        string conteudo,
        CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(pasta);

            var caminho = Path.Combine(pasta, nomeArquivo);

            await File.WriteAllTextAsync(caminho, conteudo, ct).ConfigureAwait(false);
            return caminho;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Servicos.RegistroDeFalhas.Registrar(ex, "SalvarArquivoWindows");
            return null;
        }
    }
}
