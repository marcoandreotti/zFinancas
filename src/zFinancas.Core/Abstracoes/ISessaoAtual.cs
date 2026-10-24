using zFinancas.Core.Dominio.Entidades;

namespace zFinancas.Core.Abstracoes;

/// <summary>
/// Sessão do usuário autenticado. Carrega as contas permitidas, já resolvendo a
/// regra do perfil USR (só acessa o que estiver em USUARIOCONTACORRENTE) e do
/// perfil ADM (acessa todas as contas ativas).
/// </summary>
public interface ISessaoAtual
{
    Usuario? Usuario { get; }

    IReadOnlyList<ContaCorrente> ContasPermitidas { get; }

    long? ContaAtivaId { get; }

    bool Autenticado { get; }

    bool EhAdministrador { get; }

    void Iniciar(Usuario usuario, IReadOnlyList<ContaCorrente> contas, long? contaAtivaId = null);

    void Encerrar();

    void DefinirContaAtiva(long? contaId);

    bool PodeAcessarConta(long contaId);
}
