using zFinancas.Core.Dominio.Entidades;

namespace zFinancas.Core.Abstracoes;

public interface IUsuarioRepositorio
{
    Task<bool> ExisteAlgumAsync(CancellationToken ct = default);

    Task<Usuario?> ObterPorIdAsync(long id, CancellationToken ct = default);

    Task<Usuario?> ObterPorNomeAsync(string nome, CancellationToken ct = default);

    Task<IReadOnlyList<Usuario>> ListarAsync(bool incluirInativos = true, CancellationToken ct = default);

    Task<int> ContarAdministradoresAtivosAsync(CancellationToken ct = default);

    /// <summary>Insere (Id == 0) ou atualiza. Retorna o Id persistido.</summary>
    Task<long> SalvarAsync(Usuario usuario, CancellationToken ct = default);

    /// <summary>Visão de sincronização: inclui registros excluídos (lápides).</summary>
    Task<IReadOnlyList<Usuario>> ListarTodosIncluindoExcluidosAsync(CancellationToken ct = default);
}

public interface IContaCorrenteRepositorio
{
    Task<ContaCorrente?> ObterPorIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<ContaCorrente>> ListarAsync(bool incluirInativas = false, CancellationToken ct = default);

    Task<ContaCorrente?> ObterPrincipalAsync(CancellationToken ct = default);

    Task<long> SalvarAsync(ContaCorrente conta, CancellationToken ct = default);

    /// <summary>Garante uma única conta principal ativa (zera as demais).</summary>
    Task DefinirComoPrincipalAsync(long contaId, DateTime agoraUtc, CancellationToken ct = default);

    Task<IReadOnlyList<ContaCorrente>> ListarTodosIncluindoExcluidosAsync(CancellationToken ct = default);
}

public interface ICentroCustoRepositorio
{
    Task<CentroCusto?> ObterPorIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<CentroCusto>> ListarAsync(bool incluirInativos = false, CancellationToken ct = default);

    Task<CentroCusto?> ObterPrincipalAsync(CancellationToken ct = default);

    Task<long> SalvarAsync(CentroCusto centroCusto, CancellationToken ct = default);

    Task DefinirComoPrincipalAsync(long centroCustoId, DateTime agoraUtc, CancellationToken ct = default);

    Task<IReadOnlyList<CentroCusto>> ListarTodosIncluindoExcluidosAsync(CancellationToken ct = default);
}

public interface ILancamentoRepositorio
{
    Task<Lancamento?> ObterPorIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Lancamento>> ListarPorContaAsync(
        long contaId,
        DateTime? inicio = null,
        DateTime? fim = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<Lancamento>> ListarPorContasAsync(
        IReadOnlyCollection<long> contasIds,
        DateTime? inicio = null,
        DateTime? fim = null,
        CancellationToken ct = default);

    /// <summary>Lançamentos de uma mesma série (por UUID da série ou por agrupador numérico).</summary>
    Task<IReadOnlyList<Lancamento>> ListarPorSerieAsync(string chaveDaSerie, CancellationToken ct = default);

    Task<IReadOnlyList<Lancamento>> ListarUltimosAsync(
        IReadOnlyCollection<long> contasIds,
        DateTime inicio,
        DateTime fim,
        int limite = 20,
        CancellationToken ct = default);

    /// <summary>Próximo número de agrupador (série de parcelas).</summary>
    Task<long> ProximoAgrupadorAsync(CancellationToken ct = default);

    Task<long> SalvarAsync(Lancamento lancamento, CancellationToken ct = default);

    Task SalvarVariosAsync(IEnumerable<Lancamento> lancamentos, CancellationToken ct = default);

    Task<IReadOnlyList<Lancamento>> ListarTodosIncluindoExcluidosAsync(CancellationToken ct = default);
}

public interface IConfiguracaoRepositorio
{
    Task<string?> ObterAsync(string chave, CancellationToken ct = default);

    Task DefinirAsync(string chave, string valor, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> ListarAsync(CancellationToken ct = default);
}
