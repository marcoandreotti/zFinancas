using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace zFinancas.App.ViewModels;

/// <summary>Base de view model: notificação de propriedades + estado de ocupado.</summary>
public abstract class BaseViewModel : INotifyPropertyChanged
{
    /// <summary>Tempo mínimo de espera antes de exibir o indicador (evita o "pisca").</summary>
    private const int AtrasoDoIndicador = 300;

    private bool _ocupado;
    private string _titulo = string.Empty;
    private bool _mostrarCarregando;
    private CancellationTokenSource? _atrasoIndicador;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Titulo
    {
        get => _titulo;
        protected set => SetProperty(ref _titulo, value);
    }

    /// <summary>Indicador de progresso exibido nas telas enquanto uma operação roda.</summary>
    public bool Ocupado
    {
        get => _ocupado;
        protected set
        {
            if (SetProperty(ref _ocupado, value))
            {
                OnPropertyChanged(nameof(NaoOcupado));
                AtualizarIndicador(value);
            }
        }
    }

    public bool NaoOcupado => !_ocupado;

    /// <summary>
    /// Igual a <see cref="Ocupado"/>, mas só fica verdadeiro quando a operação passa de
    /// <see cref="AtrasoDoIndicador"/> ms — cargas rápidas não piscam o overlay.
    /// </summary>
    public bool MostrarCarregando
    {
        get => _mostrarCarregando;
        private set => SetProperty(ref _mostrarCarregando, value);
    }

    /// <summary>
    /// Liga/desliga o indicador com atraso. Telas que controlam o próprio estado de carga
    /// (em vez de usar <see cref="Ocupado"/>) chamam este método no setter da sua propriedade.
    /// </summary>
    protected void AtualizarIndicador(bool ocupado)
    {
        _atrasoIndicador?.Cancel();
        _atrasoIndicador?.Dispose();
        _atrasoIndicador = null;

        if (!ocupado)
        {
            MostrarCarregando = false;
            return;
        }

        var atraso = new CancellationTokenSource();
        _atrasoIndicador = atraso;

        _ = Task.Delay(AtrasoDoIndicador, atraso.Token).ContinueWith(
            tarefa =>
            {
                if (tarefa.IsCanceled)
                {
                    return;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!atraso.IsCancellationRequested)
                    {
                        MostrarCarregando = true;
                    }
                });
            },
            TaskScheduler.Default);
    }

    protected bool SetProperty<T>(ref T campo, T valor, [CallerMemberName] string? nomePropriedade = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
        {
            return false;
        }

        campo = valor;
        OnPropertyChanged(nomePropriedade);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? nomePropriedade = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nomePropriedade));

    /// <summary>Executa uma rotina de tela tratando erros de negócio com mensagem amigável.</summary>
    protected async Task ExecutarAsync(
        Func<Task> acao,
        string? tituloErro = null,
        string? mensagemSucesso = null)
    {
        if (Ocupado) return;

        Ocupado = true;

        try
        {
            await acao().ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(mensagemSucesso))
            {
                await Servicos.ServicosApp.Obter<Servicos.ServicoDialogos>()
                    .AlertaAsync("Tudo certo", mensagemSucesso);
            }
        }
        catch (Core.Dominio.Excecoes.DominioException ex)
        {
            await Servicos.ServicosApp.Obter<Servicos.ServicoDialogos>()
                .AlertaAsync(tituloErro ?? "Atenção", ex.Message);
        }
        catch (Exception ex)
        {
            await Servicos.ServicosApp.Obter<Servicos.ServicoDialogos>()
                .AlertaAsync(tituloErro ?? "Erro inesperado", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }
}

/// <summary>Comando assíncrono com proteção contra dupla execução (evita duplo clique).</summary>
public sealed class ComandoAssincrono : ICommand
{
    private readonly Func<Task> _executar;
    private readonly Func<bool>? _podeExecutar;
    private bool _executando;

    public ComandoAssincrono(Func<Task> executar, Func<bool>? podeExecutar = null)
    {
        _executar = executar ?? throw new ArgumentNullException(nameof(executar));
        _podeExecutar = podeExecutar;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => !_executando && (_podeExecutar?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        _executando = true;
        LevantarCanExecuteChanged();

        try
        {
            await _executar().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // última barreira: nenhum comando deve derrubar o app.
            System.Diagnostics.Debug.WriteLine($"Falha ao executar comando: {ex}");
        }
        finally
        {
            _executando = false;
            LevantarCanExecuteChanged();
        }
    }

    public void LevantarCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Comando síncrono simples.</summary>
public sealed class Comando : ICommand
{
    private readonly Action _executar;
    private readonly Func<bool>? _podeExecutar;

    public Comando(Action executar, Func<bool>? podeExecutar = null)
    {
        _executar = executar ?? throw new ArgumentNullException(nameof(executar));
        _podeExecutar = podeExecutar;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _podeExecutar?.Invoke() ?? true;

    public void Execute(object? parameter) => _executar();

    public void LevantarCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
