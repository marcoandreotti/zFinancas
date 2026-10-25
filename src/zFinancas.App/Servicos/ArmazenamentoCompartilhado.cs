using CommunityToolkit.Maui.Storage;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.App.Servicos;

/// <summary>
/// Exportação/importação de arquivos.
/// <para>
/// O pacote <b>não</b> fica preso na pasta privada do app: ele é gravado na pasta escolhida
/// pelo usuário (seletor nativo, lembrada em <c>Preferences</c>) ou, na falta dela, na mesma
/// pasta gravável em que o banco foi aberto — que o app já validou no start.
/// </para>
/// </summary>
public sealed class ArmazenamentoCompartilhado : IArmazenamentoCompartilhado
{
    /// <summary>Pasta escolhida pelo usuário para os pacotes (vazia = padrão do app).</summary>
    public const string ChavePastaPacotes = "sync.pasta";

    private readonly CaminhoBanco _banco;

    public ArmazenamentoCompartilhado(CaminhoBanco banco) => _banco = banco;

    /// <summary>Pasta onde os pacotes são gravados hoje.</summary>
    public string PastaAtual
    {
        get
        {
            var escolhida = Preferences.Default.Get(ChavePastaPacotes, string.Empty);

            if (!string.IsNullOrWhiteSpace(escolhida))
            {
                return escolhida;
            }

            return Path.Combine(_banco.Pasta, "Sincronizacao");
        }
    }

    /// <summary>Pede uma pasta ao usuário (pasta de documentos, OneDrive, pen drive…).</summary>
    public async Task<string?> EscolherPastaAsync(CancellationToken ct = default)
    {
        var escolha = await FolderPicker.Default.PickAsync(ct).ConfigureAwait(false);

        if (!escolha.IsSuccessful || string.IsNullOrWhiteSpace(escolha.Folder?.Path))
        {
            return null;
        }

        var pasta = escolha.Folder.Path;

        // Valida escrita antes de lembrar a escolha: melhor falhar agora do que no export.
        var teste = Path.Combine(pasta, $".escrita-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(teste, "zfinancas", ct).ConfigureAwait(false);
        File.Delete(teste);

        Preferences.Default.Set(ChavePastaPacotes, pasta);
        return pasta;
    }

    /// <summary>Volta a usar a pasta padrão (junto do banco).</summary>
    public void UsarPastaPadrao() => Preferences.Default.Remove(ChavePastaPacotes);

    /// <summary>
    /// Abre o “Salvar como” do sistema no clique de exportar e grava onde o usuário escolher.
    /// Devolve null quando ele cancela.
    /// </summary>
    public async Task<string?> SalvarTextoAsync(
        string nomeArquivo,
        string conteudo,
        string titulo,
        CancellationToken ct = default)
    {
        var caminho = await SalvarNoSistemaAsync(nomeArquivo, conteudo, ct).ConfigureAwait(false);

        if (caminho is not null)
        {
            return caminho;
        }

        // Sem seletor (ou cancelado em plataforma sem picker): grava na pasta do app,
        // garantindo que o pacote exista.
        return await GravarAsync(nomeArquivo, conteudo, ct).ConfigureAwait(false);
    }

    private static Task<string?> SalvarNoSistemaAsync(string nomeArquivo, string conteudo, CancellationToken ct)
    {
        const string tipoMime = "application/octet-stream";

#if ANDROID
        return Plataformas.SalvarArquivoAndroid.SalvarAsync(nomeArquivo, conteudo, tipoMime, ct);
#elif WINDOWS
        return Plataformas.SalvarArquivoWindows.SalvarAsync(nomeArquivo, conteudo, tipoMime, ct);
#else
        return Task.FromResult<string?>(null);
#endif
    }

    /// <summary>Grava o pacote; se a pasta escolhida falhar, cai para a pasta do banco.</summary>
    private async Task<string> GravarAsync(string nomeArquivo, string conteudo, CancellationToken ct)
    {
        var tentativas = new List<(string Pasta, string Erro)>();

        foreach (var pasta in new[] { PastaAtual, Path.Combine(_banco.Pasta, "Sincronizacao") }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
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
                tentativas.Add((pasta, ex.Message));
            }
        }

        RegistroDeFalhas.Registrar(
            new IOException(string.Join(" | ", tentativas.Select(t => $"{t.Pasta}: {t.Erro}"))),
            "ExportarPacote");

        throw new DominioException(
            "Não consegui gravar o pacote. Use “Pasta dos pacotes” para escolher uma pasta com permissão de escrita.");
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
            throw new DominioException($"Selecione um arquivo {extensao} gerado pelo zFinancas.");
        }

        await using var fluxo = await resultado.OpenReadAsync().ConfigureAwait(false);
        using var leitor = new StreamReader(fluxo);
        var conteudo = await leitor.ReadToEndAsync(ct).ConfigureAwait(false);

        return new ArquivoTexto(resultado.FileName, conteudo);
    }
}
