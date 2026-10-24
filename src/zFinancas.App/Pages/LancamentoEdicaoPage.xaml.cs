using System.ComponentModel;
using zFinancas.App.Servicos;
using zFinancas.App.ViewModels;

namespace zFinancas.App.Pages;

public partial class LancamentoEdicaoPage : ContentPage
{
    private bool _aplicandoMascara;

    /// <summary>
    /// Alguns aparelhos recusam reescrever o texto do <c>EditText</c> enquanto ele processa a
    /// digitação. Nesse caso passamos a formatar apenas ao sair do campo.
    /// </summary>
    private bool _formatarSomenteAoSair;

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
    /// Máscara de moeda do campo valor.
    /// <para>
    /// A reescrita do texto é <b>postergada</b> para o próximo ciclo da UI: no Android, trocar
    /// o texto dentro do próprio <c>TextChanged</c> (ou pelo binding) estoura
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

        ViewModel.ValorTexto = mascarado;

        if (_formatarSomenteAoSair ||
            e.NewTextValue is null ||
            mascarado == e.NewTextValue)
        {
            return;
        }

        Dispatcher.Dispatch(() => AplicarTexto(campo, mascarado));
    }

    /// <summary>Ao sair do campo, formata (caminho seguro, sem edição em andamento).</summary>
    private void OnValorDesfocado(object? sender, FocusEventArgs e)
    {
        if (sender is not Entry campo)
        {
            return;
        }

        var mascarado = MascaraMoeda.Aplicar(campo.Text);

        ViewModel.ValorTexto = mascarado;

        if (mascarado != campo.Text)
        {
            AplicarTexto(campo, mascarado);
        }
    }

    private void AplicarTexto(Entry campo, string texto)
    {
        if (_aplicandoMascara)
        {
            return;
        }

        _aplicandoMascara = true;

        try
        {
            campo.Text = texto;

            // Só mexe no cursor quando o texto realmente assumiu o valor esperado.
            if (campo.Text?.Length == texto.Length)
            {
                campo.CursorPosition = texto.Length;
            }
        }
        catch (Exception ex)
        {
            // Aparelho recusou a troca durante a digitação: formata só ao sair do campo.
            _formatarSomenteAoSair = true;
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
