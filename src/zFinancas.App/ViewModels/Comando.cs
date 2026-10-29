using System.Windows.Input;

namespace zFinancas.App.ViewModels;

/// <summary>Comando genérico com parâmetro.</summary>
public sealed class Comando<T> : ICommand
{
    private readonly Func<T?, Task> _executar;
    private readonly Func<T?, bool>? _podeExecutar;
    private bool _executando;

    public Comando(Func<T?, Task> executar, Func<T?, bool>? podeExecutar = null)
    {
        _executar = executar ?? throw new ArgumentNullException(nameof(executar));
        _podeExecutar = podeExecutar;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => !_executando && (_podeExecutar?.Invoke(Converter(parameter)) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        _executando = true;
        LevantarCanExecuteChanged();

        try
        {
            await _executar(Converter(parameter)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Falha ao executar comando: {ex}");
        }
        finally
        {
            _executando = false;
            LevantarCanExecuteChanged();
        }
    }

    public void LevantarCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? Converter(object? parameter) => parameter is T valor ? valor : default;
}

/// <summary>Comando assíncrono com parâmetro tipado (itens de lista).</summary>
public sealed class ComandoAssincrono<T> : ICommand
{
    private readonly Func<T?, Task> _executar;
    private readonly Func<T?, bool>? _podeExecutar;
    private bool _executando;

    public ComandoAssincrono(Func<T?, Task> executar, Func<T?, bool>? podeExecutar = null)
    {
        _executar = executar ?? throw new ArgumentNullException(nameof(executar));
        _podeExecutar = podeExecutar;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => !_executando && (_podeExecutar?.Invoke(Converter(parameter)) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        _executando = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await _executar(Converter(parameter)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Falha ao executar comando: {ex}");
        }
        finally
        {
            _executando = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static T? Converter(object? parameter) => parameter is T valor ? valor : default;
}
