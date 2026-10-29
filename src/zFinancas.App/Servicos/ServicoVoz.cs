using System.Globalization;
using CommunityToolkit.Maui.Media;

namespace zFinancas.App.Servicos;

/// <summary>Resultado de uma tentativa de captura de voz.</summary>
public sealed record ResultadoVoz(bool Sucesso, string? Texto, string? Erro);

/// <summary>Captura de voz (Speech-to-Text) usada pela UI.</summary>
public interface IServicoVoz
{
    Task<ResultadoVoz> OuvirAsync(CancellationToken ct = default);
}

/// <summary>
/// Implementação sobre o <c>SpeechToText</c> do .NET MAUI Community Toolkit, que usa o
/// reconhecedor nativo de cada plataforma (Google no Android, Windows Speech no
/// desktop) — sem serviço em nuvem e coerente com o app 100% offline.
/// </summary>
public sealed class ServicoVoz : IServicoVoz
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeSpan LimiteEscuta = TimeSpan.FromSeconds(25);

    public async Task<ResultadoVoz> OuvirAsync(CancellationToken ct = default)
    {
#if DEBUG
        // Atalho de desenvolvimento: ZFINANCAS_VOZ="gastei 32,50 no mercado" simula a fala,
        // permitindo testar a interpretação e a UI em máquinas sem microfone.
        var simulacao = Environment.GetEnvironmentVariable("ZFINANCAS_VOZ");
        if (!string.IsNullOrWhiteSpace(simulacao))
        {
            await Task.Delay(300, ct).ConfigureAwait(true);
            return new ResultadoVoz(true, simulacao, null);
        }
#endif

        ISpeechToText? reconhecimento = null;
        TaskCompletionSource<SpeechToTextResult>? conclusao = null;

        void AoCompletar(object? remetente, SpeechToTextRecognitionResultCompletedEventArgs args)
            => conclusao?.TrySetResult(args.RecognitionResult);

        try
        {
            reconhecimento = SpeechToText.Default;
            conclusao = new TaskCompletionSource<SpeechToTextResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            reconhecimento.RecognitionResultCompleted += AoCompletar;

            await reconhecimento.RequestPermissions(ct).ConfigureAwait(true);

            await reconhecimento.StartListenAsync(new SpeechToTextOptions
            {
                Culture = Cultura,
                ShouldReportPartialResults = true
            }, ct).ConfigureAwait(true);

            var resultado = await conclusao.Task.WaitAsync(LimiteEscuta, ct).ConfigureAwait(true);

            if (!resultado.IsSuccessful)
            {
                return new ResultadoVoz(
                    false,
                    null,
                    resultado.Exception?.Message ?? "Não consegui reconhecer a fala. Tente novamente.");
            }

            var texto = (resultado.Text ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(texto)
                ? new ResultadoVoz(false, null, "Não ouvi nada. Fale mais perto do microfone.")
                : new ResultadoVoz(true, texto, null);
        }
        catch (OperationCanceledException)
        {
            return new ResultadoVoz(false, null, "Captura cancelada.");
        }
        catch (TimeoutException)
        {
            return new ResultadoVoz(false, null, "Nada foi dito. Toque no microfone e fale o lançamento.");
        }
        catch (Exception ex)
        {
            RegistroDeFalhas.Registrar(ex, "ServicoVoz");
            return new ResultadoVoz(
                false,
                null,
                "O reconhecimento de voz não está disponível neste aparelho. " +
                "Verifique o microfone e o idioma português do sistema.");
        }
        finally
        {
            if (reconhecimento is not null)
            {
                reconhecimento.RecognitionResultCompleted -= AoCompletar;

                try
                {
                    await reconhecimento.StopListenAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception)
                {
                    // já estava parado
                }
            }
        }
    }
}

/// <summary>
/// Junta captura de voz + interpretação: devolve um rascunho de lançamento pronto para
/// a tela de cadastro (o usuário sempre confirma).
/// </summary>
public sealed class ServicoLancamentoPorVoz
{
    private readonly IServicoVoz _voz;
    private readonly Core.Dominio.Voz.InterpretadorDeVoz _interpretador;
    private readonly Core.Aplicacao.Servicos.CentroCustoService _centros;
    private readonly Core.Aplicacao.Servicos.ContaCorrenteService _contas;
    private readonly Core.Abstracoes.IRelogio _relogio;

    public ServicoLancamentoPorVoz(
        IServicoVoz voz,
        Core.Dominio.Voz.InterpretadorDeVoz interpretador,
        Core.Aplicacao.Servicos.CentroCustoService centros,
        Core.Aplicacao.Servicos.ContaCorrenteService contas,
        Core.Abstracoes.IRelogio relogio)
    {
        _voz = voz;
        _interpretador = interpretador;
        _centros = centros;
        _contas = contas;
        _relogio = relogio;
    }

    public async Task<Core.Dominio.Voz.ResultadoInterpretacaoVoz> OuvirEInterpretarAsync(
        CancellationToken ct = default)
    {
        var captura = await _voz.OuvirAsync(ct).ConfigureAwait(true);

        if (!captura.Sucesso)
        {
            return new Core.Dominio.Voz.ResultadoInterpretacaoVoz(null, captura.Erro ?? "Não foi possível capturar a voz.");
        }

        var centros = await _centros.ListarAsync(ct: ct).ConfigureAwait(true);
        var contas = await _contas.ListarAsync(ct: ct).ConfigureAwait(true);

        return _interpretador.Interpretar(captura.Texto, centros, contas, _relogio.Hoje);
    }
}
