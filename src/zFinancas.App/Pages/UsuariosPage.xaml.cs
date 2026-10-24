using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class UsuariosPage : ContentPage
{
    private readonly UsuariosViewModel _viewModel;

    public UsuariosPage()
    {
        InitializeComponent();

        _viewModel = ServicosApp.Obter<UsuariosViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.CarregarAsync();
    }
}
