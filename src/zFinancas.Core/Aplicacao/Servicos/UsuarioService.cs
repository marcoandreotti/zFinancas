using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Validacao;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Core.Seguranca;

namespace zFinancas.Core.Aplicacao.Servicos;

/// <summary>
/// Manutenção de usuários (tela de Cadastro de Usuário), incluindo o vínculo com
/// contas correntes (USUARIOCONTACORRENTE).
/// </summary>
public sealed class UsuarioService
{
    private readonly IUsuarioRepositorio _usuarios;
    private readonly IUsuarioContaCorrenteRepositorio _vinculos;
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho;
    private readonly SenhaHasher _hasher;
    private readonly IRelogio _relogio;

    public UsuarioService(
        IUsuarioRepositorio usuarios,
        IUsuarioContaCorrenteRepositorio vinculos,
        IUnidadeDeTrabalho unidadeDeTrabalho,
        SenhaHasher hasher,
        IRelogio relogio)
    {
        _usuarios = usuarios;
        _vinculos = vinculos;
        _unidadeDeTrabalho = unidadeDeTrabalho;
        _hasher = hasher;
        _relogio = relogio;
    }

    public async Task<IReadOnlyList<UsuarioResumo>> ListarAsync(CancellationToken ct = default)
    {
        var usuarios = await _usuarios.ListarAsync(incluirInativos: true, ct).ConfigureAwait(false);
        var vinculos = await _vinculos.ListarVinculosAsync(incluirExcluidos: false, ct).ConfigureAwait(false);

        return usuarios
            .OrderBy(u => u.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new UsuarioResumo(
                u,
                vinculos.Where(v => v.UsuarioId == u.Id)
                        .Select(v => v.ContaCorrenteId)
                        .Distinct()
                        .ToList()))
            .ToList();
    }

    public async Task<UsuarioResumo?> ObterAsync(long id, CancellationToken ct = default)
    {
        var usuario = await _usuarios.ObterPorIdAsync(id, ct).ConfigureAwait(false);
        if (usuario is null) return null;

        var contas = await _vinculos.ListarContasDoUsuarioAsync(id, ct).ConfigureAwait(false);
        return new UsuarioResumo(usuario, contas);
    }

    public async Task<(Usuario Usuario, string CodigoRecuperacao)> CriarAsync(
        RequisicaoUsuario requisicao,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);
        ValidadorSenha.Validar(requisicao.Senha);

        var nome = (requisicao.Nome ?? string.Empty).Trim();
        var agora = _relogio.AgoraUtc;
        var codigoRecuperacao = GeradorCodigoRecuperacao.Gerar();

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            await GarantirNomeUnicoAsync(nome, ignorarId: 0, token).ConfigureAwait(false);

            var usuario = Usuario.Novo(nome, _hasher.Hash(requisicao.Senha!), requisicao.Perfil, agora);
            usuario.DefinirDicaSenha(requisicao.DicaSenha, agora);
            usuario.DefinirCodigoRecuperacao(
                _hasher.Hash(GeradorCodigoRecuperacao.Normalizar(codigoRecuperacao)),
                agora);
            usuario.AlterarAtivo(requisicao.Ativo, agora);

            usuario.Id = await _usuarios.SalvarAsync(usuario, token).ConfigureAwait(false);
            await _vinculos.DefinirContasDoUsuarioAsync(usuario.Id, requisicao.ContasIds ?? [], token)
                .ConfigureAwait(false);

            return (usuario, codigoRecuperacao);
        }, ct).ConfigureAwait(false);
    }

    public async Task<Usuario> AtualizarAsync(RequisicaoUsuario requisicao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        var agora = _relogio.AgoraUtc;

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var usuario = await _usuarios.ObterPorIdAsync(requisicao.Id, token).ConfigureAwait(false)
                          ?? throw new DominioException("Usuário não encontrado.");

            var nome = (requisicao.Nome ?? string.Empty).Trim();
            await GarantirNomeUnicoAsync(nome, ignorarId: usuario.Id, token).ConfigureAwait(false);

            var perdeAdministrador = usuario.EhAdministrador
                                     && (requisicao.Perfil != PerfilUsuario.Administrador || !requisicao.Ativo);

            if (perdeAdministrador)
            {
                await GarantirOutroAdministradorAtivoAsync(usuario.Id, token).ConfigureAwait(false);
            }

            usuario.Nome = nome;
            usuario.Perfil = requisicao.Perfil;
            usuario.Ativo = requisicao.Ativo;
            usuario.DefinirDicaSenha(requisicao.DicaSenha, agora);

            if (!string.IsNullOrWhiteSpace(requisicao.Senha))
            {
                ValidadorSenha.Validar(requisicao.Senha);
                usuario.DefinirSenha(_hasher.Hash(requisicao.Senha), agora);
            }

            usuario.MarcarAlteracao(agora);
            usuario.Validar();
            await _usuarios.SalvarAsync(usuario, token).ConfigureAwait(false);

            await _vinculos.DefinirContasDoUsuarioAsync(usuario.Id, requisicao.ContasIds ?? [], token)
                .ConfigureAwait(false);

            return usuario;
        }, ct).ConfigureAwait(false);
    }

    public async Task AlterarAtivoAsync(long usuarioId, bool ativo, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;

        await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var usuario = await _usuarios.ObterPorIdAsync(usuarioId, token).ConfigureAwait(false)
                          ?? throw new DominioException("Usuário não encontrado.");

            if (!ativo && usuario.EhAdministrador)
            {
                await GarantirOutroAdministradorAtivoAsync(usuario.Id, token).ConfigureAwait(false);
            }

            usuario.AlterarAtivo(ativo, agora);
            await _usuarios.SalvarAsync(usuario, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Reset administrativo: devolve senha temporária e novo código de recuperação.</summary>
    public async Task<(string SenhaTemporaria, string CodigoRecuperacao)> RedefinirSenhaAsync(
        long usuarioId,
        CancellationToken ct = default)
    {
        var usuario = await _usuarios.ObterPorIdAsync(usuarioId, ct).ConfigureAwait(false)
                      ?? throw new DominioException("Usuário não encontrado.");

        var senhaTemporaria = GeradorCodigoRecuperacao.Gerar().Replace("-", string.Empty)[..8];
        var codigoRecuperacao = GeradorCodigoRecuperacao.Gerar();
        var agora = _relogio.AgoraUtc;

        usuario.DefinirSenha(_hasher.Hash(senhaTemporaria), agora);
        usuario.DefinirCodigoRecuperacao(
            _hasher.Hash(GeradorCodigoRecuperacao.Normalizar(codigoRecuperacao)),
            agora);

        await _usuarios.SalvarAsync(usuario, ct).ConfigureAwait(false);
        return (senhaTemporaria, codigoRecuperacao);
    }

    private async Task GarantirNomeUnicoAsync(string nome, long ignorarId, CancellationToken ct)
    {
        var existentes = await _usuarios.ListarAsync(incluirInativos: true, ct).ConfigureAwait(false);

        var conflito = existentes.Any(u =>
            u.Id != ignorarId &&
            string.Equals(u.Nome, nome, StringComparison.CurrentCultureIgnoreCase));

        if (conflito)
        {
            throw new DominioException($"Já existe um usuário com o nome '{nome}'.");
        }
    }

    private async Task GarantirOutroAdministradorAtivoAsync(long ignorarId, CancellationToken ct)
    {
        var usuarios = await _usuarios.ListarAsync(incluirInativos: false, ct).ConfigureAwait(false);

        var outrosAdmins = usuarios.Count(u =>
            u.Id != ignorarId &&
            u.EhAdministrador &&
            u.Ativo);

        if (outrosAdmins == 0)
        {
            throw new DominioException("O sistema precisa de ao menos um administrador ativo.");
        }
    }
}
