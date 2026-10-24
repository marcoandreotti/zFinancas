using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class LancamentosPage : ContentPage
{
    private readonly LancamentosViewModel _viewModel;

#if DEBUG
    private static bool _hookDeModalExecutado;
#endif

    public LancamentosPage()
    {
        InitializeComponent();

        _viewModel = ServicosApp.Obter<LancamentosViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.CarregarAsync();

#if DEBUG
        if (!_hookDeModalExecutado)
        {
        var modal = Environment.GetEnvironmentVariable("ZFINANCAS_MODAL");
        if (!string.IsNullOrWhiteSpace(modal))
        {
            await Task.Delay(1500);
            if (modal == "quitar")
            {
                await _viewModel.DepurarQuitacaoAsync();
            }
            else if (modal == "excluir")
            {
                await _viewModel.DepurarExclusaoAsync();
            }

            _hookDeModalExecutado = true;
        }
        }
#endif
    }
}
