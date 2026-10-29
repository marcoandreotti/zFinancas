using zFinancas.Core.Dominio.Entidades;

namespace zFinancas.Core.Abstracoes;

public interface IUsuarioContaCorrenteRepositorio
{
    Task<IReadOnlyList<long>> ListarContasDoUsuarioAsync(long usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Define o conjunto de contas do usuário. Vínculos removidos são marcados como
    /// excluídos (lápide) em vez de apagados, para a sincronização propagar a remoção.
    /// </summary>
    Task DefinirContasDoUsuarioAsync(long usuarioId, IReadOnlyCollection<long> contasIds, CancellationToken ct = default);

    Task<IReadOnlyList<long>> ListarUsuariosDaContaAsync(long contaId, CancellationToken ct = default);

    Task<IReadOnlyList<UsuarioContaCorrente>> ListarVinculosAsync(bool incluirExcluidos = false, CancellationToken ct = default);

    Task SalvarVinculosAsync(IEnumerable<UsuarioContaCorrente> vinculos, CancellationToken ct = default);
}
