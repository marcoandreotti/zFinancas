using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Core.Dominio.Servicos;

namespace zFinancas.Core.Aplicacao.Servicos;

/// <summary>
/// Monta o dashboard: cartões de conta com saldo (compensado, não compensado e total)
/// e os lançamentos dos últimos 7 dias.
/// </summary>
public sealed class DashboardService
{
    private const int DiasNaJanela = 7;

    private readonly ILancamentoRepositorio _lancamentos;
    private readonly CalculadoraDeSaldo _calculadora;
    private readonly ISessaoAtual _sessao;
    private readonly IRelogio _relogio;

    public DashboardService(
        ILancamentoRepositorio lancamentos,
        CalculadoraDeSaldo calculadora,
        ISessaoAtual sessao,
        IRelogio relogio)
    {
        _lancamentos = lancamentos;
        _calculadora = calculadora;
        _sessao = sessao;
        _relogio = relogio;
    }

    public async Task<ResumoDashboard> ObterAsync(CancellationToken ct = default)
    {
        var contas = _sessao.ContasPermitidas;

        if (contas.Count == 0)
        {
            throw new DominioException(
                "Nenhuma conta corrente disponível para este usuário. Peça ao administrador para vincular uma conta.");
        }

        var hoje = _relogio.Hoje.Date;
        var inicioJanela = hoje.AddDays(-(DiasNaJanela - 1));

        var ids = contas.Select(c => c.Id).ToList();
        var todos = await _lancamentos.ListarPorContasAsync(ids, ct: ct).ConfigureAwait(false);
        var porConta = todos
            .Where(l => !l.Excluido)
            .GroupBy(l => l.ContaCorrenteId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Lancamento>)g.ToList());

        var resumos = new List<ContaResumo>(contas.Count);
        decimal totalCompensado = 0, totalPendente = 0, totalVencido = 0;

        foreach (var conta in contas)
        {
            var lancamentosDaConta = porConta.TryGetValue(conta.Id, out var lista) ? lista : [];
            var saldo = _calculadora.Calcular(lancamentosDaConta, hoje);

            totalCompensado += saldo.Compensado;
            totalPendente += saldo.NaoCompensado;
            totalVencido += saldo.NaoCompensadoVencido;

            resumos.Add(new ContaResumo(conta.Id, conta.Descricao, conta.CorHex, conta.Icone, conta.Principal, saldo));
        }

        var ultimos = await _lancamentos
            .ListarUltimosAsync(ids, inicioJanela, hoje, limite: 30, ct)
            .ConfigureAwait(false);

        return new ResumoDashboard(
            resumos,
            new SaldoConta(totalCompensado, totalPendente, totalVencido),
            ultimos.Where(l => !l.Excluido).OrderByDescending(l => l.DataLancamento).ThenByDescending(l => l.Id).ToList(),
            inicioJanela,
            hoje);
    }
}
