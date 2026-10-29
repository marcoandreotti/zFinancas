using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace zFinancas.App.ViewModels;

/// <summary>Parcela exibida no painel de seleção (quitação em série ou exclusão em lote).</summary>
public sealed class ParcelaSelecionavel : INotifyPropertyChanged
{
    private bool _selecionada;

    public long Id { get; init; }
    public string Descricao { get; init; }
    public string Detalhe { get; init; }
    public decimal Valor { get; init; }

    public string ValorTexto => Formatador.MoedaComSinal(Valor);

    /// <summary>TextColor espera Color (um Brush não é aplicado).</summary>
    public Color CorValor => Valor < 0 ? Paleta.Negativo : Paleta.Positivo;

    public bool Selecionada
    {
        get => _selecionada;
        set
        {
            if (_selecionada == value)
            {
                return;
            }

            _selecionada = value;
            OnPropertyChanged();
            SelecaoAlterada?.Invoke(this, EventArgs.Empty);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? SelecaoAlterada;

    private void OnPropertyChanged([CallerMemberName] string? nome = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}

/// <summary>
/// Estado do painel de seleção de parcelas (dados + totais). Os comandos ficam no
/// ViewModel da tela, que é quem executa a ação escolhida.
/// </summary>
public sealed class SelecaoParcelasViewModel : INotifyPropertyChanged
{
    public SelecaoParcelasViewModel(
        string titulo,
        string instrucao,
        IEnumerable<ParcelaSelecionavel> parcelas,
        string textoConfirmar)
    {
        Titulo = titulo;
        Instrucao = instrucao;
        TextoConfirmar = textoConfirmar;

        foreach (var parcela in parcelas)
        {
            parcela.SelecaoAlterada += (_, _) => AtualizarTotais();
            Parcelas.Add(parcela);
        }

        AtualizarTotais();
    }

    public string Titulo { get; }

    public string Instrucao { get; }

    public string TextoConfirmar { get; }

    public ObservableCollection<ParcelaSelecionavel> Parcelas { get; } = [];

    public IReadOnlyList<long> IdsSelecionados =>
        Parcelas.Where(p => p.Selecionada).Select(p => p.Id).ToList();

    public string TotalTexto { get; private set; } = Formatador.Moeda(0m);

    public string QuantidadeTexto { get; private set; } = "0 parcelas";

    public bool TemSelecao => Parcelas.Any(p => p.Selecionada);

    public void MarcarTodas()
    {
        foreach (var parcela in Parcelas)
        {
            parcela.Selecionada = true;
        }

        AtualizarTotais();
    }

    public void Limpar()
    {
        foreach (var parcela in Parcelas)
        {
            parcela.Selecionada = false;
        }

        AtualizarTotais();
    }

    public void AtualizarTotais()
    {
        var selecionadas = Parcelas.Where(p => p.Selecionada).ToList();

        TotalTexto = Formatador.Moeda(selecionadas.Sum(p => p.Valor));
        QuantidadeTexto = selecionadas.Count == 1
            ? "1 parcela"
            : $"{selecionadas.Count} parcelas";

        OnPropertyChanged(nameof(TotalTexto));
        OnPropertyChanged(nameof(QuantidadeTexto));
        OnPropertyChanged(nameof(TemSelecao));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? nome = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}
