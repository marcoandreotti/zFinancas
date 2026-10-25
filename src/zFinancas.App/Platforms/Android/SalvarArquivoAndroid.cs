using System.Text;
using Android.App;
using Android.Content;
using Microsoft.Maui.ApplicationModel;

namespace zFinancas.App.Plataformas;

/// <summary>
/// “Salvar como” no Android via Storage Access Framework (<c>ACTION_CREATE_DOCUMENT</c>).
/// O usuário escolhe a pasta e o nome; a gravação vai por stream no <c>content://</c>
/// devolvido — sem permissão de armazenamento e sem depender de caminho de arquivo.
/// </summary>
public static class SalvarArquivoAndroid
{
    private const int CodigoRequisicao = 7013;

    private static TaskCompletionSource<string?>? _pendente;

    public static async Task<string?> SalvarAsync(
        string nomeArquivo,
        string conteudo,
        string tipoMime,
        CancellationToken ct)
    {
        var atividade = Platform.CurrentActivity;

        if (atividade is null)
        {
            return null;
        }

        var conclusao = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendente = conclusao;

        using var registro = ct.Register(() => conclusao.TrySetResult(null));

        var intencao = new Intent(Intent.ActionCreateDocument);
        intencao.AddCategory(Intent.CategoryOpenable);
        intencao.SetType(tipoMime);
        intencao.PutExtra(Intent.ExtraTitle, nomeArquivo);

        atividade.StartActivityForResult(intencao, CodigoRequisicao);

        var uri = await conclusao.Task.ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        try
        {
            using var destino = Android.Net.Uri.Parse(uri)!;
            await using var fluxo = atividade.ContentResolver?.OpenOutputStream(destino);

            if (fluxo is null)
            {
                return null;
            }

            await using var escritor = new StreamWriter(fluxo, new UTF8Encoding(false));
            await escritor.WriteAsync(conteudo).ConfigureAwait(false);

            return uri;
        }
        catch (Exception ex)
        {
            Servicos.RegistroDeFalhas.Registrar(ex, "SalvarArquivoAndroid");
            return null;
        }
    }

    /// <summary>Chamado pelo <c>MainActivity.OnActivityResult</c>.</summary>
    public static void TratarResultado(int codigoRequisicao, Result resultado, Intent? dados)
    {
        if (codigoRequisicao != CodigoRequisicao)
        {
            return;
        }

        var pendente = Interlocked.Exchange(ref _pendente, null);
        pendente?.TrySetResult(resultado == Result.Ok ? dados?.Data?.ToString() : null);
    }
}
