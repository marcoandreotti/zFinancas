using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class ContasPage : ContentPage
{
    private readonly ContasViewModel _viewModel;

    public ContasPage()
    {
        InitializeComponent();

        _viewModel = ServicosApp.Obter<ContasViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.CarregarAsync();
    }

    private void OnCorTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TapGestureRecognizer { BindingContext: CorOpcao cor })
        {
            _viewModel.DefinirCor(cor.Hex);
        }
    }
}
