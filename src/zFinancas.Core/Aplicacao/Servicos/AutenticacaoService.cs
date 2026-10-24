using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Validacao;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Core.Seguranca;

namespace zFinancas.Core.Aplicacao.Servicos;

/// <summary>
/// Login e sessão. Concentra as regras de acesso:
/// <list type="bullet">
/// <item>usuário inativo não entra e, portanto, não acessa nenhuma conta relacionada;</item>
/// <item>ADM enxerga todas as contas ativas;</item>
/// <item>USR enxerga apenas as contas vinculadas em USUARIOCONTACORRENTE.</item>
/// </list>
/// </summary>
public sealed class AutenticacaoService
{
    private readonly IUsuarioRepositorio _usuarios;
    private readonly IContaCorrenteRepositorio _contas;
    private readonly IUsuarioContaCorrenteRepositorio _vinculos;
    private readonly ISessaoAtual _sessao;
    private readonly SenhaHasher _hasher;
    private readonly IRelogio _relogio;

    public AutenticacaoService(
        IUsuarioRepositorio usuarios,
        IContaCorrenteRepositorio contas,
        IUsuarioContaCorrenteRepositorio vinculos,
        ISessaoAtual sessao,
        SenhaHasher hasher,
        IRelogio relogio)
    {
        _usuarios = usuarios;
        _contas = contas;
        _vinculos = vinculos;
        _sessao = sessao;
        _hasher = hasher;
        _relogio = relogio;
    }

    /// <summary>Usuários disponíveis na tela de entrada (apenas ativos).</summary>
    public async Task<IReadOnlyList<Usuario>> ListarUsuariosAsync(CancellationToken ct = default)
    {
        var usuarios = await _usuarios.ListarAsync(incluirInativos: false, ct).ConfigureAwait(false);
        return usuarios.OrderBy(u => u.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<Usuario> EntrarAsync(long usuarioId, string senha, CancellationToken ct = default)
    {
        var usuario = await ObterUsuarioAtivoAsync(usuarioId, ct).ConfigureAwait(false);

        if (!_hasher.Verificar(senha, usuario.SenhaHash))
        {
            throw new DominioException("Senha incorreta.");
        }

        // Atualiza o hash silenciosamente se o custo de iteração aumentou numa versão nova.
        if (_hasher.PrecisaRehash(usuario.SenhaHash))
        {
            usuario.DefinirSenha(_hasher.Hash(senha), _relogio.AgoraUtc);
        }

        return await ConcluirLoginAsync(usuario, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Entrada por biometria: a autenticação do dispositivo já aconteceu, então aqui
    /// apenas validamos que o usuário continua ativo e montamos a sessão.
    /// </summary>
    public async Task<Usuario> EntrarComBiometriaAsync(long usuarioId, CancellationToken ct = default)
    {
        var usuario = await ObterUsuarioAtivoAsync(usuarioId, ct).ConfigureAwait(false);
        return await ConcluirLoginAsync(usuario, ct).ConfigureAwait(false);
    }

    public void Sair() => _sessao.Encerrar();

    /// <summary>Recuperação autoatendida por código de recuperação (uso único, regenerado depois).</summary>
    public async Task<string> RecuperarSenhaAsync(
        string nome,
        string codigoRecuperacao,
        string novaSenha,
        CancellationToken ct = default)
    {
        ValidadorSenha.Validar(novaSenha);

        var usuario = await _usuarios.ObterPorNomeAsync((nome ?? string.Empty).Trim(), ct).ConfigureAwait(false);
        if (usuario is null)
        {
            throw new DominioException("Usuário não encontrado.");
        }

        if (!usuario.Ativo)
        {
            throw new DominioException("Usuário inativo. Procure um administrador.");
        }

        var codigoNormalizado = GeradorCodigoRecuperacao.Normalizar(codigoRecuperacao);
        if (string.IsNullOrWhiteSpace(usuario.CodigoRecuperacaoHash)
            || !_hasher.Verificar(codigoNormalizado, usuario.CodigoRecuperacaoHash))
        {
            throw new DominioException("Código de recuperação inválido.");
        }

        var novoCodigo = GeradorCodigoRecuperacao.Gerar();
        usuario.DefinirSenha(_hasher.Hash(novaSenha), _relogio.AgoraUtc);
        usuario.DefinirCodigoRecuperacao(
            _hasher.Hash(GeradorCodigoRecuperacao.Normalizar(novoCodigo)),
            _relogio.AgoraUtc);

        await _usuarios.SalvarAsync(usuario, ct).ConfigureAwait(false);
        return novoCodigo;
    }

    private async Task<Usuario> ObterUsuarioAtivoAsync(long usuarioId, CancellationToken ct)
    {
        var usuario = await _usuarios.ObterPorIdAsync(usuarioId, ct).ConfigureAwait(false)
                      ?? throw new DominioException("Usuário não encontrado.");

        if (usuario.Excluido)
        {
            throw new DominioException("Usuário não encontrado.");
        }

        if (!usuario.Ativo)
        {
            throw new DominioException("Usuário inativo não pode acessar o sistema.");
        }

        return usuario;
    }

    private async Task<Usuario> ConcluirLoginAsync(Usuario usuario, CancellationToken ct)
    {
        var contasPermitidas = await ResolverContasPermitidasAsync(usuario, ct).ConfigureAwait(false);

        var contaAtiva = contasPermitidas.FirstOrDefault(c => c.Principal)?.Id
                         ?? contasPermitidas.FirstOrDefault()?.Id;

        _sessao.Iniciar(usuario, contasPermitidas, contaAtiva);

        usuario.RegistrarAcesso(_relogio.AgoraUtc);
        await _usuarios.SalvarAsync(usuario, ct).ConfigureAwait(false);

        return usuario;
    }

    private async Task<IReadOnlyList<ContaCorrente>> ResolverContasPermitidasAsync(Usuario usuario, CancellationToken ct)
    {
        var contasAtivas = (await _contas.ListarAsync(incluirInativas: false, ct).ConfigureAwait(false))
            .OrderBy(c => c.Ordem)
            .ThenBy(c => c.Descricao, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (usuario.EhAdministrador)
        {
            return contasAtivas;
        }

        var vinculadas = await _vinculos.ListarContasDoUsuarioAsync(usuario.Id, ct).ConfigureAwait(false);
        var permitidas = new HashSet<long>(vinculadas);

        return contasAtivas.Where(c => permitidas.Contains(c.Id)).ToList();
    }
}
