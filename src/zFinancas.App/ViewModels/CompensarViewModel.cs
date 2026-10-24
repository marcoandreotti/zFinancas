using System.Windows.Input;

namespace zFinancas.App.ViewModels;

/// <summary>
/// Estado do painel de compensação: mostra o vencimento do lançamento, permite escolher
/// a data da compensação (padrão: o próprio vencimento) e tem atalho para "hoje".
/// </summary>
public sealed class CompensarViewModel : BaseViewModel
{
    private DateTime _data;

    public CompensarViewModel(LancamentoVm item)
    {
        Item = item;

        // Padrão: mantém o vencimento como data da compensação.
        _data = item.DataVencimento.Date;

        HojeCommand = new Comando(() => Data = DateTime.Today);
        ManterVencimentoCommand = new Comando(() => Data = Item.DataVencimento.Date);
        ConfirmarCommand = new Comando(Confirmar);
        CancelarCommand = new Comando(Cancelar);
    }

    public LancamentoVm Item { get; }

    public string Descricao => Item.Descricao;

    public string ValorTexto => Item.ValorTexto;

    public string VencimentoTexto => $"Vencimento: {Formatador.Data(Item.DataVencimento)}";

    /// <summary>Data escolhida para a compensação.</summary>
    public DateTime Data
    {
        get => _data;
        set
        {
            if (SetProperty(ref _data, value.Date))
            {
                OnPropertyChanged(nameof(DataTexto));
                OnPropertyChanged(nameof(EhVencimento));
                OnPropertyChanged(nameof(EhHoje));
            }
        }
    }

    public string DataTexto => Formatador.Data(Data);

    public bool EhVencimento => Data.Date == Item.DataVencimento.Date;

    public bool EhHoje => Data.Date == DateTime.Today;

    public string RotuloHoje => $"Hoje ({Formatador.Data(DateTime.Today)})";

    public ICommand HojeCommand { get; }
    public ICommand ManterVencimentoCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand CancelarCommand { get; }

    /// <summary>Data confirmada para a compensação.</summary>
    public event EventHandler<DateTime>? Confirmou;

    public event EventHandler? Cancelou;

    private void Confirmar() => Confirmou?.Invoke(this, Data.Date);

    private void Cancelar() => Cancelou?.Invoke(this, EventArgs.Empty);
}
