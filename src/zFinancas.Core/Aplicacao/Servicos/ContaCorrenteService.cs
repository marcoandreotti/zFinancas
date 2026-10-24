using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.Core.Aplicacao.Servicos;

/// <summary>Manutenção de contas correntes (tela de Conta Corrente).</summary>
public sealed class ContaCorrenteService
{
    private readonly IContaCorrenteRepositorio _contas;
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho;
    private readonly IRelogio _relogio;

    public ContaCorrenteService(
        IContaCorrenteRepositorio contas,
        IUnidadeDeTrabalho unidadeDeTrabalho,
        IRelogio relogio)
    {
        _contas = contas;
        _unidadeDeTrabalho = unidadeDeTrabalho;
        _relogio = relogio;
    }

    public Task<IReadOnlyList<ContaCorrente>> ListarAsync(bool incluirInativas = false, CancellationToken ct = default)
        => _contas.ListarAsync(incluirInativas, ct);

    public async Task<ContaCorrente> CriarAsync(RequisicaoConta requisicao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        var agora = _relogio.AgoraUtc;

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            await GarantirDescricaoUnicaAsync(requisicao.Descricao, ignorarId: 0, token).ConfigureAwait(false);

            var existentes = await _contas.ListarAsync(incluirInativas: true, token).ConfigureAwait(false);
            var primeiraConta = existentes.Count == 0;

            var conta = ContaCorrente.Nova(
                requisicao.Descricao,
                principal: requisicao.Principal || primeiraConta,
                agora,
                requisicao.CorHex,
                requisicao.Icone);

            conta.AlterarAtivo(requisicao.Ativo || primeiraConta, agora);
            conta.Id = await _contas.SalvarAsync(conta, token).ConfigureAwait(false);

            if (conta.Principal)
            {
                await _contas.DefinirComoPrincipalAsync(conta.Id, agora, token).ConfigureAwait(false);
            }

            return conta;
        }, ct).ConfigureAwait(false);
    }

    public async Task<ContaCorrente> AtualizarAsync(RequisicaoConta requisicao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        var agora = _relogio.AgoraUtc;

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var conta = await _contas.ObterPorIdAsync(requisicao.Id, token).ConfigureAwait(false)
                        ?? throw new DominioException("Conta corrente não encontrada.");

            await GarantirDescricaoUnicaAsync(requisicao.Descricao, conta.Id, token).ConfigureAwait(false);

            conta.Alterar(requisicao.Descricao, requisicao.CorHex, requisicao.Icone, agora);
            conta.AlterarAtivo(requisicao.Ativo, agora);

            await _contas.SalvarAsync(conta, token).ConfigureAwait(false);

            if (requisicao.Principal && conta.Ativo)
            {
                await _contas.DefinirComoPrincipalAsync(conta.Id, agora, token).ConfigureAwait(false);
                conta.Principal = true;
            }
            else if (!requisicao.Principal && conta.Principal)
            {
                conta.DefinirPrincipal(false, agora);
                await _contas.SalvarAsync(conta, token).ConfigureAwait(false);
            }

            return conta;
        }, ct).ConfigureAwait(false);
    }

    public async Task AlterarAtivoAsync(long contaId, bool ativo, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;

        await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var conta = await _contas.ObterPorIdAsync(contaId, token).ConfigureAwait(false)
                        ?? throw new DominioException("Conta corrente não encontrada.");

            if (!ativo)
            {
                var ativas = (await _contas.ListarAsync(incluirInativas: false, token).ConfigureAwait(false))
                    .Count(c => c.Id != contaId);

                if (ativas == 0)
                {
                    throw new DominioException("É necessário manter ao menos uma conta corrente ativa.");
                }
            }

            conta.AlterarAtivo(ativo, agora);
            await _contas.SalvarAsync(conta, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task DefinirPrincipalAsync(long contaId, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;

        await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var conta = await _contas.ObterPorIdAsync(contaId, token).ConfigureAwait(false)
                        ?? throw new DominioException("Conta corrente não encontrada.");

            if (!conta.Ativo)
            {
                throw new DominioException("Uma conta inativa não pode ser a principal.");
            }

            await _contas.DefinirComoPrincipalAsync(contaId, agora, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task GarantirDescricaoUnicaAsync(string descricao, long ignorarId, CancellationToken ct)
    {
        var texto = (descricao ?? string.Empty).Trim();
        var contas = await _contas.ListarAsync(incluirInativas: true, ct).ConfigureAwait(false);

        if (contas.Any(c => c.Id != ignorarId &&
                            string.Equals(c.Descricao, texto, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new DominioException($"Já existe uma conta corrente com a descrição '{texto}'.");
        }
    }
}
