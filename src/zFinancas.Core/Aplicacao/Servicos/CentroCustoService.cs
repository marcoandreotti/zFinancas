using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.Core.Aplicacao.Servicos;

/// <summary>Manutenção de centros de custo (tela de Centro de Custos).</summary>
public sealed class CentroCustoService
{
    private readonly ICentroCustoRepositorio _centros;
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho;
    private readonly IRelogio _relogio;

    public CentroCustoService(
        ICentroCustoRepositorio centros,
        IUnidadeDeTrabalho unidadeDeTrabalho,
        IRelogio relogio)
    {
        _centros = centros;
        _unidadeDeTrabalho = unidadeDeTrabalho;
        _relogio = relogio;
    }

    public Task<IReadOnlyList<CentroCusto>> ListarAsync(bool incluirInativos = false, CancellationToken ct = default)
        => _centros.ListarAsync(incluirInativos, ct);

    public async Task<CentroCusto> CriarAsync(RequisicaoCentroCusto requisicao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        var agora = _relogio.AgoraUtc;

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            await GarantirDescricaoUnicaAsync(requisicao.Descricao, ignorarId: 0, token).ConfigureAwait(false);

            var existentes = await _centros.ListarAsync(incluirInativos: true, token).ConfigureAwait(false);
            var primeiro = existentes.Count == 0;

            var centro = CentroCusto.Novo(
                requisicao.Descricao,
                principal: requisicao.Principal || primeiro,
                agora,
                requisicao.Icone);

            centro.AlterarAtivo(requisicao.Ativo || primeiro, agora);
            centro.Id = await _centros.SalvarAsync(centro, token).ConfigureAwait(false);

            if (centro.Principal)
            {
                await _centros.DefinirComoPrincipalAsync(centro.Id, agora, token).ConfigureAwait(false);
            }

            return centro;
        }, ct).ConfigureAwait(false);
    }

    public async Task<CentroCusto> AtualizarAsync(RequisicaoCentroCusto requisicao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        var agora = _relogio.AgoraUtc;

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var centro = await _centros.ObterPorIdAsync(requisicao.Id, token).ConfigureAwait(false)
                         ?? throw new DominioException("Centro de custo não encontrado.");

            await GarantirDescricaoUnicaAsync(requisicao.Descricao, centro.Id, token).ConfigureAwait(false);

            centro.Alterar(requisicao.Descricao, requisicao.Icone, agora);
            centro.AlterarAtivo(requisicao.Ativo, agora);

            await _centros.SalvarAsync(centro, token).ConfigureAwait(false);

            if (requisicao.Principal && centro.Ativo)
            {
                await _centros.DefinirComoPrincipalAsync(centro.Id, agora, token).ConfigureAwait(false);
                centro.Principal = true;
            }
            else if (!requisicao.Principal && centro.Principal)
            {
                centro.DefinirPrincipal(false, agora);
                await _centros.SalvarAsync(centro, token).ConfigureAwait(false);
            }

            return centro;
        }, ct).ConfigureAwait(false);
    }

    public async Task AlterarAtivoAsync(long centroCustoId, bool ativo, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;

        await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var centro = await _centros.ObterPorIdAsync(centroCustoId, token).ConfigureAwait(false)
                         ?? throw new DominioException("Centro de custo não encontrado.");

            if (!ativo)
            {
                var ativos = (await _centros.ListarAsync(incluirInativos: false, token).ConfigureAwait(false))
                    .Count(c => c.Id != centroCustoId);

                if (ativos == 0)
                {
                    throw new DominioException("É necessário manter ao menos um centro de custo ativo.");
                }
            }

            centro.AlterarAtivo(ativo, agora);
            await _centros.SalvarAsync(centro, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task DefinirPrincipalAsync(long centroCustoId, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;

        await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var centro = await _centros.ObterPorIdAsync(centroCustoId, token).ConfigureAwait(false)
                         ?? throw new DominioException("Centro de custo não encontrado.");

            if (!centro.Ativo)
            {
                throw new DominioException("Um centro de custo inativo não pode ser o principal.");
            }

            await _centros.DefinirComoPrincipalAsync(centroCustoId, agora, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task GarantirDescricaoUnicaAsync(string descricao, long ignorarId, CancellationToken ct)
    {
        var texto = (descricao ?? string.Empty).Trim();
        var centros = await _centros.ListarAsync(incluirInativos: true, ct).ConfigureAwait(false);

        if (centros.Any(c => c.Id != ignorarId &&
                             string.Equals(c.Descricao, texto, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new DominioException($"Já existe um centro de custo com a descrição '{texto}'.");
        }
    }
}
