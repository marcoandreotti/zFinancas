using System.ComponentModel;
using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class LancamentoEdicaoPage : ContentPage
{
    private bool _aplicandoMascara;

    public LancamentoEdicaoPage()
    {
        InitializeComponent();

        ViewModel = ServicosApp.Obter<LancamentoEdicaoViewModel>();
        BindingContext = ViewModel;

        ViewModel.PropertyChanged += AoMudarViewModel;
    }

    public LancamentoEdicaoViewModel ViewModel { get; }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SincronizarCampoValor();
    }

    /// <summary>
    /// Máscara de moeda do campo valor, no padrão “desanexa → formata → reanexa”.
    /// <para>
    /// A troca do texto é <b>síncrona</b> e não mexe no cursor: no Android, reescrever o
    /// <c>EditText</c> de forma assíncrona (ou reposicionar a seleção) estoura
    /// <c>IllegalArgumentException: end should be &lt; than charSequence length</c>.
    /// </para>
    /// </summary>
    private void OnValorDigitado(object? sender, TextChangedEventArgs e)
    {
        if (_aplicandoMascara || sender is not Entry campo)
        {
            return;
        }

        var mascarado = MascaraMoeda.Aplicar(e.NewTextValue);

        if (string.Equals(mascarado, e.NewTextValue, StringComparison.Ordinal))
        {
            ViewModel.ValorTexto = mascarado;
            return;
        }

        // Desanexa o handler para não entrar em laço, troca o texto e reanexa.
        _aplicandoMascara = true;

        try
        {
            campo.TextChanged -= OnValorDigitado;
            campo.Text = mascarado;
        }
        catch (Exception ex)
        {
            RegistroDeFalhas.Registrar(ex, "MascaraValor");
        }
        finally
        {
            campo.TextChanged += OnValorDigitado;
            _aplicandoMascara = false;
        }

        // Cursor no fim depois que o texto assenta: sem isso o próximo dígito entra no
        // começo do texto formatado. Mexer só na seleção não toca no texto.
        Dispatcher.Dispatch(() =>
        {
            if (campo.Text?.Length == mascarado.Length)
            {
                campo.CursorPosition = mascarado.Length;
            }
        });

        ViewModel.ValorTexto = mascarado;
    }

    /// <summary>Reflete no campo valores definidos pelo ViewModel (edição, voz).</summary>
    private void SincronizarCampoValor()
    {
        if (CampoValor.IsFocused)
        {
            return;
        }

        var texto = ViewModel.ValorTexto ?? string.Empty;

        if (string.Equals(CampoValor.Text, texto, StringComparison.Ordinal))
        {
            return;
        }

        _aplicandoMascara = true;

        try
        {
            CampoValor.Text = texto;
        }
        catch (Exception ex)
        {
            RegistroDeFalhas.Registrar(ex, "MascaraValor");
        }
        finally
        {
            _aplicandoMascara = false;
        }
    }

    private void AoMudarViewModel(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LancamentoEdicaoViewModel.ValorTexto) && !_aplicandoMascara)
        {
            SincronizarCampoValor();
        }
    }
}