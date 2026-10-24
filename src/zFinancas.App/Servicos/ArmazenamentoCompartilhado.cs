using zFinancas.Core.Abstracoes;

namespace zFinancas.App.Servicos;

/// <summary>
/// Exportação/importação de arquivos. O pacote é sempre gravado na pasta do app
/// (garantindo que exista um arquivo real) e, quando o sistema suporta, também é
/// oferecido no compartilhamento nativo.
/// </summary>
public sealed class ArmazenamentoCompartilhado : IArmazenamentoCompartilhado
{
    public async Task<string> CompartilharTextoAsync(
        string nomeArquivo,
        string conteudo,
        string titulo,
        CancellationToken ct = default)
    {
        var pasta = Path.Combine(FileSystem.AppDataDirectory, "Sincronizacao");
        Directory.CreateDirectory(pasta);

        var caminho = Path.Combine(pasta, nomeArquivo);
        await File.WriteAllTextAsync(caminho, conteudo, ct).ConfigureAwait(false);

        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = titulo,
                File = new ShareFile(caminho)
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Compartilhamento é opcional: o arquivo já está salvo e o caminho é exibido.
        }

        return caminho;
    }

    public async Task<ArquivoTexto?> EscolherArquivoTextoAsync(string extensao, CancellationToken ct = default)
    {
        var resultado = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Selecione o pacote de sincronização do zFinancas"
        }).ConfigureAwait(false);

        if (resultado is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(extensao)
            && !resultado.FileName.EndsWith(extensao, StringComparison.OrdinalIgnoreCase)
            && !resultado.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new Core.Dominio.Excecoes.DominioException(
                $"Selecione um arquivo {extensao} gerado pelo zFinancas.");
        }

        await using var fluxo = await resultado.OpenReadAsync().ConfigureAwait(false);
        using var leitor = new StreamReader(fluxo);
        var conteudo = await leitor.ReadToEndAsync(ct).ConfigureAwait(false);

        return new ArquivoTexto(resultado.FileName, conteudo);
    }
}
