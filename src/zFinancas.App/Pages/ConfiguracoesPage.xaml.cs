using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class ConfiguracoesPage : ContentPage
{
    private readonly ConfiguracoesViewModel _viewModel;

    /// <summary>Evita tratar como toque do usuário as mudanças que o próprio app faz.</summary>
    private bool _sincronizando;

    public ConfiguracoesPage()
    {
        InitializeComponent();

        _viewModel = ServicosApp.Obter<ConfiguracoesViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.CarregarAsync();
        SincronizarSwitch();

#if ANDROID
        Android.Util.Log.Info("zFinancas.Bio", $"Carregado: disponivel={_viewModel.BiometriaDisponivel} habilitada={_viewModel.BiometriaHabilitada} switch={SwitchBiometria.IsToggled} ativo={SwitchBiometria.IsEnabled}");
#endif
    }

    /// <summary>
    /// O switch não usa binding: o estado é aplicado aqui e, depois de cada tentativa, ele
    /// volta a refletir o que ficou realmente salvo (inclusive quando o usuário cancela).
    /// </summary>
    private void SincronizarSwitch()
    {
        _sincronizando = true;

        try
        {
            SwitchBiometria.IsToggled = _viewModel.BiometriaHabilitada;
        }
        finally
        {
            _sincronizando = false;
        }
    }

    private async void OnBiometriaToggled(object? sender, ToggledEventArgs e)
    {
#if ANDROID
        Android.Util.Log.Info("zFinancas.Bio", $"Toggled valor={e.Value} sincronizando={_sincronizando}");
#endif

        if (_sincronizando)
        {
            return;
        }

        try
        {
            await _viewModel.AlternarBiometriaAsync(e.Value);
        }
        finally
        {
            SincronizarSwitch();
        }
    }
}
