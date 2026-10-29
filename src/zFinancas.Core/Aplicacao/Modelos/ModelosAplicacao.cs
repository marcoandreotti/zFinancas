using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Servicos;

namespace zFinancas.Core.Aplicacao.Modelos;

/// <summary>Cartão de conta exibido no carrossel do dashboard.</summary>
public sealed record ContaResumo(
    long Id,
    string Descricao,
    string CorHex,
    string Icone,
    bool Principal,
    SaldoConta Saldo);

/// <summary>Dados do dashboard: contas, totais e últimos lançamentos.</summary>
public sealed record ResumoDashboard(
    IReadOnlyList<ContaResumo> Contas,
    SaldoConta Total,
    IReadOnlyList<Lancamento> UltimosLancamentos,
    DateTime InicioJanela,
    DateTime FimJanela)
{
    public decimal EntradasNaJanela => UltimosLancamentos.Where(l => l.Valor > 0m).Sum(l => l.Valor);
    public decimal SaidasNaJanela => UltimosLancamentos.Where(l => l.Valor < 0m).Sum(l => l.Valor);
}

/// <summary>Dados do primeiro acesso (criação do ADM).</summary>
public sealed record RequisicaoConfiguracaoInicial(string NomeAdministrador, string Senha, string? DicaSenha = null);

public sealed record ResultadoConfiguracaoInicial(
    Usuario Administrador,
    string CodigoRecuperacao);

/// <summary>Cadastro/edição de usuário, já com a seleção de contas (USUARIOCONTACORRENTE).</summary>
public sealed record RequisicaoUsuario(
    long Id,
    string Nome,
    string? Senha,
    string? DicaSenha,
    PerfilUsuario Perfil,
    bool Ativo,
    IReadOnlyCollection<long> ContasIds);

public sealed record UsuarioResumo(
    Usuario Usuario,
    IReadOnlyList<long> ContasIds);

public sealed record RequisicaoConta(long Id, string Descricao, string CorHex, string Icone, bool Principal, bool Ativo);

public sealed record RequisicaoCentroCusto(long Id, string Descricao, string Icone, bool Principal, bool Ativo);

/// <summary>
/// Lançamento a criar/editar. <see cref="Valor"/> já chega com sinal (negativo = despesa).
/// Quando <see cref="Recorrencia"/> é diferente de <see cref="Recorrencia.Unica"/>,
/// o serviço gera <see cref="QuantidadeParcelas"/> lançamentos com o mesmo AGRUPADOR.
/// </summary>
public sealed record RequisicaoLancamento(
    long Id,
    long ContaCorrenteId,
    long CentroCustoId,
    string Descricao,
    decimal Valor,
    DateTime DataVencimento,
    Recorrencia Recorrencia,
    int QuantidadeParcelas,
    bool Compensado,
    DateTime? DataCompensado,
    string? Observacao,
    DateTime? DataLancamento = null);

public sealed record ResultadoCriacaoLancamentos(IReadOnlyList<Lancamento> Lancamentos, long Agrupador);

/// <summary>Totais de uma série de parcelas: o que já foi pago, o que falta e o total.</summary>
public sealed record TotaisDaSerie(
    decimal Total,
    decimal Pago,
    decimal APagar,
    int Parcelas,
    int Compensadas)
{
    public bool TemParcelas => Parcelas > 0;
}

/// <summary>Erro de validação apresentável ao usuário.</summary>
public sealed record MensagemValidacao(string Mensagem);
