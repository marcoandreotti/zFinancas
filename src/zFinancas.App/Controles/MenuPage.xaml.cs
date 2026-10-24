using zFinancas.App.ViewModels;

namespace zFinancas.App.Controles;

/// <summary>
/// Página que hospeda o menu lateral. O FlyoutPage exige uma Page no flyout,
/// então o conteúdo (ContentView) fica dentro desta página.
/// </summary>
public partial class MenuPage : ContentPage
{
    public MenuPage()
    {
        InitializeComponent();
        NavigationPage.SetHasNavigationBar(this, false);
    }

    public MenuViewModel ViewModel => Conteudo.ViewModel;
}
