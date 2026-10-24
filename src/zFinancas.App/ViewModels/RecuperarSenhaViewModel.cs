using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Aplicacao.Validacao;

namespace zFinancas.App.ViewModels;

/// <summary>Recuperação de senha pelo código gerado no cadastro (uso único).</summary>
public sealed class RecuperarSenhaViewModel : BaseViewModel
{
    private readonly AutenticacaoService _autenticacao;
    private readonly ServicoNavegacao _navegacao;
    private readonly ServicoDialogos _dialogos;

    private string _nome = string.Empty;
    private string _codigo = string.Empty;
    private string _novaSenha = string.Empty;
    private string _confirmacao = string.Empty;

    public RecuperarSenhaViewModel(
        AutenticacaoService autenticacao,
        ServicoNavegacao navegacao,
        ServicoDialogos dialogos)
    {
        _autenticacao = autenticacao;
        _navegacao = navegacao;
        _dialogos = dialogos;

        Titulo = "Recuperar senha";
        RecuperarCommand = new ComandoAssincrono(RecuperarAsync);
    }

    public ICommand RecuperarCommand { get; }

    public string Nome
    {
        get => _nome;
        set => SetProperty(ref _nome, value);
    }

    public string Codigo
    {
        get => _codigo;
        set => SetProperty(ref _codigo, value);
    }

    public string NovaSenha
    {
        get => _novaSenha;
        set => SetProperty(ref _novaSenha, value);
    }

    public string Confirmacao
    {
        get => _confirmacao;
        set => SetProperty(ref _confirmacao, value);
    }

    public string Explicacao =>
        "Digite seu nome, o código de recuperação entregue no cadastro e a nova senha. " +
        "O código é de uso único: um novo será gerado ao final.";

    private async Task RecuperarAsync()
    {
        await ExecutarAsync(async () =>
        {
            ValidadorSenha.ValidarConfirmacao(NovaSenha, Confirmacao);

            var novoCodigo = await _autenticacao.RecuperarSenhaAsync(Nome, Codigo, NovaSenha);

            await _dialogos.MostrarCodigoAsync(
                "Senha alterada",
                novoCodigo,
                "Sua senha foi redefinida. Guarde o NOVO código de recuperação é o anterior não funciona mais.");

            await _navegacao.PopAsync();
        }, "Não foi possível recuperar a senha");
    }
}
