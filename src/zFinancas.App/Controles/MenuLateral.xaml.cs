using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Controles;

public partial class MenuLateral : ContentView
{
    public MenuLateral()
    {
        InitializeComponent();

        ViewModel = ServicosApp.Obter<MenuViewModel>();
        ViewModel.Atualizar();
        BindingContext = ViewModel;
    }

    public MenuViewModel ViewModel { get; }

    private void OnItemSelecionado(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ItemMenu item)
        {
            return;
        }

        // Limpa a seleção para permitir escolher o mesmo item de novo depois.
        if (sender is CollectionView lista)
        {
            lista.SelectedItem = null;
        }

        ViewModel.Selecionar(item);
    }
}
