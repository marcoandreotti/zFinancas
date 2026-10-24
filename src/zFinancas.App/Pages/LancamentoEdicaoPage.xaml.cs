using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class LancamentoEdicaoPage : ContentPage
{
    public LancamentoEdicaoPage()
    {
        InitializeComponent();

        ViewModel = ServicosApp.Obter<LancamentoEdicaoViewModel>();
        BindingContext = ViewModel;
    }

    public LancamentoEdicaoViewModel ViewModel { get; }
}
