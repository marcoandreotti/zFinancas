using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    public LoginPage()
    {
        InitializeComponent();

        _viewModel = ServicosApp.Obter<LoginViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InicializarAsync();
        FocarSenha();
    }

    /// <summary>
    /// Deixa o cursor na senha ao abrir: o usuário já vem selecionado (o último usado), então
    /// digitar a senha é o próximo passo. O foco é pedido no ciclo seguinte ao aparecimento
    /// porque, no Android e no Windows, pedir durante o OnAppearing às vezes se perde antes de
    /// a página (e o teclado) terminar de montar.
    /// </summary>
    private void FocarSenha() => Dispatcher.DispatchDelayed(
        TimeSpan.FromMilliseconds(120),
        () =>
        {
            // A página pode já ter sido trocada (ex.: login por biometria bem-sucedido).
            if (Window is null || _viewModel.UsuarioSelecionado is null)
            {
                return;
            }

            if (CampoSenha.IsVisible && !CampoSenha.IsFocused)
            {
                CampoSenha.Focus();
            }
        });
}
