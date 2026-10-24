namespace zFinancas.Core.Abstracoes;

/// <summary>
/// Fronteira transacional. Serviços de aplicação usam esta abstração em vez de
/// abrir transações do SQLite diretamente, mantendo o domínio ignorante do banco.
/// </summary>
public interface IUnidadeDeTrabalho
{
    Task<T> ExecutarAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken ct = default);

    Task ExecutarAsync(Func<CancellationToken, Task> operacao, CancellationToken ct = default);
}
