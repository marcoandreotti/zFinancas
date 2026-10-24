using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class RecuperarSenhaPage : ContentPage
{
    public RecuperarSenhaPage()
    {
        InitializeComponent();
        BindingContext = ServicosApp.Obter<RecuperarSenhaViewModel>();
    }
}
