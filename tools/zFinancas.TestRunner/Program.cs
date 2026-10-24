using System.Diagnostics;
using System.Reflection;

// Runner minimalista: descobre os metodos [Fact] da suite zFinancas.Tests e executa.
// Existe porque alguns ambientes bloqueiam o VSTest (abertura de handle do processo pai).
// Retorna 0 quando tudo passa e 1 quando ha falha - pronto para uso em CI.

var assembly = Assembly.Load("zFinancas.Tests");
var cronometro = Stopwatch.StartNew();

var aprovados = 0;
var falhas = new List<(string Nome, string Erro)>();
// Filtro opcional por nome (ignora flags repassadas pelo "dotnet run").
var filtro = args.FirstOrDefault(a => !a.StartsWith('-'));

var tipos = assembly.GetTypes()
    .Where(t => t.IsClass && !t.IsAbstract)
    .OrderBy(t => t.Name)
    .ToList();

foreach (var tipo in tipos)
{
    var testes = tipo
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m.GetCustomAttributes(inherit: true).Any(a => a.GetType().Name == "FactAttribute"))
        .Where(m => filtro is null || (tipo.Name + "." + m.Name).Contains(filtro, StringComparison.OrdinalIgnoreCase))
        .OrderBy(m => m.MetadataToken)
        .ToList();

    if (testes.Count == 0)
    {
        continue;
    }

    Console.WriteLine();
    Console.WriteLine(tipo.Name);

    foreach (var teste in testes)
    {
        var nome = tipo.Name + "." + teste.Name;
        object? instancia = null;

        try
        {
            instancia = Activator.CreateInstance(tipo);

            if (teste.Invoke(instancia, null) is Task tarefa)
            {
                await tarefa.ConfigureAwait(false);
            }

            aprovados++;
            Console.WriteLine("  [ok]    " + teste.Name);
        }
        catch (Exception excecao)
        {
            var real = excecao is TargetInvocationException { InnerException: not null } interno
                ? interno.InnerException!
                : excecao;

            falhas.Add((nome, real.Message));
            Console.WriteLine("  [FALHA] " + teste.Name);
            Console.WriteLine("          " + real.GetType().Name + ": " + real.Message);
        }
        finally
        {
            switch (instancia)
            {
                case IAsyncDisposable assincrono:
                    await assincrono.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable descartavel:
                    descartavel.Dispose();
                    break;
            }
        }
    }
}

cronometro.Stop();

Console.WriteLine();
Console.WriteLine(new string('-', 70));
Console.WriteLine($"Aprovados: {aprovados}   Falhas: {falhas.Count}   Tempo: {cronometro.Elapsed.TotalSeconds:F1}s");

foreach (var falha in falhas)
{
    Console.WriteLine();
    Console.WriteLine("  FALHA em " + falha.Nome);
    Console.WriteLine("    " + falha.Erro);
}

return falhas.Count == 0 ? 0 : 1;


