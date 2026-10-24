using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Enums;

namespace zFinancas.App.ViewModels;

/// <summary>Lista de usuários (somente ADM).</summary>
public sealed class UsuariosViewModel : BaseViewModel
{
    private readonly UsuarioService _servico;
    private readonly ContaCorrenteService _contas;
    private readonly ISessaoAtual _sessao;
    private readonly ServicoNavegacao _navegacao;
    private readonly ServicoDialogos _dialogos;

    public UsuariosViewModel(
        UsuarioService servico,
        ContaCorrenteService contas,
        ISessaoAtual sessao,
        ServicoNavegacao navegacao,
        ServicoDialogos dialogos)
    {
        _servico = servico;
        _contas = contas;
        _sessao = sessao;
        _navegacao = navegacao;
        _dialogos = dialogos;

        Titulo = "Usuários";

        CarregarCommand = new ComandoAssincrono(CarregarAsync);
        NovoCommand = new ComandoAssincrono(() => _navegacao.AbrirUsuarioAsync());
        EditarCommand = new ComandoAssincrono<UsuarioVm>(item =>
            item is null ? Task.CompletedTask : _navegacao.AbrirUsuarioAsync(item.Id));
        AlternarAtivoCommand = new ComandoAssincrono<UsuarioVm>(AlternarAtivoAsync);
        AcoesCommand = new ComandoAssincrono<UsuarioVm>(ExecutarAcoesAsync);
        RedefinirSenhaCommand = new ComandoAssincrono<UsuarioVm>(RedefinirSenhaAsync);
    }

    public ObservableCollection<UsuarioVm> Itens { get; } = [];

    public ICommand CarregarCommand { get; }
    public ICommand NovoCommand { get; }
    public ICommand EditarCommand { get; }
    public ICommand AlternarAtivoCommand { get; }
    public ICommand RedefinirSenhaCommand { get; }
    public ICommand AcoesCommand { get; }

    public bool SemItens => Itens.Count == 0;

    public bool EhAdministrador => _sessao.EhAdministrador;

    public async Task CarregarAsync()
    {
        await ExecutarAsync(async () =>
        {
            var usuarios = await _servico.ListarAsync();
            var contas = (await _contas.ListarAsync(incluirInativas: true)).ToDictionary(c => c.Id);

            Itens.Clear();
            foreach (var resumo in usuarios)
            {
                var contasTexto = resumo.Usuario.EhAdministrador
                    ? "acesso a todas as contas"
                    : resumo.ContasIds.Count == 0
                        ? "sem conta vinculada"
                        : string.Join(", ", resumo.ContasIds
                            .Select(id => contas.GetValueOrDefault(id)?.Descricao)
                            .Where(nome => nome is not null));

                Itens.Add(new UsuarioVm(
                    resumo.Usuario.Id,
                    resumo.Usuario.Nome,
                    resumo.Usuario.Perfil.Descrever(),
                    Formatador.Iniciais(resumo.Usuario.Nome),
                    resumo.Usuario.Ativo,
                    resumo.Usuario.EhAdministrador,
                    contasTexto));
            }

            OnPropertyChanged(nameof(SemItens));
        }, "Não foi possível carregar os usuários");
    }

    private async Task AlternarAtivoAsync(UsuarioVm? item)
    {
        if (item is null) return;

        await ExecutarAsync(async () =>
        {
            await _servico.AlterarAtivoAsync(item.Id, !item.Ativo);
            await CarregarAsync();
        }, "Não foi possível alterar o usuário");
    }

    private async Task RedefinirSenhaAsync(UsuarioVm? item)
    {
        if (item is null) return;

        var confirmar = await _dialogos.ConfirmarAsync(
            "Redefinir senha",
            $"Gerar uma senha temporária para {item.Nome}? A senha atual deixará de funcionar.",
            "Gerar",
            "Cancelar");

        if (!confirmar) return;

        await ExecutarAsync(async () =>
        {
            var (senhaTemporaria, codigo) = await _servico.RedefinirSenhaAsync(item.Id);

            await _dialogos.AlertaAsync(
                "Senha redefinida",
                $"Senha temporária de {item.Nome}: {senhaTemporaria}\n\n" +
                $"Novo código de recuperação: {codigo}\n\n" +
                "Entregue essas informações ao usuário e peça para trocar a senha.");
        }, "Não foi possível redefinir a senha");
    }
    /// <summary>Menu de ações do usuário na lista (editar, redefinir senha, ativar/desativar).</summary>
    private async Task ExecutarAcoesAsync(UsuarioVm? item)
    {
        if (item is null) return;

        var opcoes = new List<string>
        {
            "Editar",
            "Redefinir senha",
            item.Ativo ? "Desativar" : "Ativar"
        };

        var escolha = await _dialogos.EscolherAsync($"{item.Nome} • {item.PerfilDescricao}", [.. opcoes]);

        switch (escolha)
        {
            case "Editar":
                await _navegacao.AbrirUsuarioAsync(item.Id);
                return;

            case "Redefinir senha":
                await RedefinirSenhaAsync(item);
                break;

            case "Ativar":
            case "Desativar":
                await AlternarAtivoAsync(item);
                break;

            default:
                return;
        }

        await CarregarAsync();
    }
}