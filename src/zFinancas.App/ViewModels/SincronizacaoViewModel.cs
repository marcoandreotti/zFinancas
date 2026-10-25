using System.Collections.ObjectModel;
using System.Windows.Input;
using zFinancas.App.Servicos;
using zFinancas.Core.Abstracoes;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Aplicacao.Servicos;
using zFinancas.Core.Dominio.Excecoes;

namespace zFinancas.App.ViewModels;

/// <summary>Sincronização PC ? celular por arquivo de pacote (.zfsync).</summary>
public sealed class SincronizacaoViewModel : BaseViewModel
{
    private readonly SincronizacaoService _sincronizacao;
    private readonly IArmazenamentoCompartilhado _armazenamento;
    private readonly IInfoDispositivo _dispositivo;
    private readonly ServicoDialogos _dialogos;

    private bool _mostrarDestinos;

    private string _dispositivoId = string.Empty;
    private string _ultimaSincronizacao = string.Empty;
    private string _resultado = string.Empty;
    private bool _temResultado;

    public SincronizacaoViewModel(
        SincronizacaoService sincronizacao,
        IArmazenamentoCompartilhado armazenamento,
        IInfoDispositivo dispositivo,
        ServicoDialogos dialogos)
    {
        _sincronizacao = sincronizacao;
        _armazenamento = armazenamento;
        _dispositivo = dispositivo;
        _dialogos = dialogos;

        Titulo = "Sincronização";

        CarregarCommand = new ComandoAssincrono(CarregarAsync);
        ExportarCommand = new ComandoAssincrono(ExportarAsync);
        ExportarParaCommand = new ComandoAssincrono<DestinoExportacao>(ExportarParaAsync);
        CancelarDestinosCommand = new Comando(() => MostrarDestinos = false);
        ImportarCommand = new ComandoAssincrono(ImportarAsync);
    }

    public ObservableCollection<string> Avisos { get; } = [];

    public ICommand CarregarCommand { get; }
    public ICommand ExportarCommand { get; }

    /// <summary>Pastas oferecidas no modal de exportação (desktop).</summary>
    public ObservableCollection<DestinoExportacao> Destinos { get; } = [];

    /// <summary>Controla o modal de escolha da pasta.</summary>
    public bool MostrarDestinos
    {
        get => _mostrarDestinos;
        private set => SetProperty(ref _mostrarDestinos, value);
    }

    public ICommand ExportarParaCommand { get; }
    public ICommand CancelarDestinosCommand { get; }
    public ICommand ImportarCommand { get; }

    public string DispositivoId
    {
        get => _dispositivoId;
        private set => SetProperty(ref _dispositivoId, value);
    }

    public string UltimaSincronizacao
    {
        get => _ultimaSincronizacao;
        private set => SetProperty(ref _ultimaSincronizacao, value);
    }

    public string Resultado
    {
        get => _resultado;
        private set => SetProperty(ref _resultado, value);
    }

    public bool TemResultado
    {
        get => _temResultado;
        private set => SetProperty(ref _temResultado, value);
    }

    public string NomeDispositivo => _dispositivo.Nome;

    public string Plataforma => _dispositivo.Plataforma;

    public string Explicacao =>
        "O zFinancas é offline: a sincronização troca um arquivo .zfsync entre os aparelhos. " +
        "Exporte aqui e importe no outro aparelho. Cada registro guarda a última alteração, " +
        "então o mais recente vence e nada é duplicado: pode importar o mesmo arquivo mais de uma vez.";

    public async Task CarregarAsync()
    {
        await ExecutarAsync(async () =>
        {
            DispositivoId = await _sincronizacao.ObterDispositivoIdAsync();

            var ultima = await _sincronizacao.ObterUltimaSincronizacaoAsync();
            UltimaSincronizacao = ultima is null
                ? "Nenhuma sincronização realizada neste aparelho."
                : $"última sincronização: {Formatador.DataHora(ultima.Value.ToLocalTime())}";
        }, "Não foi possível carregar os dados de sincronização");
    }

    private async Task ExportarAsync()
    {
        await ExecutarAsync(async () =>
        {
#if WINDOWS
            // Desktop: escolhe a pasta no modal da própria tela.
            MontarDestinos();
            MostrarDestinos = true;
            return;
#endif

            var caminho = await _sincronizacao.ExportarAsync(_armazenamento);

            if (caminho is null)
            {
                Resultado = "Exportação cancelada.";
                TemResultado = true;
                return;
            }

            var pacote = await _sincronizacao.MontarPacoteAsync();

            Resultado =
                $"Pacote exportado com {pacote.TotalRegistros} registro(s).\n" +
                $"Arquivo: {caminho}";
            TemResultado = true;

            await _dialogos.AlertaAsync(
                "Pacote exportado",
                $"{pacote.TotalRegistros} registros exportados.\n\n{caminho}");
        }, "Não foi possível exportar");
    }

    /// <summary>Pastas comuns do Windows, sem depender de diálogo do sistema.</summary>
    private void MontarDestinos()
    {
        Destinos.Clear();

        void Adicionar(string nome, Environment.SpecialFolder pasta)
        {
            var caminho = Environment.GetFolderPath(pasta);

            if (!string.IsNullOrWhiteSpace(caminho))
            {
                Destinos.Add(new DestinoExportacao(nome, caminho));
            }
        }

        Adicionar("Downloads", Environment.SpecialFolder.UserProfile);
        Adicionar("Documentos", Environment.SpecialFolder.MyDocuments);
        Adicionar("Área de Trabalho", Environment.SpecialFolder.Desktop);

        if (Destinos.Count > 0 && Destinos[0].Nome == "Downloads")
        {
            Destinos[0] = new DestinoExportacao("Downloads", Path.Combine(Destinos[0].Pasta, "Downloads"));
        }

        Destinos.Add(new DestinoExportacao("Pasta do zFinancas", _armazenamento.PastaAtual));
    }

    private async Task ExportarParaAsync(DestinoExportacao destino)
    {
        if (destino is null)
        {
            return;
        }

        MostrarDestinos = false;

        await ExecutarAsync(async () =>
        {
            var caminho = await _sincronizacao.ExportarParaAsync(destino.Pasta, _armazenamento);
            var pacote = await _sincronizacao.MontarPacoteAsync();

            Resultado =
                $"Pacote exportado com {pacote.TotalRegistros} registro(s).\n" +
                $"Arquivo: {caminho}";
            TemResultado = true;

            await _dialogos.AlertaAsync(
                "Pacote exportado",
                $"{pacote.TotalRegistros} registros exportados.\n\n{caminho}");
        }, "Não foi possível exportar");
    }

    private async Task ImportarAsync()
    {
        await ExecutarAsync(async () =>
        {
            var arquivo = await _armazenamento.EscolherArquivoTextoAsync(".zfsync");
            if (arquivo is null) return;

            var resultado = await _sincronizacao.ImportarTextoAsync(arquivo.Conteudo);

            Avisos.Clear();
            foreach (var aviso in resultado.Avisos) Avisos.Add(aviso);

            Resultado = resultado.Total == 0
                ? "Nada novo para importar: os dois aparelhos já estão sincronizados."
                : $"Importado de '{arquivo.Nome}': {resultado.Total} registro(s) atualizado(s).";

            TemResultado = true;

            await CarregarAsync();

            await ServicosApp.Obter<ServicoDialogos>().AlertaAsync("Importação concluída", Resultado);
        }, "Não foi possível importar o pacote");
    }
}
