using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class CentrosCustoPage : ContentPage
{
    private readonly CentrosCustoViewModel _viewModel;

    public CentrosCustoPage()
    {
        InitializeComponent();

        _viewModel = ServicosApp.Obter<CentrosCustoViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.CarregarAsync();
    }
}
