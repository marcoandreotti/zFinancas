using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using zFinancas.App.Plataformas;

namespace zFinancas.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Necessário para o “Salvar como” (Storage Access Framework): o resultado do seletor
    /// chega aqui e é repassado para <see cref="SalvarArquivoAndroid"/>.
    /// </summary>
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        SalvarArquivoAndroid.TratarResultado(requestCode, resultCode, data);
    }
}
