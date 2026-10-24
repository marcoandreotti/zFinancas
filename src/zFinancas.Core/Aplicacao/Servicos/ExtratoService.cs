using zFinancas.Core.Abstracoes;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Core.Dominio.Servicos;

namespace zFinancas.Core.Aplicacao.Servicos;

/// <summary>Extrato de lançamentos por conta e período (tela de Extrato).</summary>
public sealed class ExtratoService
{
    private readonly ILancamentoRepositorio _lancamentos;
    private readonly CalculadoraDeSaldo _calculadora;
    private readonly ISessaoAtual _sessao;
    private readonly IRelogio _relogio;

    public ExtratoService(
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

    public async Task<ExtratoConta> ObterAsync(
        long contaCorrenteId,
        DateTime inicio,
        DateTime fim,
        CancellationToken ct = default)
    {
        if (_sessao.Autenticado && !_sessao.PodeAcessarConta(contaCorrenteId))
        {
            throw new DominioException("Você não tem acesso a esta conta corrente.");
        }

        if (fim.Date < inicio.Date)
        {
            throw new DominioException("O período informado é inválido: a data final é anterior à inicial.");
        }

        // Volume de dados pessoal é pequeno o bastante para calcular saldos em memória,
        // o que mantém a regra de saldo em um único lugar testável (CalculadoraDeSaldo).
        var lancamentos = await _lancamentos.ListarPorContaAsync(contaCorrenteId, ct: ct).ConfigureAwait(false);

        return _calculadora.GerarExtrato(contaCorrenteId, lancamentos, inicio, fim, _relogio.Hoje);
    }
}
