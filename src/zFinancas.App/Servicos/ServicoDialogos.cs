namespace zFinancas.App.Servicos;

/// <summary>Diálogos nativos usados pelos view models (sem acoplá-los a Page).</summary>
public sealed class ServicoDialogos
{
    /// <summary>
    /// Página realmente visível. Usar o AppShell (FlyoutPage) fazia o action sheet aparecer
    /// no painel errado no Windows; aqui descemos até a página do detalhe.
    /// </summary>
    private static Page? PaginaAtual
    {
        get
        {
            var raiz = Application.Current?.Windows.FirstOrDefault()?.Page;

            return raiz switch
            {
                FlyoutPage { Detail: NavigationPage navegacao } => navegacao.CurrentPage,
                FlyoutPage { Detail: { } detalhe } => detalhe,
                NavigationPage navegacao => navegacao.CurrentPage,
                _ => raiz
            };
        }
    }

    public Task AlertaAsync(string titulo, string mensagem, string botao = "OK")
        => MainThread.InvokeOnMainThreadAsync(() =>
            PaginaAtual?.DisplayAlert(titulo, mensagem, botao) ?? Task.CompletedTask);

    public Task<bool> ConfirmarAsync(string titulo, string mensagem, string sim = "Confirmar", string nao = "Cancelar")
        => MainThread.InvokeOnMainThreadAsync(() =>
            PaginaAtual?.DisplayAlert(titulo, mensagem, sim, nao) ?? Task.FromResult(false));

    public Task<string?> PerguntarAsync(string titulo, string mensagem, string valorInicial = "", string placeholder = "Valor")
        => MainThread.InvokeOnMainThreadAsync(() =>
            PaginaAtual?.DisplayPromptAsync(titulo, mensagem, "OK", "Cancelar", placeholder, maxLength: 50, initialValue: valorInicial)
            ?? Task.FromResult<string?>(null));

    public Task<string?> EscolherAsync(string titulo, params string[] opcoes)
        => MainThread.InvokeOnMainThreadAsync(() =>
            PaginaAtual?.DisplayActionSheet(titulo, "Cancelar", null, opcoes)
            ?? Task.FromResult<string?>(null));

    /// <summary>Informa um resultado importante com destaque (ex.: código de recuperação).</summary>
    public Task MostrarCodigoAsync(string titulo, string codigo, string mensagem)
        => AlertaAsync(titulo, $"{mensagem}\n\n{codigo}", "Entendi");
}
