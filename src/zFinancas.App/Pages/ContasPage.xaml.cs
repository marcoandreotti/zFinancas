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
    /// <summary>
    /// Seleção de cor pela CollectionView (toque nativo) — o TapGestureRecognizer em Border
    /// não é confiável no Android.
    /// </summary>
    private void OnCorSelecionada(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ViewModels.CorOpcao cor)
        {
            return;
        }

        _viewModel.DefinirCor(cor.Hex);

        if (sender is CollectionView lista)
        {
            lista.SelectedItem = null;
        }
    }
}