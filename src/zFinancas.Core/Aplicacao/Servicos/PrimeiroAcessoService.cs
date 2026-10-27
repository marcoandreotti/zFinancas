using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Validacao;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Excecoes;
using zFinancas.Core.Seguranca;

namespace zFinancas.Core.Aplicacao.Servicos;

/// <summary>
/// Primeiro acesso: quando o banco está vazio, o app exige a criação do usuário ADM
/// e já semeia uma conta corrente e um centro de custo principais para o app ser usável na hora.
/// </summary>
public sealed class PrimeiroAcessoService
{
    private readonly IUsuarioRepositorio _usuarios;
    private readonly IContaCorrenteRepositorio _contas;
    private readonly ICentroCustoRepositorio _centrosDeCusto;
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho;
    private readonly SenhaHasher _hasher;
    private readonly IRelogio _relogio;

    public PrimeiroAcessoService(
        IUsuarioRepositorio usuarios,
        IContaCorrenteRepositorio contas,
        ICentroCustoRepositorio centrosDeCusto,
        IUnidadeDeTrabalho unidadeDeTrabalho,
        SenhaHasher hasher,
        IRelogio relogio)
    {
        _usuarios = usuarios;
        _contas = contas;
        _centrosDeCusto = centrosDeCusto;
        _unidadeDeTrabalho = unidadeDeTrabalho;
        _hasher = hasher;
        _relogio = relogio;
    }

    /// <summary>Verdadeiro quando ainda não existe nenhum usuário cadastrado.</summary>
    public async Task<bool> PrecisaConfigurarAsync(CancellationToken ct = default)
        => !await _usuarios.ExisteAlgumAsync(ct).ConfigureAwait(false);

    public async Task<ResultadoConfiguracaoInicial> ConfigurarAsync(
        RequisicaoConfiguracaoInicial requisicao,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        ValidadorSenha.Validar(requisicao.Senha);

        var nome = (requisicao.NomeAdministrador ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new DominioException("Informe o nome do administrador.");
        }

        var agora = _relogio.AgoraUtc;
        var codigoRecuperacao = GeradorCodigoRecuperacao.Gerar();

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            if (await _usuarios.ExisteAlgumAsync(token).ConfigureAwait(false))
            {
                throw new DominioException("A configuração inicial já foi concluída.");
            }

            var administrador = Usuario.Novo(nome, _hasher.Hash(requisicao.Senha), PerfilUsuario.Administrador, agora);
            administrador.DefinirDicaSenha(requisicao.DicaSenha, agora);
            administrador.DefinirCodigoRecuperacao(
                _hasher.Hash(GeradorCodigoRecuperacao.Normalizar(codigoRecuperacao)),
                agora);

            var administradorId = await _usuarios.SalvarAsync(administrador, token).ConfigureAwait(false);
            administrador.Id = administradorId;

            // A base nasce vazia de propósito: o ADM cria as contas e centros que quiser
            // (evita "Conta Principal"/"Geral" fantasma depois de começar do zero).
            return new ResultadoConfiguracaoInicial(administrador, codigoRecuperacao);
        }, ct).ConfigureAwait(false);
    }
}
