namespace zFinancas.Core.Aplicacao.Modelos;

/// <summary>
/// Pacote de sincronização trocado entre PC e celular (arquivo <c>.zfsync</c>, JSON).
/// <para>
/// Todas as referências entre entidades usam <b>UUID</b>, nunca o Id local, porque os
/// autoincrementos de cada dispositivo são independentes.
/// </para>
/// <para>
/// Segurança: o pacote contém hashes de senha (nunca senhas em texto claro) para que o
/// mesmo usuário consiga entrar nos dois aparelhos. Trate o arquivo como sensível e
/// transfira apenas por canal confiável (cabo, rede local, armazenamento próprio).
/// </para>
/// </summary>
public sealed class PacoteSincronizacao
{
    public const int VersaoAtual = 1;

    public int Versao { get; set; } = VersaoAtual;

    public string DispositivoId { get; set; } = string.Empty;

    public string DispositivoNome { get; set; } = string.Empty;

    public DateTime GeradoEmUtc { get; set; }

    public List<ContaCorrenteSync> Contas { get; set; } = [];

    public List<CentroCustoSync> CentrosDeCusto { get; set; } = [];

    public List<UsuarioSync> Usuarios { get; set; } = [];

    public List<VinculoSync> Vinculos { get; set; } = [];

    public List<LancamentoSync> Lancamentos { get; set; } = [];

    public int TotalRegistros => Contas.Count + CentrosDeCusto.Count + Usuarios.Count + Vinculos.Count + Lancamentos.Count;
}

public sealed class ContaCorrenteSync
{
    public string Uuid { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public bool Principal { get; set; }
    public bool Ativo { get; set; }
    public string CorHex { get; set; } = "#6C5BFF";
    public string Icone { get; set; } = "🏦";
    public int Ordem { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public bool Excluido { get; set; }
}

public sealed class CentroCustoSync
{
    public string Uuid { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public bool Principal { get; set; }
    public string Icone { get; set; } = "🧾";
    public bool Ativo { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public bool Excluido { get; set; }
}

public sealed class UsuarioSync
{
    public string Uuid { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;

    /// <summary>Hash PBKDF2 da senha é permite entrar com a mesma senha nos dois aparelhos.</summary>
    public string SenhaHash { get; set; } = string.Empty;

    public string? DicaSenha { get; set; }
    public string Perfil { get; set; } = "USR";
    public bool Ativo { get; set; }
    public DateTime DataCadastro { get; set; }
    public DateTime? UltimoAcessoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public bool Excluido { get; set; }
}

public sealed class VinculoSync
{
    public string UsuarioUuid { get; set; } = string.Empty;
    public string ContaUuid { get; set; } = string.Empty;
    public DateTime AtualizadoEm { get; set; }
    public bool Excluido { get; set; }
}

public sealed class LancamentoSync
{
    public string Uuid { get; set; } = string.Empty;
    public string ContaUuid { get; set; } = string.Empty;
    public string CentroCustoUuid { get; set; } = string.Empty;
    public DateTime DataLancamento { get; set; }
    public DateTime DataVencimento { get; set; }
    public DateTime? DataCompensado { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public bool Compensado { get; set; }
    public long Agrupador { get; set; }
    public string? AgrupadorUuid { get; set; }
    public long Parcela { get; set; }
    public long QuantidadeParcelas { get; set; }
    public string? Observacao { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public bool Excluido { get; set; }
}

/// <summary>Resumo do que foi exportado/importado, exibido na tela de Sincronização.</summary>
public sealed record ResultadoSincronizacao(
    int Contas,
    int CentrosDeCusto,
    int Usuarios,
    int Vinculos,
    int Lancamentos,
    IReadOnlyList<string> Avisos)
{
    public int Total => Contas + CentrosDeCusto + Usuarios + Vinculos + Lancamentos;
}
