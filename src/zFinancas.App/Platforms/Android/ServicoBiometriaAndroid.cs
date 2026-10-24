using Android.Content;
using Android.OS;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using zFinancas.App.Servicos;

// Implementacao Android da biometria usando BiometricPrompt nativo (API 28+).
// Sem plugins de terceiros: o app e offline e mantem a superficie de dependencias minima.
namespace zFinancas.App.Plataformas;

public sealed class ServicoBiometriaAndroid : IServicoBiometria
{
    public Task<bool> DisponivelAsync()
    {
        try
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.Q)
            {
                return Task.FromResult(false);
            }

            var contexto = Android.App.Application.Context;
            var gerenciador = contexto.GetSystemService(Context.BiometricService)
                as Android.Hardware.Biometrics.BiometricManager;

            var codigo = gerenciador is null ? -1 : (int)gerenciador.CanAuthenticate();
            return Task.FromResult(codigo == (int)Android.Hardware.Biometrics.BiometricCode.Success);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    public async Task<bool> AutenticarAsync(string motivo)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.P)
        {
            return false;
        }

        var atividade = Platform.CurrentActivity;
        if (atividade is null)
        {
            return false;
        }

        var conclusao = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = ContextCompat.GetMainExecutor(atividade);

        var prompt = new Android.Hardware.Biometrics.BiometricPrompt.Builder(atividade)
            .SetTitle("zFinancas")
            .SetSubtitle(motivo)
            .SetDescription("Use sua digital ou reconhecimento facial para entrar.")
            .Build();

        using var sinal = new CancellationSignal();

        prompt.Authenticate(
            null,
            sinal,
            executor,
            new RetornoBiometrico(conclusao));

        // Garante retorno caso o dialogo seja descartado sem callback.
        return await conclusao.Task.WaitAsync(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    }

    private sealed class RetornoBiometrico : Android.Hardware.Biometrics.BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<bool> _conclusao;

        public RetornoBiometrico(TaskCompletionSource<bool> conclusao)
        {
            _conclusao = conclusao;
        }

        public override void OnAuthenticationSucceeded(Android.Hardware.Biometrics.BiometricPrompt.AuthenticationResult resultado)
            => _conclusao.TrySetResult(true);

        public override void OnAuthenticationFailed()
        {
            // Leitura nao reconhecida: o sistema permite tentar novamente, entao nao finaliza.
        }

        public override void OnAuthenticationError(
            Android.Hardware.Biometrics.BiometricErrorCode codigoErro,
            Java.Lang.ICharSequence? mensagem)
            => _conclusao.TrySetResult(false);
    }
}


