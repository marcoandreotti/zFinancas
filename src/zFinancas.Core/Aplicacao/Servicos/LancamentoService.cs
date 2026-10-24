using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Core.Dominio.Servicos;

namespace zFinancas.Core.Aplicacao.Servicos;

/// <summary>
/// Regras de lançamento: criação com recorrência/parcelas, alteração de pendentes,
/// compensação e quitação de parcelas.
/// </summary>
public sealed class LancamentoService
{
    private readonly ILancamentoRepositorio _lancamentos;
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho;
    private readonly GeradorDeParcelas _geradorDeParcelas;
    private readonly PoliticaDeQuitacao _politicaDeQuitacao;
    private readonly ISessaoAtual _sessao;
    private readonly IRelogio _relogio;

    public LancamentoService(
        ILancamentoRepositorio lancamentos,
        IUnidadeDeTrabalho unidadeDeTrabalho,
        GeradorDeParcelas geradorDeParcelas,
        PoliticaDeQuitacao politicaDeQuitacao,
        ISessaoAtual sessao,
        IRelogio relogio)
    {
        _lancamentos = lancamentos;
        _unidadeDeTrabalho = unidadeDeTrabalho;
        _geradorDeParcelas = geradorDeParcelas;
        _politicaDeQuitacao = politicaDeQuitacao;
        _sessao = sessao;
        _relogio = relogio;
    }

    public async Task<IReadOnlyList<Lancamento>> ListarAsync(
        long contaCorrenteId,
        DateTime? inicio = null,
        DateTime? fim = null,
        CancellationToken ct = default)
    {
        GarantirAcessoConta(contaCorrenteId);
        return await _lancamentos.ListarPorContaAsync(contaCorrenteId, inicio, fim, ct).ConfigureAwait(false);
    }

    public Task<Lancamento?> ObterAsync(long id, CancellationToken ct = default)
        => _lancamentos.ObterPorIdAsync(id, ct);

    public Task<IReadOnlyList<Lancamento>> ListarSerieAsync(string chaveDaSerie, CancellationToken ct = default)
        => _lancamentos.ListarPorSerieAsync(chaveDaSerie, ct);

    /// <summary>
    /// Cria um lançamento. Com recorrência diferente de "não repetir", gera N parcelas
    /// compartilhando AGRUPADOR (numérico) e AGRUPADORUUID (global, seguro na sincronização).
    /// </summary>
    public async Task<ResultadoCriacaoLancamentos> CriarAsync(
        RequisicaoLancamento requisicao,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        GarantirAcessoConta(requisicao.ContaCorrenteId);
        ValidarValor(requisicao.Valor);

        var agora = _relogio.AgoraUtc;
        var hoje = _relogio.Hoje;

        var quantidade = requisicao.Recorrencia == Recorrencia.Unica
            ? 1
            : Math.Clamp(requisicao.QuantidadeParcelas, 1, GeradorDeParcelas.MaximoParcelas);

        var parcelas = _geradorDeParcelas.Gerar(requisicao.DataVencimento, requisicao.Recorrencia, quantidade);

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var agrupador = parcelas.Count > 1 ? await _lancamentos.ProximoAgrupadorAsync(token).ConfigureAwait(false) : 0;
            var agrupadorUuid = parcelas.Count > 1 ? Guid.NewGuid().ToString("N") : null;

            var criados = new List<Lancamento>(parcelas.Count);

            foreach (var parcela in parcelas)
            {
                var lancamento = new Lancamento
                {
                    ContaCorrenteId = requisicao.ContaCorrenteId,
                    CentroCustoId = requisicao.CentroCustoId,
                    DataLancamento = (requisicao.DataLancamento ?? hoje).Date,
                    DataVencimento = parcela.Vencimento,
                    Descricao = (requisicao.Descricao ?? string.Empty).Trim(),
                    Valor = requisicao.Valor,
                    Agrupador = agrupador,
                    AgrupadorUuid = agrupadorUuid,
                    Parcela = parcela.Numero,
                    QuantidadeParcelas = parcela.Total,
                    Observacao = string.IsNullOrWhiteSpace(requisicao.Observacao) ? null : requisicao.Observacao.Trim(),
                    AtualizadoEm = agora
                };

                lancamento.Validar();
                criados.Add(lancamento);
            }

            // "Já compensado" baixa apenas a primeira parcela (a de menor vencimento):
            // as demais seguem pendentes para serem compensadas quando vencerem.
            if (requisicao.Compensado)
            {
                criados.OrderBy(l => l.DataVencimento).ThenBy(l => l.Parcela).First()
                    .Compensar(requisicao.DataCompensado ?? hoje, agora);
            }

            await _lancamentos.SalvarVariosAsync(criados, token).ConfigureAwait(false);

            return new ResultadoCriacaoLancamentos(criados, agrupador);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Altera um lançamento ainda não compensado.</summary>
    public async Task<Lancamento> AtualizarAsync(RequisicaoLancamento requisicao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);
        ValidarValor(requisicao.Valor);

        var agora = _relogio.AgoraUtc;
        var hoje = _relogio.Hoje;

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var lancamento = await _lancamentos.ObterPorIdAsync(requisicao.Id, token).ConfigureAwait(false)
                             ?? throw new DominioException("Lançamento não encontrado.");

            GarantirAcessoConta(lancamento.ContaCorrenteId);

            if (lancamento.Compensado)
            {
                throw new DominioException(
                    "Lançamento compensado não pode ser alterado. Descompense o lançamento antes de editar.");
            }

            lancamento.ContaCorrenteId = requisicao.ContaCorrenteId;
            lancamento.AlterarDados(
                requisicao.Descricao,
                requisicao.Valor,
                requisicao.DataVencimento,
                requisicao.CentroCustoId,
                requisicao.Observacao,
                agora);

            if (!string.IsNullOrWhiteSpace(requisicao.Descricao) && requisicao.Recorrencia == Recorrencia.Unica)
            {
                lancamento.Agrupador = 0;
                lancamento.AgrupadorUuid = null;
                lancamento.Parcela = 1;
                lancamento.QuantidadeParcelas = 1;
            }

            if (requisicao.Compensado)
            {
                lancamento.Compensar(requisicao.DataCompensado ?? hoje, agora);
            }

            await _lancamentos.SalvarAsync(lancamento, token).ConfigureAwait(false);
            return lancamento;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Exclui o lançamento. Com <paramref name="aPartirDaParcela"/>, exclui esta parcela
    /// e todas as seguintes ainda pendentes da mesma série.
    /// </summary>
    public async Task ExcluirAsync(long id, bool aPartirDaParcela = false, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;

        await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var lancamento = await _lancamentos.ObterPorIdAsync(id, token).ConfigureAwait(false)
                             ?? throw new DominioException("Lançamento não encontrado.");

            GarantirAcessoConta(lancamento.ContaCorrenteId);

            if (lancamento.Compensado)
            {
                throw new DominioException("Lançamento compensado não pode ser excluído. Descompense antes.");
            }

            var alvos = new List<Lancamento> { lancamento };

            if (aPartirDaParcela && lancamento.ChaveDaSerie is { } chave)
            {
                var serie = await _lancamentos.ListarPorSerieAsync(chave, token).ConfigureAwait(false);
                alvos = serie
                    .Where(l => !l.Compensado && l.Parcela >= lancamento.Parcela)
                    .ToList();
            }

            foreach (var alvo in alvos)
            {
                alvo.MarcarExclusao(agora);
            }

            await _lancamentos.SalvarVariosAsync(alvos, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Exclui um conjunto escolhido de parcelas de uma vez (todas precisam estar pendentes).
    /// Usado quando o usuário seleciona várias parcelas em aberto para remover.
    /// </summary>
    public async Task ExcluirVariasAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        var alvosIds = (ids ?? []).Distinct().ToList();

        if (alvosIds.Count == 0)
        {
            throw new DominioException("Selecione ao menos uma parcela para excluir.");
        }

        var agora = _relogio.AgoraUtc;

        await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var alvos = new List<Lancamento>(alvosIds.Count);

            foreach (var id in alvosIds)
            {
                var lancamento = await _lancamentos.ObterPorIdAsync(id, token).ConfigureAwait(false)
                                 ?? throw new DominioException("Lançamento não encontrado.");

                GarantirAcessoConta(lancamento.ContaCorrenteId);

                if (lancamento.Compensado)
                {
                    throw new DominioException(
                        $"'{lancamento.Descricao}' ({lancamento.RotuloParcela}) está compensado e não pode ser excluído. Descompense antes.");
                }

                lancamento.MarcarExclusao(agora);
                alvos.Add(lancamento);
            }

            await _lancamentos.SalvarVariosAsync(alvos, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task CompensarAsync(long id, DateTime? dataCompensacao = null, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;
        var data = (dataCompensacao ?? _relogio.Hoje).Date;

        await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var lancamento = await _lancamentos.ObterPorIdAsync(id, token).ConfigureAwait(false)
                             ?? throw new DominioException("Lançamento não encontrado.");

            GarantirAcessoConta(lancamento.ContaCorrenteId);
            lancamento.Compensar(data, agora);
            await _lancamentos.SalvarAsync(lancamento, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task DescompensarAsync(long id, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;

        await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var lancamento = await _lancamentos.ObterPorIdAsync(id, token).ConfigureAwait(false)
                             ?? throw new DominioException("Lançamento não encontrado.");

            GarantirAcessoConta(lancamento.ContaCorrenteId);
            lancamento.Descompensar(agora);
            await _lancamentos.SalvarAsync(lancamento, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Monta o plano de quitação para pré-visualização na UI.</summary>
    public async Task<PlanoQuitacao> PlanejarQuitacaoAsync(
        IEnumerable<long> lancamentoIds,
        DateTime dataQuitacao,
        string? descricao = null,
        CancellationToken ct = default)
    {
        var ids = (lancamentoIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new DominioException("Selecione ao menos uma parcela para quitar.");
        }

        var lancamentos = new List<Lancamento>(ids.Count);
        foreach (var id in ids)
        {
            var lancamento = await _lancamentos.ObterPorIdAsync(id, ct).ConfigureAwait(false)
                             ?? throw new DominioException("Lançamento não encontrado.");
            GarantirAcessoConta(lancamento.ContaCorrenteId);
            lancamentos.Add(lancamento);
        }

        return _politicaDeQuitacao.Planejar(lancamentos, dataQuitacao, descricao);
    }

    /// <summary>
    /// Executa a quitação: exclui as parcelas pendentes e cria UM lançamento compensado
    /// com o valor total da quitação.
    /// </summary>
    public async Task<Lancamento> QuitarAsync(PlanoQuitacao plano, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plano);

        var agora = _relogio.AgoraUtc;

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var referencia = plano.LancamentosOriginais[0];

            foreach (var original in plano.LancamentosOriginais)
            {
                var atual = await _lancamentos.ObterPorIdAsync(original.Id, token).ConfigureAwait(false);
                if (atual is null) continue;
                atual.MarcarExclusao(agora);
                await _lancamentos.SalvarAsync(atual, token).ConfigureAwait(false);
            }

            var quitacao = new Lancamento
            {
                ContaCorrenteId = plano.ContaCorrenteId,
                CentroCustoId = referencia.CentroCustoId,
                DataLancamento = _relogio.Hoje.Date,
                DataVencimento = plano.DataQuitacao.Date,
                Descricao = plano.Descricao,
                Valor = plano.ValorTotal,
                Compensado = true,
                DataCompensado = plano.DataQuitacao.Date,
                Agrupador = 0,
                AgrupadorUuid = null,
                Parcela = 0,
                QuantidadeParcelas = 1,
                Observacao = $"Quitação consolidada de {plano.QuantidadeParcelas} parcela(s).",
                AtualizadoEm = agora
            };

            quitacao.Validar();
            quitacao.Id = await _lancamentos.SalvarAsync(quitacao, token).ConfigureAwait(false);
            return quitacao;
        }, ct).ConfigureAwait(false);
    }

    private void GarantirAcessoConta(long contaCorrenteId)
    {
        if (_sessao.Autenticado && !_sessao.PodeAcessarConta(contaCorrenteId))
        {
            throw new DominioException("Você não tem acesso a esta conta corrente.");
        }
    }

    private static void ValidarValor(decimal valor)
    {
        if (valor == 0m)
        {
            throw new DominioException("O valor do lançamento não pode ser zero.");
        }
    }
}
