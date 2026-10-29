using zFinancas.Core.Abstracoes;

namespace zFinancas.Data.Infra;

/// <summary>
/// Implementação transacional sobre o SQLite.
/// <para>
/// Se já existe transação aberta (chamada aninhada), apenas executa a operação —
/// garantindo que a unidade de trabalho mais externa controle o commit.
/// </para>
/// </summary>
public sealed class UnidadeDeTrabalho : IUnidadeDeTrabalho
{
    private readonly ContextoBanco _contexto;

    public UnidadeDeTrabalho(ContextoBanco contexto)
    {
        _contexto = contexto;
    }

    public async Task<T> ExecutarAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operacao);

        // Já existe unidade de trabalho neste fluxo: apenas participa dela.
        if (_contexto.SouDonoDoEscopo)
        {
            return await operacao(ct).ConfigureAwait(false);
        }

        using var _ = await _contexto.EntrarAsync(ct).ConfigureAwait(false);
        _contexto.AssumirEscopoNesteFluxo();

        var transacaoPropria = _contexto.TransacaoAtual is null;
        if (transacaoPropria)
        {
            await _contexto.IniciarTransacaoAsync(ct).ConfigureAwait(false);
        }

        try
        {
            var resultado = await operacao(ct).ConfigureAwait(false);

            if (transacaoPropria)
            {
                await _contexto.ConfirmarAsync(ct).ConfigureAwait(false);
            }

            return resultado;
        }
        catch
        {
            if (transacaoPropria)
            {
                await _contexto.DesfazerAsync(ct).ConfigureAwait(false);
            }

            throw;
        }
    }

    public Task ExecutarAsync(Func<CancellationToken, Task> operacao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operacao);

        return ExecutarAsync(async token =>
        {
            await operacao(token).ConfigureAwait(false);
            return true;
        }, ct);
    }
}
