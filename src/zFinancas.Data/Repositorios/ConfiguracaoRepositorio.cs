using zFinancas.Core.Abstracoes;
using zFinancas.Data.Infra;

namespace zFinancas.Data.Repositorios;

/// <summary>Configurações chave/valor do dispositivo (id do aparelho, biometria, última sincronização).</summary>
public sealed class ConfiguracaoRepositorio : RepositorioBase, IConfiguracaoRepositorio
{
    private readonly IRelogio _relogio;

    public ConfiguracaoRepositorio(ContextoBanco contexto, IRelogio relogio) : base(contexto)
    {
        _relogio = relogio;
    }

    public async Task<string?> ObterAsync(string chave, CancellationToken ct = default)
    {
        var itens = await ConsultarAsync(
            "SELECT VALOR FROM CONFIGURACAO WHERE CHAVE = $chave;",
            c => c.Parameters.AddWithValue("$chave", chave),
            r => r.GetString(0),
            ct).ConfigureAwait(false);

        return itens.Count > 0 ? itens[0] : null;
    }

    public Task DefinirAsync(string chave, string valor, CancellationToken ct = default)
        => ExecutarComandoAsync(
            """
            INSERT INTO CONFIGURACAO (CHAVE, VALOR, ATUALIZADOEM)
            VALUES ($chave, $valor, $agora)
            ON CONFLICT (CHAVE) DO UPDATE SET VALOR = $valor, ATUALIZADOEM = $agora;
            """,
            c =>
            {
                c.Parameters.AddWithValue("$chave", chave);
                c.Parameters.AddWithValue("$valor", valor);
                c.Parameters.AddWithValue("$agora", Texto(_relogio.AgoraUtc));
            },
            ct);

    public async Task<IReadOnlyDictionary<string, string>> ListarAsync(CancellationToken ct = default)
    {
        var itens = await ConsultarAsync(
            "SELECT CHAVE, VALOR FROM CONFIGURACAO;",
            null,
            r => new KeyValuePair<string, string>(r.GetString(0), r.GetString(1)),
            ct).ConfigureAwait(false);

        return itens.ToDictionary(p => p.Key, p => p.Value);
    }
}
