using System.Text;

namespace zFinancas.App.Servicos;

/// <summary>
/// Registro simples de falhas em arquivo.
/// <para>
/// Um app offline não tem telemetria: se algo estourar no celular do usuário, este arquivo
/// é a única forma de diagnosticar. Gravamos em vários locais possíveis porque, durante a
/// inicialização, a pasta de dados do app ainda pode não estar disponível.
/// </para>
/// </summary>
public static class RegistroDeFalhas
{
    private static readonly object Bloqueio = new();

    public static IReadOnlyList<string> Caminhos()
    {
        var caminhos = new List<string>();

        void Adicionar(Func<string> obter)
        {
            try
            {
                var caminho = obter();
                if (!string.IsNullOrWhiteSpace(caminho)) caminhos.Add(caminho);
            }
            catch
            {
                // local indisponível: segue para o próximo
            }
        }

        Adicionar(() => Path.Combine(Path.GetTempPath(), "zfinancas-falhas.log"));
        Adicionar(() => Path.Combine(AppContext.BaseDirectory, "zfinancas-falhas.log"));
        Adicionar(() => Path.Combine(FileSystem.AppDataDirectory, "falhas.log"));

        return caminhos;
    }

    public static void Registrar(Exception? excecao, string origem)
    {
        if (excecao is null) return;

        var texto = new StringBuilder()
            .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {origem}")
            .AppendLine(excecao.ToString())
            .AppendLine(new string('-', 70))
            .ToString();

        lock (Bloqueio)
        {
            foreach (var caminho in Caminhos())
            {
                try
                {
                    var pasta = Path.GetDirectoryName(caminho);
                    if (!string.IsNullOrWhiteSpace(pasta)) Directory.CreateDirectory(pasta);
                    File.AppendAllText(caminho, texto, Encoding.UTF8);
                }
                catch (Exception)
                {
                    // nunca deixar o registro de falha derrubar o app
                }
            }
        }

        try
        {
            Console.Error.WriteLine($"[zFinancas/{origem}] {excecao}");
        }
        catch
        {
            // sem console disponível
        }

        System.Diagnostics.Debug.WriteLine($"[zFinancas/{origem}] {excecao}");
    }
}
