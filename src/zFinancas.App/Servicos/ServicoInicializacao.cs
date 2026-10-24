using zFinancas.Data.Repositorios;

namespace zFinancas.App.Servicos;

/// <summary>
/// Prepara o banco na inicialização (cria arquivo, aplica migrações) uma única vez,
/// de forma segura para chamadas concorrentes vindas de várias telas.
/// </summary>
public sealed class ServicoInicializacao
{
    private readonly MigradorBanco _migrador;
    private readonly SemaphoreSlim _semaforo = new(1, 1);

    private bool _pronto;

    public ServicoInicializacao(MigradorBanco migrador)
    {
        _migrador = migrador;
    }

    public async Task GarantirProntoAsync(CancellationToken ct = default)
    {
        if (_pronto) return;

        await _semaforo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pronto) return;

            await _migrador.AplicarAsync(ct).ConfigureAwait(false);
            _pronto = true;
        }
        finally
        {
            _semaforo.Release();
        }
    }
}
