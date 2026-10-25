using System.Globalization;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Enums;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.Core.Aplicacao.Servicos;

/// <summary>
/// Sincronização offline entre PC e celular por pacote JSON.
/// <para>
/// Estratégia: cada entidade carrega <c>Uuid</c> (identidade global), <c>AtualizadoEm</c>
/// (UTC) e <c>Excluido</c> (lápide). A mesclagem é "última escrita vence" por registro,
/// e as referências entre entidades são resolvidas por UUID é o que torna o processo
/// idempotente e seguro para importar o mesmo pacote mais de uma vez.
/// </para>
/// </summary>
public sealed class SincronizacaoService
{
    private const string ChaveDispositivoId = "dispositivo.id";
    private const string ChaveUltimaSincronizacao = "sincronizacao.ultima";

    private readonly IUsuarioRepositorio _usuarios;
    private readonly IContaCorrenteRepositorio _contas;
    private readonly ICentroCustoRepositorio _centrosDeCusto;
    private readonly ILancamentoRepositorio _lancamentos;
    private readonly IUsuarioContaCorrenteRepositorio _vinculos;
    private readonly IConfiguracaoRepositorio _configuracoes;
    private readonly ISerializadorJson _serializador;
    private readonly IInfoDispositivo _dispositivo;
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho;
    private readonly IRelogio _relogio;

    public SincronizacaoService(
        IUsuarioRepositorio usuarios,
        IContaCorrenteRepositorio contas,
        ICentroCustoRepositorio centrosDeCusto,
        ILancamentoRepositorio lancamentos,
        IUsuarioContaCorrenteRepositorio vinculos,
        IConfiguracaoRepositorio configuracoes,
        ISerializadorJson serializador,
        IInfoDispositivo dispositivo,
        IUnidadeDeTrabalho unidadeDeTrabalho,
        IRelogio relogio)
    {
        _usuarios = usuarios;
        _contas = contas;
        _centrosDeCusto = centrosDeCusto;
        _lancamentos = lancamentos;
        _vinculos = vinculos;
        _configuracoes = configuracoes;
        _serializador = serializador;
        _dispositivo = dispositivo;
        _unidadeDeTrabalho = unidadeDeTrabalho;
        _relogio = relogio;
    }

    // ------------------------------------------------------------------ exportação

    public async Task<PacoteSincronizacao> MontarPacoteAsync(CancellationToken ct = default)
    {
        var contas = await _contas.ListarTodosIncluindoExcluidosAsync(ct).ConfigureAwait(false);
        var centros = await _centrosDeCusto.ListarTodosIncluindoExcluidosAsync(ct).ConfigureAwait(false);
        var usuarios = await _usuarios.ListarTodosIncluindoExcluidosAsync(ct).ConfigureAwait(false);
        var lancamentos = await _lancamentos.ListarTodosIncluindoExcluidosAsync(ct).ConfigureAwait(false);
        var vinculos = await _vinculos.ListarVinculosAsync(incluirExcluidos: true, ct).ConfigureAwait(false);

        var contaPorId = contas.ToDictionary(c => c.Id, c => c.Uuid);
        var centroPorId = centros.ToDictionary(c => c.Id, c => c.Uuid);
        var usuarioPorId = usuarios.ToDictionary(u => u.Id, u => u.Uuid);

        var pacote = new PacoteSincronizacao
        {
            Versao = PacoteSincronizacao.VersaoAtual,
            DispositivoId = await ObterDispositivoIdAsync(ct).ConfigureAwait(false),
            DispositivoNome = _dispositivo.Nome,
            GeradoEmUtc = _relogio.AgoraUtc,
            Contas = contas.Select(c => new ContaCorrenteSync
            {
                Uuid = c.Uuid,
                Descricao = c.Descricao,
                Principal = c.Principal,
                Ativo = c.Ativo,
                CorHex = c.CorHex,
                Icone = c.Icone,
                Ordem = c.Ordem,
                AtualizadoEm = c.AtualizadoEm,
                Excluido = c.Excluido
            }).ToList(),
            CentrosDeCusto = centros.Select(c => new CentroCustoSync
            {
                Uuid = c.Uuid,
                Descricao = c.Descricao,
                Principal = c.Principal,
                Icone = c.Icone,
                Ativo = c.Ativo,
                AtualizadoEm = c.AtualizadoEm,
                Excluido = c.Excluido
            }).ToList(),
            Usuarios = usuarios.Select(u => new UsuarioSync
            {
                Uuid = u.Uuid,
                Nome = u.Nome,
                SenhaHash = u.SenhaHash,
                DicaSenha = u.DicaSenha,
                Perfil = u.Perfil.ParaCodigo(),
                Ativo = u.Ativo,
                DataCadastro = u.DataCadastro,
                UltimoAcessoEm = u.UltimoAcessoEm,
                AtualizadoEm = u.AtualizadoEm,
                Excluido = u.Excluido
            }).ToList(),
            Vinculos = vinculos
                .Where(v => usuarioPorId.ContainsKey(v.UsuarioId) && contaPorId.ContainsKey(v.ContaCorrenteId))
                .Select(v => new VinculoSync
                {
                    UsuarioUuid = usuarioPorId[v.UsuarioId],
                    ContaUuid = contaPorId[v.ContaCorrenteId],
                    AtualizadoEm = v.AtualizadoEm,
                    Excluido = v.Excluido
                }).ToList(),
            Lancamentos = lancamentos
                .Where(l => contaPorId.ContainsKey(l.ContaCorrenteId) && centroPorId.ContainsKey(l.CentroCustoId))
                .Select(l => new LancamentoSync
                {
                    Uuid = l.Uuid,
                    ContaUuid = contaPorId[l.ContaCorrenteId],
                    CentroCustoUuid = centroPorId[l.CentroCustoId],
                    DataLancamento = l.DataLancamento,
                    DataVencimento = l.DataVencimento,
                    DataCompensado = l.DataCompensado,
                    Descricao = l.Descricao,
                    Valor = l.Valor,
                    Compensado = l.Compensado,
                    Agrupador = l.Agrupador,
                    AgrupadorUuid = l.AgrupadorUuid,
                    Parcela = l.Parcela,
                    QuantidadeParcelas = l.QuantidadeParcelas,
                    Observacao = l.Observacao,
                    AtualizadoEm = l.AtualizadoEm,
                    Excluido = l.Excluido
                }).ToList()
        };

        return pacote;
    }

    public async Task<string> ExportarTextoAsync(CancellationToken ct = default)
    {
        var pacote = await MontarPacoteAsync(ct).ConfigureAwait(false);
        return _serializador.Serializar(pacote);
    }

    /// <summary>
    /// Abre o “Salvar como” do sistema e devolve o caminho escolhido
    /// (ou null quando o usuário cancela).
    /// </summary>
    public async Task<string?> ExportarAsync(IArmazenamentoCompartilhado armazenamento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(armazenamento);

        var pacote = await MontarPacoteAsync(ct).ConfigureAwait(false);
        var json = _serializador.Serializar(pacote);
        var nomeArquivo = $"zfinancas-{pacote.GeradoEmUtc.ToLocalTime():yyyyMMdd-HHmm}.zfsync";

        return await armazenamento
            .SalvarTextoAsync(nomeArquivo, json, "Pacote de sincronização do zFinancas", ct)
            .ConfigureAwait(false);
    }


    // ------------------------------------------------------------------ importação

    public async Task<ResultadoSincronizacao> ImportarTextoAsync(string json, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new DominioException("O arquivo de sincronização está vazio.");
        }

        PacoteSincronizacao? pacote;
        try
        {
            pacote = _serializador.Desserializar<PacoteSincronizacao>(json);
        }
        catch (Exception ex)
        {
            throw new DominioException("Não foi possível ler o arquivo de sincronização. Verifique se é um pacote do zFinancas.", ex);
        }

        if (pacote is null)
        {
            throw new DominioException("Arquivo de sincronização inválido.");
        }

        if (pacote.Versao > PacoteSincronizacao.VersaoAtual)
        {
            throw new DominioException(
                $"O pacote foi gerado por uma versão mais nova do zFinancas (versão {pacote.Versao}). Atualize este aplicativo.");
        }

        return await ImportarAsync(pacote, ct).ConfigureAwait(false);
    }

    public async Task<ResultadoSincronizacao> ImportarAsync(PacoteSincronizacao pacote, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pacote);

        var avisos = new List<string>();

        return await _unidadeDeTrabalho.ExecutarAsync(async token =>
        {
            var contasInseridas = 0; var contasAtualizadas = 0;
            var centrosInseridos = 0; var centrosAtualizados = 0;
            var usuariosInseridos = 0; var usuariosAtualizados = 0;
            var vinculosAplicados = 0;
            var lancamentosInseridos = 0; var lancamentosAtualizados = 0;

            // ---------------------------------------------------------- contas
            var contasLocais = (await _contas.ListarTodosIncluindoExcluidosAsync(token).ConfigureAwait(false))
                .ToDictionary(c => c.Uuid, StringComparer.OrdinalIgnoreCase);
            var contaIdPorUuid = contasLocais.ToDictionary(p => p.Key, p => p.Value.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var remoto in pacote.Contas.Where(c => !string.IsNullOrWhiteSpace(c.Uuid)))
            {
                if (contasLocais.TryGetValue(remoto.Uuid, out var local))
                {
                    if (remoto.AtualizadoEm <= local.AtualizadoEm) continue;

                    local.Descricao = remoto.Descricao;
                    local.Principal = remoto.Principal;
                    local.Ativo = remoto.Ativo;
                    local.CorHex = remoto.CorHex;
                    local.Icone = remoto.Icone;
                    local.Ordem = remoto.Ordem;
                    local.AtualizadoEm = remoto.AtualizadoEm;
                    local.Excluido = remoto.Excluido;

                    await _contas.SalvarAsync(local, token).ConfigureAwait(false);
                    contasAtualizadas++;
                    continue;
                }

                if (remoto.Excluido) continue;

                // Bases criadas separadamente geram o mesmo nome com UUIDs diferentes
                // (ex.: "Conta Principal" nos dois aparelhos). Como DESCRICAO e unica,
                // unificamos: mantemos a conta local e adotamos o UUID remoto, para que
                // as proximas sincronizacoes ja se reconheçam sem aviso.
                var homonima = contasLocais.Values.FirstOrDefault(c =>
                    !c.Excluido && string.Equals(c.Descricao, remoto.Descricao, StringComparison.OrdinalIgnoreCase));

                if (homonima is not null)
                {
                    homonima.Uuid = remoto.Uuid;
                    homonima.AtualizadoEm = remoto.AtualizadoEm;

                    await _contas.SalvarAsync(homonima, token).ConfigureAwait(false);

                    contaIdPorUuid[remoto.Uuid] = homonima.Id;
                    contasAtualizadas++;
                    avisos.Add($"Conta \"{remoto.Descricao}\" existia nos dois aparelhos: unificada.");
                    continue;
                }

                var nova = new ContaCorrente
                {
                    Uuid = remoto.Uuid,
                    Descricao = remoto.Descricao,
                    Principal = remoto.Principal,
                    Ativo = remoto.Ativo,
                    CorHex = remoto.CorHex,
                    Icone = remoto.Icone,
                    Ordem = remoto.Ordem,
                    AtualizadoEm = remoto.AtualizadoEm,
                    Excluido = false
                };
                nova.Validar();
                nova.Id = await _contas.SalvarAsync(nova, token).ConfigureAwait(false);
                contaIdPorUuid[nova.Uuid] = nova.Id;
                contasInseridas++;
            }

            // ---------------------------------------------------- centros de custo
            var centrosLocais = (await _centrosDeCusto.ListarTodosIncluindoExcluidosAsync(token).ConfigureAwait(false))
                .ToDictionary(c => c.Uuid, StringComparer.OrdinalIgnoreCase);
            var centroIdPorUuid = centrosLocais.ToDictionary(p => p.Key, p => p.Value.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var remoto in pacote.CentrosDeCusto.Where(c => !string.IsNullOrWhiteSpace(c.Uuid)))
            {
                if (centrosLocais.TryGetValue(remoto.Uuid, out var local))
                {
                    if (remoto.AtualizadoEm <= local.AtualizadoEm) continue;

                    local.Descricao = remoto.Descricao;
                    local.Principal = remoto.Principal;
                    local.Icone = remoto.Icone;
                    local.Ativo = remoto.Ativo;
                    local.AtualizadoEm = remoto.AtualizadoEm;
                    local.Excluido = remoto.Excluido;

                    await _centrosDeCusto.SalvarAsync(local, token).ConfigureAwait(false);
                    centrosAtualizados++;
                    continue;
                }

                if (remoto.Excluido) continue;

                var homonimo = centrosLocais.Values.FirstOrDefault(c =>
                    !c.Excluido && string.Equals(c.Descricao, remoto.Descricao, StringComparison.OrdinalIgnoreCase));

                if (homonimo is not null)
                {
                    homonimo.Uuid = remoto.Uuid;
                    homonimo.AtualizadoEm = remoto.AtualizadoEm;

                    await _centrosDeCusto.SalvarAsync(homonimo, token).ConfigureAwait(false);

                    centroIdPorUuid[remoto.Uuid] = homonimo.Id;
                    centrosAtualizados++;
                    avisos.Add($"Centro de custo \"{remoto.Descricao}\" existia nos dois aparelhos: unificado.");
                    continue;
                }

                var novo = new CentroCusto
                {
                    Uuid = remoto.Uuid,
                    Descricao = remoto.Descricao,
                    Principal = remoto.Principal,
                    Icone = remoto.Icone,
                    Ativo = remoto.Ativo,
                    AtualizadoEm = remoto.AtualizadoEm,
                    Excluido = false
                };
                novo.Validar();
                novo.Id = await _centrosDeCusto.SalvarAsync(novo, token).ConfigureAwait(false);
                centroIdPorUuid[novo.Uuid] = novo.Id;
                centrosInseridos++;
            }

            // ------------------------------------------------------------ usuários
            var usuariosLocais = (await _usuarios.ListarTodosIncluindoExcluidosAsync(token).ConfigureAwait(false))
                .ToDictionary(u => u.Uuid, StringComparer.OrdinalIgnoreCase);
            var usuarioIdPorUuid = usuariosLocais.ToDictionary(p => p.Key, p => p.Value.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var remoto in pacote.Usuarios.Where(u => !string.IsNullOrWhiteSpace(u.Uuid)))
            {
                if (usuariosLocais.TryGetValue(remoto.Uuid, out var local))
                {
                    if (remoto.AtualizadoEm <= local.AtualizadoEm) continue;

                    local.Nome = remoto.Nome;
                    local.SenhaHash = string.IsNullOrWhiteSpace(remoto.SenhaHash) ? local.SenhaHash : remoto.SenhaHash;
                    local.DicaSenha = remoto.DicaSenha;
                    local.Perfil = PerfilDe(remoto.Perfil);
                    local.Ativo = remoto.Ativo;
                    local.UltimoAcessoEm = remoto.UltimoAcessoEm;
                    local.AtualizadoEm = remoto.AtualizadoEm;
                    local.Excluido = remoto.Excluido;

                    await _usuarios.SalvarAsync(local, token).ConfigureAwait(false);
                    usuariosAtualizados++;
                    continue;
                }

                if (remoto.Excluido) continue;

                var homonimo = usuariosLocais.Values.FirstOrDefault(u =>
                    !u.Excluido && string.Equals(u.Nome, remoto.Nome, StringComparison.OrdinalIgnoreCase));

                if (homonimo is not null)
                {
                    homonimo.Uuid = remoto.Uuid;
                    homonimo.AtualizadoEm = remoto.AtualizadoEm;

                    await _usuarios.SalvarAsync(homonimo, token).ConfigureAwait(false);

                    usuarioIdPorUuid[remoto.Uuid] = homonimo.Id;
                    usuariosAtualizados++;
                    avisos.Add($"Usuario \"{remoto.Nome}\" existia nos dois aparelhos: unificado (senha local mantida).");
                    continue;
                }

                var novo = new Usuario
                {
                    Uuid = remoto.Uuid,
                    Nome = remoto.Nome,
                    SenhaHash = string.IsNullOrWhiteSpace(remoto.SenhaHash) ? "-" : remoto.SenhaHash,
                    DicaSenha = remoto.DicaSenha,
                    Perfil = PerfilDe(remoto.Perfil),
                    Ativo = remoto.Ativo,
                    DataCadastro = remoto.DataCadastro == default ? _relogio.AgoraUtc : remoto.DataCadastro,
                    UltimoAcessoEm = remoto.UltimoAcessoEm,
                    AtualizadoEm = remoto.AtualizadoEm,
                    Excluido = false
                };

                // O código de recuperação nunca é sincronizado: cada aparelho mantém o seu.
                novo.Id = await _usuarios.SalvarAsync(novo, token).ConfigureAwait(false);
                usuarioIdPorUuid[novo.Uuid] = novo.Id;
                usuariosInseridos++;
            }

            // ------------------------------------------------------------ vínculos
            var vinculosLocais = (await _vinculos.ListarVinculosAsync(incluirExcluidos: true, token).ConfigureAwait(false))
                .ToDictionary(v => (v.UsuarioId, v.ContaCorrenteId));

            foreach (var remoto in pacote.Vinculos)
            {
                if (!usuarioIdPorUuid.TryGetValue(remoto.UsuarioUuid, out var usuarioId) ||
                    !contaIdPorUuid.TryGetValue(remoto.ContaUuid, out var contaId))
                {
                    avisos.Add($"Vínculo ignorado: usuário/conta não encontrado ({remoto.UsuarioUuid} ? {remoto.ContaUuid}).");
                    continue;
                }

                if (vinculosLocais.TryGetValue((usuarioId, contaId), out var local))
                {
                    if (remoto.AtualizadoEm <= local.AtualizadoEm) continue;

                    local.Excluido = remoto.Excluido;
                    local.AtualizadoEm = remoto.AtualizadoEm;
                    await _vinculos.SalvarVinculosAsync([local], token).ConfigureAwait(false);
                    vinculosAplicados++;
                    continue;
                }

                if (remoto.Excluido) continue;

                var novo = new UsuarioContaCorrente(usuarioId, contaId, remoto.AtualizadoEm) { Excluido = false };
                await _vinculos.SalvarVinculosAsync([novo], token).ConfigureAwait(false);
                vinculosAplicados++;
            }

            // --------------------------------------------------------- lançamentos
            var lancamentosLocais = (await _lancamentos.ListarTodosIncluindoExcluidosAsync(token).ConfigureAwait(false))
                .ToDictionary(l => l.Uuid, StringComparer.OrdinalIgnoreCase);

            foreach (var remoto in pacote.Lancamentos.Where(l => !string.IsNullOrWhiteSpace(l.Uuid)))
            {
                if (!contaIdPorUuid.TryGetValue(remoto.ContaUuid, out var contaId) ||
                    !centroIdPorUuid.TryGetValue(remoto.CentroCustoUuid, out var centroId))
                {
                    avisos.Add($"Lançamento '{remoto.Descricao}' ignorado: conta ou centro de custo ausente.");
                    continue;
                }

                if (lancamentosLocais.TryGetValue(remoto.Uuid, out var local))
                {
                    if (remoto.AtualizadoEm <= local.AtualizadoEm) continue;

                    local.ContaCorrenteId = contaId;
                    local.CentroCustoId = centroId;
                    local.DataLancamento = remoto.DataLancamento;
                    local.DataVencimento = remoto.DataVencimento;
                    local.DataCompensado = remoto.DataCompensado;
                    local.Descricao = remoto.Descricao;
                    local.Valor = remoto.Valor;
                    local.Compensado = remoto.Compensado;
                    local.Agrupador = remoto.Agrupador;
                    local.AgrupadorUuid = remoto.AgrupadorUuid;
                    local.Parcela = remoto.Parcela;
                    local.QuantidadeParcelas = remoto.QuantidadeParcelas;
                    local.Observacao = remoto.Observacao;
                    local.AtualizadoEm = remoto.AtualizadoEm;
                    local.Excluido = remoto.Excluido;

                    await _lancamentos.SalvarAsync(local, token).ConfigureAwait(false);
                    lancamentosAtualizados++;
                    continue;
                }

                if (remoto.Excluido) continue;

                var novo = new Lancamento
                {
                    Uuid = remoto.Uuid,
                    ContaCorrenteId = contaId,
                    CentroCustoId = centroId,
                    DataLancamento = remoto.DataLancamento,
                    DataVencimento = remoto.DataVencimento,
                    DataCompensado = remoto.DataCompensado,
                    Descricao = remoto.Descricao,
                    Valor = remoto.Valor,
                    Compensado = remoto.Compensado,
                    Agrupador = remoto.Agrupador,
                    AgrupadorUuid = remoto.AgrupadorUuid,
                    Parcela = remoto.Parcela,
                    QuantidadeParcelas = remoto.QuantidadeParcelas,
                    Observacao = remoto.Observacao,
                    AtualizadoEm = remoto.AtualizadoEm,
                    Excluido = false
                };

                novo.Validar();
                await _lancamentos.SalvarAsync(novo, token).ConfigureAwait(false);
                lancamentosInseridos++;
            }

            await _configuracoes.DefinirAsync(
                ChaveUltimaSincronizacao,
                _relogio.AgoraUtc.ToString("O", CultureInfo.InvariantCulture),
                token).ConfigureAwait(false);

            return new ResultadoSincronizacao(
                contasInseridas + contasAtualizadas,
                centrosInseridos + centrosAtualizados,
                usuariosInseridos + usuariosAtualizados,
                vinculosAplicados,
                lancamentosInseridos + lancamentosAtualizados,
                avisos);
        }, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ dispositivo

    public async Task<string> ObterDispositivoIdAsync(CancellationToken ct = default)
    {
        var existente = await _configuracoes.ObterAsync(ChaveDispositivoId, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(existente))
        {
            return existente;
        }

        var novo = Guid.NewGuid().ToString("N");
        await _configuracoes.DefinirAsync(ChaveDispositivoId, novo, ct).ConfigureAwait(false);
        return novo;
    }

    public async Task<DateTime?> ObterUltimaSincronizacaoAsync(CancellationToken ct = default)
    {
        var valor = await _configuracoes.ObterAsync(ChaveUltimaSincronizacao, ct).ConfigureAwait(false);

        return DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var data)
            ? data
            : null;
    }

    private static PerfilUsuario PerfilDe(string? codigo)
    {
        try
        {
            return PerfilUsuarioExtensoes.DeCodigo(codigo);
        }
        catch (DominioException)
        {
            return PerfilUsuario.Usuario;
        }
    }
}
