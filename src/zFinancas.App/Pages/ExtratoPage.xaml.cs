using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class ExtratoPage : ContentPage
{
    private readonly ExtratoViewModel _viewModel;

    public ExtratoPage()
    {
        InitializeComponent();

        _viewModel = ServicosApp.Obter<ExtratoViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.CarregarAsync();
    }
}
