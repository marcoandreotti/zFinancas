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
    /// Máscara de moeda aplicada na própria Entry.
    /// No Android, reescrever o texto pelo binding durante a digitação estoura
    /// "end should be &lt; than charSequence length" — aqui a troca é feita no evento,
    /// com guarda de reentrância e cursor no fim.
    /// </summary>
    private void OnValorDigitado(object? sender, TextChangedEventArgs e)
    {
        if (_aplicandoMascara || sender is not Entry campo)
        {
            return;
        }

        var mascarado = MascaraMoeda.Aplicar(e.NewTextValue);

        if (!string.Equals(mascarado, e.NewTextValue, StringComparison.Ordinal))
        {
            _aplicandoMascara = true;

            try
            {
                campo.Text = mascarado;
                campo.CursorPosition = mascarado.Length;
            }
            finally
            {
                _aplicandoMascara = false;
            }
        }

        ViewModel.ValorTexto = mascarado;
    }

    /// <summary>Reflete no campo valores definidos pelo ViewModel (edição, voz).</summary>
    private void SincronizarCampoValor()
    {
        var texto = ViewModel.ValorTexto ?? string.Empty;

        if (string.Equals(CampoValor.Text, texto, StringComparison.Ordinal))
        {
            return;
        }

        _aplicandoMascara = true;

        try
        {
            CampoValor.Text = texto;
            CampoValor.CursorPosition = texto.Length;
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