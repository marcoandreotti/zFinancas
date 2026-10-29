using CommunityToolkit.Maui.Storage;

namespace zFinancas.App.Servicos;

/// <summary>
/// Permite ao usuário escolher onde o banco fica quando o local padrão está bloqueado.
/// A escolha é salva em <c>Preferences</c> (registro no Windows / SharedPreferences no
/// Android), que é gravável mesmo quando a pasta de dados do usuário está restrita.
/// </summary>
public sealed class ServicoPastaDados
{
    /// <summary>Chave compartilhada com <see cref="CaminhoBanco"/>.</summary>
    public const string ChavePreferencia = "banco.pasta";

    private readonly ServicoDialogos _dialogos;

    public ServicoPastaDados(ServicoDialogos dialogos)
    {
        _dialogos = dialogos;
    }

    /// <summary>Abre o seletor de pastas. Retorna a pasta escolhida ou null se cancelado.</summary>
    public async Task<string?> EscolherPastaAsync()
    {
        try
        {
            var escolha = await FolderPicker.Default.PickAsync(CancellationToken.None);

            if (!escolha.IsSuccessful || string.IsNullOrWhiteSpace(escolha.Folder?.Path))
            {
                return null;
            }

            var pasta = escolha.Folder.Path;

            // Valida escrita antes de salvar, para não trocar um problema por outro.
            var teste = Path.Combine(pasta, $".escrita-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(teste, "zfinancas");
            File.Delete(teste);

            Preferences.Default.Set(ChavePreferencia, pasta);

            await _dialogos.AlertaAsync(
                "Pasta alterada",
                $"O banco passará a ficar em:\n{pasta}\n\nFeche e abra o app para aplicar.");

            return pasta;
        }
        catch (Exception ex)
        {
            RegistroDeFalhas.Registrar(ex, "ServicoPastaDados");
            await _dialogos.AlertaAsync(
                "Não foi possível",
                "Não consegui usar a pasta escolhida. Verifique se você tem permissão de escrita nela.");
            return null;
        }
    }
}
