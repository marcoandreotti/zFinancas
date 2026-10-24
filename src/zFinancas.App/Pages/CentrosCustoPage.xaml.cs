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
    /// <summary>Escolhe o ícone pelo chip (chips grandes, ao contrário do Picker nativo).</summary>
    private void OnIconeTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TapGestureRecognizer { BindingContext: ViewModels.IconeOpcao opcao })
        {
            _viewModel.DefinirIcone(opcao.Valor);
        }
    }
}