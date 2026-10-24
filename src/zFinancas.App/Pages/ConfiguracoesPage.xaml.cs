using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class ConfiguracoesPage : ContentPage
{
    private readonly ConfiguracoesViewModel _viewModel;
    private bool _carregando;

    public ConfiguracoesPage()
    {
        InitializeComponent();

        _viewModel = ServicosApp.Obter<ConfiguracoesViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _carregando = true;
        await _viewModel.CarregarAsync();
        _carregando = false;
    }

    private async void OnBiometriaToggled(object? sender, ToggledEventArgs e)
    {
        // O switch reflete o estado salvo; o toque do usuário é tratado pelo comando
        // para evitar alternar durante o carregamento da tela.
        if (_carregando) return;

        if (e.Value != _viewModel.BiometriaHabilitada)
        {
            _viewModel.AlternarBiometriaCommand.Execute(null);
        }

        await Task.CompletedTask;
    }
}
