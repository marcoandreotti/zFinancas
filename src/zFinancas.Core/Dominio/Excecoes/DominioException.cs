namespace zFinancas.Core.Dominio.Excecoes;

/// <summary>
/// Violação de regra de negócio. A UI traduz essa exceção em uma mensagem amigável
/// (nunca em um stack trace), mantendo o domínio independente de detalhes de apresentação.
/// </summary>
public class DominioException : Exception
{
    public DominioException(string mensagem) : base(mensagem)
    {
    }

    public DominioException(string mensagem, Exception interna) : base(mensagem, interna)
    {
    }

    /// <summary>Erros de validação agrupados (campo ? mensagem).</summary>
    public DominioException(IReadOnlyList<string> erros)
        : base(string.Join("\n", erros))
    {
        Erros = erros;
    }

    public IReadOnlyList<string> Erros { get; } = [];
}
