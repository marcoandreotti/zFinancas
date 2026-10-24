using Microsoft.Maui.Storage;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.App.Servicos;

/// <summary>
/// Decide onde o banco SQLite vive e garante que o arquivo é gravável — evitando o
/// <c>SQLite Error 8: attempt to write a readonly database</c>.
/// <para>
/// O erro quase nunca é do banco: é do ambiente (pasta sem permissão, arquivo com atributo
/// “somente leitura”, arquivo criado por outro usuário/processo). A estratégia é tentar
/// vários locais, <b>testando escrita de verdade</b> em cada um, e guardar o motivo da
/// falha de cada tentativa para exibir no erro.
/// </para>
/// </summary>
public sealed class CaminhoBanco
{
    private const string NomeArquivo = "zfinancas.db";
    private const string PreferenciaPasta = "banco.pasta";

    private readonly List<string> _diagnostico = [];

    public CaminhoBanco()
    {
        var (caminho, aviso) = Resolver();

        Caminho = caminho;
        Pasta = Path.GetDirectoryName(caminho) ?? caminho;
        Aviso = aviso;

        MigrarBancoAntigo();
        GarantirArquivoGravavel();

        Pasta = Path.GetDirectoryName(Caminho) ?? Caminho;

        if (!string.IsNullOrWhiteSpace(Aviso))
        {
            RegistroDeFalhas.Registrar(new InvalidOperationException(Aviso), "CaminhoBanco");
        }

        // Trilha de diagnóstico: o caminho e as tentativas ficam registrados no log.
        RegistroDeFalhas.Registrar(
            new InvalidOperationException(
                $"Banco de dados em: {Caminho}" + Environment.NewLine + string.Join(Environment.NewLine, _diagnostico)),
            "CaminhoBanco");
    }

    public string Caminho { get; private set; }

    public string Pasta { get; private set; }

    public string? Aviso { get; private set; }

    public bool UsouLocalAlternativo => !string.IsNullOrWhiteSpace(Aviso);

    /// <summary>Tentativas realizadas (pasta + motivo) — útil para suporte.</summary>
    public IReadOnlyList<string> Diagnostico => _diagnostico;

    // ------------------------------------------------------------- descoberta

    private (string Caminho, string? Aviso) Resolver()
    {
        // 1) caminho explícito (variável de ambiente) tem prioridade
        var personalizado = Environment.GetEnvironmentVariable("ZFINANCAS_DB");
        if (!string.IsNullOrWhiteSpace(personalizado))
        {
            var preferido = Path.GetFullPath(personalizado);
            var pastaPreferida = Path.GetDirectoryName(preferido) ?? preferido;

            if (TentarPasta(pastaPreferida, foraDoPadrao: true))
            {
                return (preferido, null);
            }

            _diagnostico.Add($"ZFINANCAS_DB ignorado: '{pastaPreferida}' não é gravável.");
        }

        // 2) pasta escolhida anteriormente pelo usuário (Preferences sobrevive a tudo)
        var escolhida = LerPreferencia();
        if (!string.IsNullOrWhiteSpace(escolhida) && TentarPasta(escolhida, foraDoPadrao: true))
        {
            return (Path.Combine(escolhida, NomeArquivo), null);
        }

        // 3) candidatos automáticos, na ordem de preferência
        var candidatas = Candidatos().ToList();

        for (var i = 0; i < candidatas.Count; i++)
        {
            if (!TentarPasta(candidatas[i], foraDoPadrao: false))
            {
                continue;
            }

            // Avisa quando não foi possível usar o local preferido (aparece na UI e no log).
            var aviso = i == 0
                ? null
                : $"O local padrão não estava gravável; usando '{candidatas[i]}'.";

            return (Path.Combine(candidatas[i], NomeArquivo), aviso);
        }

        // Nada gravável: seguimos com TEMP para o app ABRIR e explicar o problema na tela,
        // em vez de derrubar a janela na criação.
        var detalhes = string.Join(Environment.NewLine, _diagnostico);
        var recurso = Path.Combine(Path.GetTempPath(), "zFinancas");

        return (Path.Combine(recurso, NomeArquivo),
            "Nenhuma pasta gravável foi encontrada para o banco de dados. Tentativas:" +
            Environment.NewLine + detalhes + Environment.NewLine +
            "Defina ZFINANCAS_DB apontando para uma pasta onde você possa escrever (ex.: C:\\zFinancas).");
    }

    private static IEnumerable<string> Candidatos()
    {
#if ANDROID
        yield return FileSystem.AppDataDirectory;
        yield return Path.Combine(FileSystem.CacheDirectory, "banco");
#else
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "zFinancas");

        yield return FileSystem.AppDataDirectory;

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "zFinancas");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "zFinancas");

        yield return Path.Combine(Path.GetTempPath(), "zFinancas");

        // Último recurso: pasta do executável. É para onde o VS compila e onde o processo
        // quase sempre pode escrever (serve para desenvolvimento; avisamos na UI).
        yield return Path.Combine(AppContext.BaseDirectory, "App_Data");
#endif
    }

    /// <summary>
    /// Testa escrita de verdade: cria a pasta, grava um arquivo pequeno e apaga.
    /// Só depois disso o SQLite é aberto. Guarda o motivo de cada falha.
    /// </summary>
    private bool TentarPasta(string pasta, bool foraDoPadrao)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pasta))
            {
                throw new InvalidOperationException("caminho vazio");
            }

            Directory.CreateDirectory(pasta);

            var teste = Path.Combine(pasta, $".escrita-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(teste, "zfinancas");
            File.Delete(teste);

            if (foraDoPadrao)
            {
                _diagnostico.Add($"OK (configurado): {pasta}");
            }

            return true;
        }
        catch (Exception ex)
        {
            _diagnostico.Add($"FALHOU: {pasta} → {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------ o arquivo

    /// <summary>
    /// Garante que o arquivo existente aceita escrita. Remove o atributo “somente leitura”
    /// (causa mais comum) e, se a permissão negar, copia o banco para uma pasta gravável.
    /// </summary>
    private void GarantirArquivoGravavel()
    {
        if (!File.Exists(Caminho))
        {
            return;
        }

        try
        {
            var atributos = File.GetAttributes(Caminho);
            if (atributos.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(Caminho, atributos & ~FileAttributes.ReadOnly);
            }

            using var fluxo = new FileStream(Caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            _ = fluxo.CanWrite;
        }
        catch (UnauthorizedAccessException)
        {
            RealocarBanco();
        }
        catch (IOException)
        {
            // Em uso por outro processo: o SQLite resolve com busy_timeout.
        }
    }

    private void RealocarBanco()
    {
        foreach (var pasta in Candidatos())
        {
            if (string.Equals(Path.GetFullPath(pasta), Pasta, StringComparison.OrdinalIgnoreCase) ||
                !TentarPasta(pasta, foraDoPadrao: false))
            {
                continue;
            }

            var destino = Path.Combine(pasta, NomeArquivo);

            try
            {
                if (!File.Exists(destino))
                {
                    File.Copy(Caminho, destino, overwrite: false);
                }

                var anterior = Caminho;
                Caminho = destino;
                Aviso =
                    $"O banco em '{anterior}' não era gravável para este usuário. " +
                    $"Passamos a usar '{destino}' (cópia preservada).";

                return;
            }
            catch (Exception ex)
            {
                _diagnostico.Add($"Não foi possível copiar o banco para {pasta} → {ex.Message}");
            }
        }

        Aviso =
            $"O banco em '{Caminho}' não é gravável e não foi possível movê-lo. " +
            "Verifique as permissões do arquivo ou apague-o para o app criar um novo.";
    }

    // -------------------------------------------------------------- manutenção

    /// <summary>
    /// Traz o banco de uma instalação anterior (o MAUI podia resolver
    /// <c>FileSystem.AppDataDirectory</c> para outra pasta).
    /// </summary>
    private void MigrarBancoAntigo()
    {
        if (File.Exists(Caminho))
        {
            return;
        }

        try
        {
            var legado = Path.Combine(FileSystem.AppDataDirectory, NomeArquivo);

            if (!File.Exists(legado) ||
                string.Equals(Path.GetFullPath(legado), Caminho, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            File.Copy(legado, Caminho, overwrite: false);

            RegistroDeFalhas.Registrar(
                new InvalidOperationException($"Banco antigo migrado de '{legado}' para '{Caminho}'."),
                "CaminhoBanco");
        }
        catch (Exception ex)
        {
            RegistroDeFalhas.Registrar(ex, "CaminhoBanco.Migrar");
        }
    }

    private static string? LerPreferencia()
    {
        try
        {
            var valor = Preferences.Default.Get(PreferenciaPasta, string.Empty);
            return string.IsNullOrWhiteSpace(valor) ? null : valor;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
