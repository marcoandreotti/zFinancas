using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class SincronizacaoPage : ContentPage
{
    private readonly SincronizacaoViewModel _viewModel;

    public SincronizacaoPage()
    {
        InitializeComponent();

        _viewModel = ServicosApp.Obter<SincronizacaoViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.CarregarAsync();
    }
}
