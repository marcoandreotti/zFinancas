using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class PrimeiroAcessoPage : ContentPage
{
    private readonly PrimeiroAcessoViewModel _viewModel;

    public PrimeiroAcessoPage()
    {
        InitializeComponent();

        _viewModel = ServicosApp.Obter<PrimeiroAcessoViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InicializarAsync();
    }
}
