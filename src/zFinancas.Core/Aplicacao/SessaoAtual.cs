using zFinancas.Core.Abstracoes;
using zFinancas.Core.Dominio.Entidades;

namespace zFinancas.Core.Aplicacao;

/// <summary>
/// Estado da sessão em memória. Fica no Core (e não na UI) para que as regras de
/// acesso possam ser reutilizadas e testadas sem MAUI.
/// </summary>
public sealed class SessaoAtual : ISessaoAtual
{
    private readonly Lock _bloqueio = new();

    private Usuario? _usuario;
    private IReadOnlyList<ContaCorrente> _contas = [];
    private long? _contaAtivaId;

    public Usuario? Usuario
    {
        get
        {
            lock (_bloqueio) return _usuario;
        }
    }

    public IReadOnlyList<ContaCorrente> ContasPermitidas
    {
        get
        {
            lock (_bloqueio) return _contas;
        }
    }

    public long? ContaAtivaId
    {
        get
        {
            lock (_bloqueio) return _contaAtivaId;
        }
    }

    public bool Autenticado
    {
        get
        {
            lock (_bloqueio) return _usuario is not null;
        }
    }

    public bool EhAdministrador
    {
        get
        {
            lock (_bloqueio) return _usuario?.EhAdministrador ?? false;
        }
    }

    public void Iniciar(Usuario usuario, IReadOnlyList<ContaCorrente> contas, long? contaAtivaId = null)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        lock (_bloqueio)
        {
            _usuario = usuario;
            _contas = contas ?? [];
            _contaAtivaId = contaAtivaId;

            if (_contaAtivaId is null && _contas.Count > 0)
            {
                _contaAtivaId = _contas.FirstOrDefault(c => c.Principal)?.Id ?? _contas[0].Id;
            }
        }
    }

    public void Encerrar()
    {
        lock (_bloqueio)
        {
            _usuario = null;
            _contas = [];
            _contaAtivaId = null;
        }
    }

    public void DefinirContaAtiva(long? contaId)
    {
        lock (_bloqueio)
        {
            _contaAtivaId = contaId is not null && _contas.Any(c => c.Id == contaId) ? contaId : _contaAtivaId;
        }
    }

    public bool PodeAcessarConta(long contaId)
    {
        lock (_bloqueio)
        {
            return _contas.Any(c => c.Id == contaId);
        }
    }
}
