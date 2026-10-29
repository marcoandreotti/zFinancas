using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Entidades;

namespace zFinancas.App.ViewModels;

/// <summary>Tela de entrada: seleção de usuário, senha e (no celular) biometria.</summary>
public sealed class LoginViewModel : BaseViewModel
{
    private readonly AutenticacaoService _autenticacao;
    private readonly PrimeiroAcessoService _primeiroAcesso;
    private readonly ServicoInicializacao _inicializacao;
    private readonly ServicoPreferencias _preferencias;
    private readonly IServicoBiometria _biometria;

    /// <summary>A biometria é pedida uma vez por abertura da tela de login.</summary>
    private bool _biometriaSolicitada;

    private readonly ServicoNavegacao _navegacao;
    private readonly ServicoDialogos _dialogos;
    private readonly IInfoDispositivo _dispositivo;

    private Usuario? _usuarioSelecionado;
    private string _senha = string.Empty;
    private bool _senhaMascarada = true;
    private bool _mostrarBiometria;
    private bool _carregado;
    private string _mensagem = string.Empty;

    public LoginViewModel(
        AutenticacaoService autenticacao,
        PrimeiroAcessoService primeiroAcesso,
        ServicoInicializacao inicializacao,
        ServicoPreferencias preferencias,
        IServicoBiometria biometria,
        ServicoNavegacao navegacao,
        ServicoDialogos dialogos,
        IInfoDispositivo dispositivo)
    {
        _autenticacao = autenticacao;
        _primeiroAcesso = primeiroAcesso;
        _inicializacao = inicializacao;
        _preferencias = preferencias;
        _biometria = biometria;
        _navegacao = navegacao;
        _dialogos = dialogos;
        _dispositivo = dispositivo;

        Titulo = "Entrar";

        EntrarCommand = new ComandoAssincrono(EntrarAsync, () => UsuarioSelecionado is not null && Senha.Length > 0);
        BiometriaCommand = new ComandoAssincrono(EntrarComBiometriaAsync);
        AlternarSenhaCommand = new Comando(() => SenhaMascarada = !SenhaMascarada);
        RecuperarSenhaCommand = new ComandoAssincrono(() => _navegacao.AbrirRecuperacaoSenhaAsync());
        PrimeiroAcessoCommand = new ComandoAssincrono(() => _navegacao.AbrirPrimeiroAcessoAsync());
    }

    public ObservableCollection<Usuario> Usuarios { get; } = [];

    public ICommand EntrarCommand { get; }
    public ICommand BiometriaCommand { get; }
    public ICommand AlternarSenhaCommand { get; }
    public ICommand RecuperarSenhaCommand { get; }
    public ICommand PrimeiroAcessoCommand { get; }

    public Usuario? UsuarioSelecionado
    {
        get => _usuarioSelecionado;
        set
        {
            if (!SetProperty(ref _usuarioSelecionado, value)) return;

            MostrarDica = !string.IsNullOrWhiteSpace(value?.DicaSenha);
            OnPropertyChanged(nameof(DicaSenha));
            OnPropertyChanged(nameof(IniciaisSelecionado));
            (EntrarCommand as ComandoAssincrono)?.LevantarCanExecuteChanged();
        }
    }

    public string Senha
    {
        get => _senha;
        set
        {
            if (!SetProperty(ref _senha, value)) return;
            Mensagem = string.Empty;
            (EntrarCommand as ComandoAssincrono)?.LevantarCanExecuteChanged();
        }
    }

    public bool SenhaMascarada
    {
        get => _senhaMascarada;
        set => SetProperty(ref _senhaMascarada, value);
    }

    public bool MostrarBiometria
    {
        get => _mostrarBiometria;
        private set => SetProperty(ref _mostrarBiometria, value);
    }

    public bool MostrarDica { get; private set; }

    public string DicaSenha => UsuarioSelecionado?.DicaSenha ?? string.Empty;

    public string IniciaisSelecionado => Formatador.Iniciais(UsuarioSelecionado?.Nome ?? "zF");

    public string Mensagem
    {
        get => _mensagem;
        private set => SetProperty(ref _mensagem, value);
    }

    public string Subtitulo => "Controle financeiro pessoal, offline e no seu aparelho";

    public string Rodape => $"zFinancas 1.0 • {_dispositivo.Plataforma} • dados locais (SQLite)";

    /// <summary>Carrega usuários e decide entre primeiro acesso e login (reexecuta a cada visita).</summary>
    public async Task InicializarAsync()
    {
        if (Ocupado) return;

        await ExecutarAsync(async () =>
        {
            await _inicializacao.GarantirProntoAsync();

            if (await _primeiroAcesso.PrecisaConfigurarAsync())
            {
                await _navegacao.AbrirPrimeiroAcessoAsync();
                return;
            }

            var selecionadoId = UsuarioSelecionado?.Id;

            var usuarios = await _autenticacao.ListarUsuariosAsync();
            Usuarios.Clear();
            foreach (var usuario in usuarios)
            {
                Usuarios.Add(usuario);
            }

            var ultimoId = selecionadoId ?? await _preferencias.ObterUltimoUsuarioAsync();
            UsuarioSelecionado = Usuarios.FirstOrDefault(u => u.Id == ultimoId) ?? Usuarios.FirstOrDefault();

            await ConfigurarBiometriaAsync();

#if DEBUG
            // Atalho de desenvolvimento no aparelho: se existir "autologin.txt" na pasta de
            // dados, entra com o usuário selecionado sem senha. Usado para depurar telas
            // internas via adb (nunca existe em Release).
            var atalho = Path.Combine(FileSystem.AppDataDirectory, "autologin.txt");
            if (File.Exists(atalho) && UsuarioSelecionado is not null && !_biometriaSolicitada)
            {
                _biometriaSolicitada = true;
                System.Diagnostics.Debug.WriteLine("zFinancas: autologin de desenvolvimento");

                await _autenticacao.EntrarComBiometriaAsync(UsuarioSelecionado.Id);
                var relogioPerf = System.Diagnostics.Stopwatch.StartNew();
                await _navegacao.AbrirAplicacaoAsync();
                System.Diagnostics.Debug.WriteLine($"[perf] AbrirApp={relogioPerf.ElapsedMilliseconds}ms");
                return;
            }
#endif

#if DEBUG
            // Atalho de desenvolvimento: ZFINANCAS_AUTOLOGIN="Marco" entra direto,
            // útil para gerar telas de demonstração e depurar. Nunca existe em Release.
            var autoLogin = Environment.GetEnvironmentVariable("ZFINANCAS_AUTOLOGIN");
            if (!string.IsNullOrWhiteSpace(autoLogin))
            {
                var alvo = Usuarios.FirstOrDefault(u =>
                    string.Equals(u.Nome, autoLogin, StringComparison.OrdinalIgnoreCase) ||
                    u.Id.ToString() == autoLogin);

                if (alvo is not null)
                {
                    await _autenticacao.EntrarComBiometriaAsync(alvo.Id);
                    await _preferencias.DefinirUltimoUsuarioAsync(alvo.Id);
                    await _navegacao.AbrirAplicacaoAsync();

                    // Abre direto uma seção específica (revisão visual no desenvolvimento).
                    var secao = Environment.GetEnvironmentVariable("ZFINANCAS_SECAO");
                    if (!string.IsNullOrWhiteSpace(secao))
                    {
                        await Task.Delay(1200);

                        if (secao == "novo-lancamento")
                        {
                            await _navegacao.AbrirLancamentoAsync();
                        }
                        else if (secao == "novo-usuario")
                        {
                            await _navegacao.AbrirUsuarioAsync();
                        }
                        else
                        {
                            await _navegacao.IrParaSecaoAsync(secao);
                        }
                    }
                }
            }
#endif
        });
        // Fora do ExecutarAsync: o login por biometria abre o próprio ExecutarAsync e, se
        // chamado de dentro do carregamento (Ocupado = true), era descartado em silêncio —
        // a digital era reconhecida mas ninguém entrava.
        if (MostrarBiometria && !_biometriaSolicitada && !Ocupado)
        {
            _biometriaSolicitada = true;
            await EntrarComBiometriaAsync();
        }
    }

    private async Task ConfigurarBiometriaAsync()
    {
        // Disponibilidade primeiro: é barato e evita consultar o usuário à toa.
        if (!await _biometria.DisponivelAsync())
        {
            MostrarBiometria = false;
            return;
        }

        var usuarioBiometriaId = await _preferencias.ObterUsuarioComBiometriaAsync();
        if (usuarioBiometriaId is null)
        {
            MostrarBiometria = false;
            return;
        }

        // Só oferece biometria para o usuário que a habilitou neste aparelho.
        var usuarioDaBiometria = Usuarios.FirstOrDefault(u => u.Id == usuarioBiometriaId.Value);
        if (usuarioDaBiometria is null)
        {
            MostrarBiometria = false;
            return;
        }

        UsuarioSelecionado = usuarioDaBiometria;
        MostrarBiometria = true;
    }

    private async Task EntrarAsync()
    {
        if (UsuarioSelecionado is null) return;

        await ExecutarAsync(async () =>
        {
            var usuario = await _autenticacao.EntrarAsync(UsuarioSelecionado.Id, Senha);
            await _preferencias.DefinirUltimoUsuarioAsync(usuario.Id);
            Senha = string.Empty;
            await _navegacao.AbrirAplicacaoAsync();
        }, "Não foi possível entrar");
    }

    private async Task EntrarComBiometriaAsync()
    {
        var usuarioId = UsuarioSelecionado?.Id
                        ?? await _preferencias.ObterUsuarioComBiometriaAsync();

        if (usuarioId is null)
        {
            await _dialogos.AlertaAsync("Biometria", "Nenhum usuário com entrada por biometria neste aparelho.");
            return;
        }

        var resultado = await _biometria.AutenticarAsync($"Entrar como {UsuarioSelecionado?.Nome ?? "usuário"}");

#if ANDROID && DEBUG
        Android.Util.Log.Info("zFinancas.Bio", $"Login por biometria: {resultado}");
#endif

        if (resultado != ResultadoBiometria.Sucesso)
        {
            Mensagem = resultado switch
            {
                ResultadoBiometria.Cancelado => "Entrada por biometria cancelada.",
                ResultadoBiometria.Indisponivel => "Biometria indisponível neste aparelho. Use a senha.",
                _ => "Biometria não reconhecida. Use a senha."
            };

            return;
        }

        await ExecutarAsync(async () =>
        {
            var usuario = await _autenticacao.EntrarComBiometriaAsync(usuarioId.Value);
            await _preferencias.DefinirUltimoUsuarioAsync(usuario.Id);
            await _navegacao.AbrirAplicacaoAsync();
        }, "Não foi possível entrar");
    }
}
