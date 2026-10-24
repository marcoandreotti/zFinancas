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
    /// Campo de valor com máscara de moeda.
    /// <para>
    /// Enquanto o campo está com foco, <b>não tocamos no texto nem no cursor</b>: no Android,
    /// reescrever o <c>EditText</c> durante a digitação (por binding ou evento) estoura
    /// <c>IllegalArgumentException: end should be &lt; than charSequence length</c>. O valor é
    /// mascarado imediatamente no ViewModel (usado ao salvar) e o campo é formatado ao sair.
    /// </para>
    /// </summary>
    private void OnValorDigitado(object? sender, TextChangedEventArgs e)
    {
        if (_aplicandoMascara)
        {
            return;
        }

        ViewModel.ValorTexto = MascaraMoeda.Aplicar(e.NewTextValue);
    }

    /// <summary>Formata o campo quando ele perde o foco (sem edição em andamento).</summary>
    private void OnValorDesfocado(object? sender, FocusEventArgs e)
    {
        if (sender is not Entry campo)
        {
            return;
        }

        var mascarado = MascaraMoeda.Aplicar(campo.Text);

        ViewModel.ValorTexto = mascarado;

        if (!string.Equals(mascarado, campo.Text, StringComparison.Ordinal))
        {
            AplicarTexto(campo, mascarado);
        }
    }

    private void AplicarTexto(Entry campo, string texto)
    {
        if (_aplicandoMascara || campo.IsFocused)
        {
            return;
        }

        _aplicandoMascara = true;

        try
        {
            campo.Text = texto;
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

        AplicarTexto(CampoValor, texto);
    }

    private void AoMudarViewModel(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LancamentoEdicaoViewModel.ValorTexto) && !_aplicandoMascara)
        {
            SincronizarCampoValor();
        }
    }
}
