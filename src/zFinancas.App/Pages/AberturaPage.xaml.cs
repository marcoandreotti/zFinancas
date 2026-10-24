using System.Diagnostics;
using zFinancas.App.Servicos;

namespace zFinancas.App.Pages;

/// <summary>
/// Abertura animada: mostra o símbolo e a marca surgindo (fade + escala) enquanto o banco
/// é preparado, e então segue para a tela de entrada.
/// <para>
/// A splash do sistema é estática (limitação das plataformas); a animação acontece aqui,
/// nos primeiros instantes do app, com o mesmo fundo, então não há “salto” visual.
/// </para>
/// </summary>
public partial class AberturaPage : ContentPage
{
    /// <summary>Tempo mínimo na tela — sem isso, num aparelho rápido a abertura só pisca.</summary>
    private static readonly TimeSpan TempoMinimo = TimeSpan.FromMilliseconds(1650);

    private bool _iniciado;

    public AberturaPage()
    {
        InitializeComponent();
        NavigationPage.SetHasNavigationBar(this, false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_iniciado)
        {
            return;
        }

        _iniciado = true;

        var cronometro = Stopwatch.StartNew();

        try
        {
            var servicos = ServicosApp.Obter<ServicoInicializacao>();

            // Banco sendo preparado em paralelo com a animação.
            var preparo = servicos.GarantirProntoAsync();

            Marca.Opacity = 0;
            Marca.Scale = 0.86;

            await Task.WhenAll(
                Marca.FadeTo(1, 520, Easing.CubicOut),
                Marca.ScaleTo(1, 620, Easing.SpringOut));

            await Rodape.FadeTo(1, 380, Easing.CubicOut);

            await Task.WhenAny(preparo, Task.Delay(900));

            var restante = TempoMinimo - cronometro.Elapsed;
            if (restante > TimeSpan.Zero)
            {
                await Task.Delay(restante);
            }

            await ServicosApp.Obter<ServicoNavegacao>().AbrirLoginAsync();
        }
        catch (Exception ex)
        {
            RegistroDeFalhas.Registrar(ex, "Abertura");

            // Nunca trava na abertura: segue para o login.
            await ServicosApp.Obter<ServicoNavegacao>().AbrirLoginAsync();
        }
    }
}