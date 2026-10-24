using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace zFinancas.App.ViewModels;

/// <summary>Conta exibida no painel de seleção de contas.</summary>
public sealed class ContaFiltro : INotifyPropertyChanged
{
    private bool _selecionada;

    public required long Id { get; init; }
    public required string Descricao { get; init; }
    public required string Icone { get; init; }
    public required bool Principal { get; init; }

    public string Rotulo => $"{Icone}  {Descricao}";

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
/// Painel de seleção de contas (exibição/conciliação). Permite marcar várias contas para
/// analisar o conjunto — usado em Lançamentos e Extrato.
/// </summary>
public sealed class SelecaoContasViewModel : INotifyPropertyChanged
{
    public SelecaoContasViewModel(
        string titulo,
        string instrucao,
        string textoConfirmar,
        IEnumerable<ContaFiltro> contas)
    {
        Titulo = titulo;
        Instrucao = instrucao;
        TextoConfirmar = textoConfirmar;

        foreach (var conta in contas)
        {
            conta.SelecaoAlterada += (_, _) => AtualizarResumo();
            Contas.Add(conta);
        }

        MarcarTodasCommand = new Comando(MarcarTodas);
        LimparCommand = new Comando(Limpar);
        ConfirmarCommand = new Comando(Confirmar);
        CancelarCommand = new Comando(Cancelar);

        AtualizarResumo();
    }

    public string Titulo { get; }

    public string Instrucao { get; }

    public string TextoConfirmar { get; }

    public ObservableCollection<ContaFiltro> Contas { get; } = [];

    public IReadOnlyList<long> IdsSelecionados => Contas.Where(c => c.Selecionada).Select(c => c.Id).ToList();

    public string Resumo { get; private set; } = "Nenhuma conta";

    public bool TemSelecao => Contas.Any(c => c.Selecionada);

    public ICommand MarcarTodasCommand { get; }
    public ICommand LimparCommand { get; }
    public ICommand ConfirmarCommand { get; }
    public ICommand CancelarCommand { get; }

    /// <summary>IDs escolhidos ao confirmar.</summary>
    public event EventHandler<IReadOnlyList<long>>? Confirmou;

    public event EventHandler? Cancelou;

    public void MarcarTodas()
    {
        foreach (var conta in Contas)
        {
            conta.Selecionada = true;
        }

        AtualizarResumo();
    }

    public void Limpar()
    {
        foreach (var conta in Contas)
        {
            conta.Selecionada = false;
        }

        AtualizarResumo();
    }

    private void Confirmar()
    {
        if (!TemSelecao)
        {
            return;
        }

        Confirmou?.Invoke(this, IdsSelecionados);
    }

    private void Cancelar() => Cancelou?.Invoke(this, EventArgs.Empty);

    public void AtualizarResumo()
    {
        var marcadas = Contas.Count(c => c.Selecionada);

        Resumo = marcadas switch
        {
            0 => "Nenhuma conta",
            1 => Contas.First(c => c.Selecionada).Descricao,
            _ => $"{marcadas} contas"
        };

        OnPropertyChanged(nameof(Resumo));
        OnPropertyChanged(nameof(TemSelecao));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? nome = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}
