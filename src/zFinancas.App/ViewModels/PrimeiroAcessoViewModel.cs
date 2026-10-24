using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Aplicacao.Validacao;

namespace zFinancas.App.ViewModels;

/// <summary>Primeiro acesso: cria o usuário ADM e mostra o código de recuperação.</summary>
public sealed class PrimeiroAcessoViewModel : BaseViewModel
{
    private readonly PrimeiroAcessoService _primeiroAcesso;
    private readonly ServicoInicializacao _inicializacao;
    private readonly ServicoNavegacao _navegacao;
    private readonly ServicoDialogos _dialogos;

    private string _nome = string.Empty;
    private string _senha = string.Empty;
    private string _confirmacao = string.Empty;
    private string _dica = string.Empty;
    private bool _senhaMascarada = true;

    public PrimeiroAcessoViewModel(
        PrimeiroAcessoService primeiroAcesso,
        ServicoInicializacao inicializacao,
        ServicoNavegacao navegacao,
        ServicoDialogos dialogos)
    {
        _primeiroAcesso = primeiroAcesso;
        _inicializacao = inicializacao;
        _navegacao = navegacao;
        _dialogos = dialogos;

        Titulo = "Primeiro acesso";
        CriarCommand = new ComandoAssincrono(CriarAsync);
        AlternarSenhaCommand = new Comando(() => SenhaMascarada = !SenhaMascarada);
    }

    public ICommand CriarCommand { get; }
    public ICommand AlternarSenhaCommand { get; }

    public string Nome
    {
        get => _nome;
        set => SetProperty(ref _nome, value);
    }

    public string Senha
    {
        get => _senha;
        set
        {
            if (!SetProperty(ref _senha, value)) return;
            OnPropertyChanged(nameof(ForcaSenha));
            OnPropertyChanged(nameof(RotuloForcaSenha));
        }
    }

    public string Confirmacao
    {
        get => _confirmacao;
        set => SetProperty(ref _confirmacao, value);
    }

    public string Dica
    {
        get => _dica;
        set => SetProperty(ref _dica, value);
    }

    public bool SenhaMascarada
    {
        get => _senhaMascarada;
        set => SetProperty(ref _senhaMascarada, value);
    }

    /// <summary>0 a 4 é usado na barra de força da senha.</summary>
    public double ForcaSenha => ValidadorSenha.Forca(Senha) / 4.0;

    public string RotuloForcaSenha => ValidadorSenha.Forca(Senha) switch
    {
        0 => "Informe uma senha",
        1 => "Senha fraca",
        2 => "Senha razoável",
        3 => "Senha boa",
        _ => "Senha forte"
    };

    public string Explicacao =>
        "O zFinancas é 100% offline. Este primeiro cadastro cria o usuário ADM, que administra contas, " +
        "centros de custo e usuários. Guarde o código de recuperação que será exibido.";

    public async Task InicializarAsync()
    {
        await ExecutarAsync(async () =>
        {
            await _inicializacao.GarantirProntoAsync();

            if (!await _primeiroAcesso.PrecisaConfigurarAsync())
            {
                await _navegacao.PopAsync();
            }
        });
    }

    private async Task CriarAsync()
    {
        await ExecutarAsync(async () =>
        {
            ValidadorSenha.ValidarConfirmacao(Senha, Confirmacao);

            var resultado = await _primeiroAcesso.ConfigurarAsync(
                new RequisicaoConfiguracaoInicial(Nome, Senha, Dica));

            Senha = string.Empty;
            Confirmacao = string.Empty;

            await _dialogos.MostrarCodigoAsync(
                "Administrador criado",
                resultado.CodigoRecuperacao,
                $"Bem-vindo, {resultado.Administrador.Nome}! Anote este código: ele é a única forma de " +
                "recuperar a senha caso você a esqueça. Ele não será exibido novamente.");

            await _navegacao.PopAsync();
        }, "Não foi possível concluir o cadastro");
    }
}
