using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using AndroidX.Core.Content;
using zFinancas.App.Servicos;

// Biometria no Android com o BiometricPrompt nativo (API 28+).
// Sem plugins de terceiros: o app é offline e mantém a superfície de dependências mínima.
// A permissão android.permission.USE_BIOMETRIC é obrigatória — sem ela CanAuthenticate()
// nunca retorna sucesso e o prompt falha.
namespace zFinancas.App.Plataformas;

public sealed class ServicoBiometriaAndroid : IServicoBiometria
{
    private const string Tag = "zFinancas.Biometria";

    /// <summary>BIOMETRIC_WEAK: aceita digital e reconhecimento facial (sem PIN do aparelho).</summary>
    private const int BiometriaFraca = 0x00FF;

    private static readonly TimeSpan TempoLimite = TimeSpan.FromMinutes(2);

    public Task<bool> DisponivelAsync()
    {
        try
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.P)
            {
                return Task.FromResult(false);
            }

            var contexto = Android.App.Application.Context;
            var gerenciador = contexto.GetSystemService(Context.BiometricService) as BiometricManager;

            if (gerenciador is null)
            {
                return Task.FromResult(false);
            }

            var codigo = gerenciador.CanAuthenticate();

#if DEBUG
            Android.Util.Log.Info(Tag, $"CanAuthenticate = {codigo}");
#endif

            return Task.FromResult(codigo == BiometricCode.Success);
        }
        catch (Exception ex)
        {
            RegistroDeFalhas.Registrar(ex, "BiometriaDisponivel");
            return Task.FromResult(false);
        }
    }

    public async Task<ResultadoBiometria> AutenticarAsync(string motivo)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.P)
        {
            return ResultadoBiometria.Indisponivel;
        }

        var atividade = Platform.CurrentActivity;
        if (atividade is null)
        {
            return ResultadoBiometria.Indisponivel;
        }

        var conclusao = new TaskCompletionSource<ResultadoBiometria>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var sinal = new CancellationSignal();

        try
        {
            var executor = ContextCompat.GetMainExecutor(atividade);

            var construtor = new BiometricPrompt.Builder(atividade)
                .SetTitle("zFinancas")
                .SetSubtitle(motivo)
                .SetDescription("Use sua digital ou reconhecimento facial");

            // Obrigatório quando não se permite a senha do aparelho: sem um botão negativo
            // nomeado, Builder.Build() lança "Negative text must be set and non-empty".
            construtor.SetNegativeButton("Cancelar", executor, new BotaoCancelar(conclusao));

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                construtor.SetConfirmationRequired(false);
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                construtor.SetAllowedAuthenticators(BiometriaFraca);
            }

            // Sobrecarga de 3 argumentos (sem CryptoObject): passar "null" como primeiro
            // argumento casaria com Authenticate(CryptoObject, ...) e o Android lançaria
            // "Must supply a crypto object".
            var prompt = construtor.Build();

            prompt.Authenticate(sinal, executor, new RetornoBiometrico(conclusao));

            using var limite = new CancellationTokenSource(TempoLimite);

            using var registro = limite.Token.Register(() =>
            {
                try
                {
                    sinal.Cancel();
                }
                catch (Exception ex)
                {
                    RegistroDeFalhas.Registrar(ex, "BiometriaCancelar");
                }

                conclusao.TrySetResult(ResultadoBiometria.Cancelado);
            });

            return await conclusao.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RegistroDeFalhas.Registrar(ex, "BiometriaAutenticar");
            return ResultadoBiometria.Indisponivel;
        }
        finally
        {
            sinal.Dispose();
        }
    }

    /// <summary>Botão “Cancelar” do diálogo do sistema.</summary>
    private sealed class BotaoCancelar : Java.Lang.Object, Android.Content.IDialogInterfaceOnClickListener
    {
        private readonly TaskCompletionSource<ResultadoBiometria> _conclusao;

        public BotaoCancelar(TaskCompletionSource<ResultadoBiometria> conclusao) => _conclusao = conclusao;

        public void OnClick(Android.Content.IDialogInterface? dialogo, int which)
            => _conclusao.TrySetResult(ResultadoBiometria.Cancelado);
    }

    private sealed class RetornoBiometrico : BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<ResultadoBiometria> _conclusao;

        public RetornoBiometrico(TaskCompletionSource<ResultadoBiometria> conclusao) => _conclusao = conclusao;

        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult resultado)
            => _conclusao.TrySetResult(ResultadoBiometria.Sucesso);

        public override void OnAuthenticationFailed()
        {
            // Leitura não reconhecida: o sistema deixa tentar de novo, então não finaliza.
#if DEBUG
            Android.Util.Log.Info(Tag, "Tentativa não reconhecida");
#endif
        }

        /// <summary>
        /// Códigos de erro do BiometricPrompt (valores estáveis da plataforma; o enum do
        /// binding muda de nome entre versões, então comparamos por número).
        /// </summary>
        private const int ErroCancelado = 5;

        private const int ErroLockout = 7;
        private const int ErroPermanente = 9;
        private const int ErroUsuarioCancelou = 10;
        private const int ErroSemBiometria = 11;
        private const int ErroSemHardware = 12;
        private const int ErroBotaoNegativo = 13;

        public override void OnAuthenticationError(BiometricErrorCode codigoErro, Java.Lang.ICharSequence? mensagem)
        {
#if DEBUG
            Android.Util.Log.Info(Tag, $"Erro {(int)codigoErro}: {mensagem}");
#endif

            _conclusao.TrySetResult((int)codigoErro switch
            {
                ErroCancelado or ErroUsuarioCancelou or ErroBotaoNegativo => ResultadoBiometria.Cancelado,
                ErroSemBiometria or ErroSemHardware => ResultadoBiometria.Indisponivel,
                ErroLockout or ErroPermanente => ResultadoBiometria.Falhou,
                _ => ResultadoBiometria.Falhou
            });
        }
    }
}
