using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class UsuarioEdicaoPage : ContentPage
{
    public UsuarioEdicaoPage()
    {
        InitializeComponent();

        ViewModel = ServicosApp.Obter<UsuarioEdicaoViewModel>();
        BindingContext = ViewModel;
    }

    public UsuarioEdicaoViewModel ViewModel { get; }
}
