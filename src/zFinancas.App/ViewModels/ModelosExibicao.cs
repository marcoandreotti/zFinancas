using System.Globalization;
using zFinancas.Core.Aplicacao.Modelos;
using zFinancas.Core.Dominio.Entidades;
using zFinancas.Core.Dominio.Servicos;

namespace zFinancas.App.ViewModels;

/// <summary>Formatação pt-BR usada em todas as telas.</summary>
public static class Formatador
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Moeda(decimal valor) => valor.ToString("N2", PtBr);

    public static string MoedaComSinal(decimal valor)
        => (valor > 0 ? "+ " : valor < 0 ? "- " : string.Empty) + Moeda(Math.Abs(valor));

    public static string Data(DateTime data) => data.ToString("dd/MM/yyyy", PtBr);

    public static string DataCurta(DateTime data) => data.ToString("dd MMM", PtBr).Replace(".", string.Empty);

    public static string MesAno(DateTime data) => data.ToString("MMM/yyyy", PtBr).Replace(".", string.Empty);

    public static string DataHora(DateTime data) => data.ToString("dd/MM/yyyy HH:mm", PtBr);

    public static string Saudacao()
    {
        var hora = DateTime.Now.Hour;
        return hora switch
        {
            < 12 => "Bom dia",
            < 18 => "Boa tarde",
            _ => "Boa noite"
        };
    }

    public static string Iniciais(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "?";

        var partes = nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length == 1
            ? partes[0][..1].ToUpperInvariant()
            : string.Concat(partes[0][..1], partes[^1][..1]).ToUpperInvariant();
    }
}

/// <summary>
/// Cores lidas da paleta de recursos (um único lugar para a identidade visual).
/// O valor é resolvido <b>uma vez</b> e memorizado: nas listas cada linha consulta várias
/// cores e procurar no dicionário de recursos a cada binding custa caro no Android.
/// </summary>
public static class Paleta
{
    private static Color? _texto, _textoSecundario, _textoFraco, _positivo, _negativo;
    private static Color? _positivoFraco, _negativoFraco, _alerta, _primaria, _superficie;
    private static Color? _superficieElevada, _borda, _fundo;

    public static Color Texto => _texto ??= Buscar("Texto", Color.FromArgb("#F4F7FF"));
    public static Color TextoSecundario => _textoSecundario ??= Buscar("TextoSecundario", Color.FromArgb("#bbc3d6"));
    public static Color TextoFraco => _textoFraco ??= Buscar("TextoFraco", Color.FromArgb("#64748B"));

    public static Color Positivo => _positivo ??= Buscar("Positivo", Color.FromArgb("#4D9BFF"));
    public static Color Negativo => _negativo ??= Buscar("Negativo", Color.FromArgb("#FF5C7A"));

    public static Color PositivoFraco => _positivoFraco ??= Buscar("PositivoFraco", Color.FromArgb("#78b2fe"));
    public static Color NegativoFraco => _negativoFraco ??= Buscar("NegativoFraco", Color.FromArgb("#f89eaf"));

    public static Color Alerta => _alerta ??= Buscar("Alerta", Color.FromArgb("#F7B955"));
    public static Color Primaria => _primaria ??= Buscar("Primaria", Color.FromArgb("#6C5BFF"));
    public static Color Superficie => _superficie ??= Buscar("Superficie", Color.FromArgb("#111A2E"));
    public static Color SuperficieElevada => _superficieElevada ??= Buscar("SuperficieElevada", Color.FromArgb("#18233C"));
    public static Color Borda => _borda ??= Buscar("Borda", Color.FromArgb("#243152"));
    public static Color Fundo => _fundo ??= Buscar("Fundo", Color.FromArgb("#070B16"));

    public static Color Buscar(string chave, Color padrao)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(chave, out var valor) == true && valor is Color cor)
            {
                return cor;
            }
        }
        catch (Exception)
        {
            // recursos ainda não carregados
        }

        return padrao;
    }

    public static Color DeHex(string? hex, Color? padrao = null)
    {
        if (string.IsNullOrWhiteSpace(hex)) return padrao ?? Primaria;

        try
        {
            return Color.FromArgb(hex);
        }
        catch (Exception)
        {
            return padrao ?? Primaria;
        }
    }

    /// <summary>Escurece uma cor para montar gradientes dos cartões de conta.</summary>
    public static Color Escurecer(Color cor, float fator)
        => new(cor.Red * fator, cor.Green * fator, cor.Blue * fator);
}

/// <summary>Lançamento já formatado para exibição em listas.</summary>
public sealed class LancamentoVm
{
    public long Id { get; init; }
    public string Descricao { get; init; }
    public string Detalhe { get; init; }
    public string ValorTexto { get; init; }
    public decimal Valor { get; init; }
    public DateTime DataVencimento { get; init; }
    public bool Pendente { get; init; }
    public bool ForaDoPeriodo { get; init; }
    public string IconeCentro { get; init; }
    public string RotuloParcela { get; init; }
    public long ContaCorrenteId { get; init; }
    public long CentroCustoId { get; init; }
    public string ContaDescricao { get; init; }
    public string CentroDescricao { get; init; }
    public string? Observacao { get; init; }

    /// <summary>Saldo acumulado após a linha (somente no extrato).</summary>
    public string? SaldoTexto { get; init; }

    public Color? CorSaldo { get; init; }

    public string DataTexto => Formatador.Data(DataVencimento);

    public Color CorValor => Valor < 0 ? Paleta.Negativo : Paleta.Positivo;

    public Color CorPendente => Pendente ? Paleta.Alerta : Paleta.Texto;

    public Color CorFundoDestaque => ForaDoPeriodo
        ? Color.FromRgba(247, 185, 85, 28)
        : Colors.Transparent;

    public Color CorBorda => Pendente ? Color.FromRgba(247, 185, 85, 90) : Paleta.Borda;

    public string SituacaoTexto => Pendente ? (ForaDoPeriodo ? "Pendente anterior" : "A compensar") : "Compensado";

    public Color CorSituacao => Pendente ? Paleta.Alerta : Paleta.Positivo;

    public bool TemSaldo => !string.IsNullOrEmpty(SaldoTexto);

    public static LancamentoVm Criar(
        Lancamento lancamento,
        ContaCorrente? conta = null,
        CentroCusto? centro = null,
        decimal? saldo = null,
        string? saldoTexto = null,
        bool foraDoPeriodo = false,
        bool parcelaNoDetalhe = false)
    {
        var partes = new List<string> { lancamento.DataVencimento.ToString("dd/MM/yyyy") };

        // Na tela de Lançamentos a parcela entra no meio: data • 3/12 • centro.
        if (parcelaNoDetalhe && lancamento.QuantidadeParcelas > 1)
        {
            partes.Add(lancamento.RotuloParcela);
        }

        if (centro is not null)
        {
            //partes.Add($"{centro.Icone} {centro.Descricao}");
            partes.Add($"{centro.Descricao}");
        }

        var detalhe = string.Join(" • ", partes);

        return new LancamentoVm
        {
            Id = lancamento.Id,
            Descricao = lancamento.Descricao,
            Detalhe = detalhe,
            ValorTexto = Formatador.MoedaComSinal(lancamento.Valor),
            Valor = lancamento.Valor,
            DataVencimento = lancamento.DataVencimento,
            Pendente = lancamento.Pendente,
            ForaDoPeriodo = foraDoPeriodo,
            IconeCentro = centro?.Icone ?? "🧾",
            RotuloParcela = lancamento.RotuloParcela,
            ContaCorrenteId = lancamento.ContaCorrenteId,
            CentroCustoId = lancamento.CentroCustoId,
            ContaDescricao = conta?.Descricao ?? string.Empty,
            CentroDescricao = centro?.Descricao ?? string.Empty,
            Observacao = lancamento.Observacao,
            SaldoTexto = saldoTexto,
            CorSaldo = GetCorSaldo(saldo)
        };
    }

    private static Color GetCorSaldo(decimal? saldo) => saldo < 0 ? Paleta.NegativoFraco : Paleta.PositivoFraco;
}

/// <summary>Cartão de conta do carrossel do dashboard.</summary>
public sealed class ContaCartaoVm
{
    public long Id { get; init; }
    public string Descricao { get; init; }
    public string Icone { get; init; }
    public string CompensadoTexto { get; init; }
    public string PendenteTexto { get; init; }
    public string VencidoTexto { get; init; }
    public string TotalTexto { get; init; }
    public bool Principal { get; init; }

    public Brush Gradiente { get; init; } = new SolidColorBrush(Paleta.Primaria);

    public static ContaCartaoVm Criar(ContaResumo resumo)
    {
        var baseCor = Paleta.DeHex(resumo.CorHex);
        var fim = Paleta.Escurecer(baseCor, 0.45f);

        return new ContaCartaoVm
        {
            Id = resumo.Id,
            Descricao = resumo.Descricao,
            Icone = resumo.Icone,
            CompensadoTexto = Formatador.Moeda(resumo.Saldo.Compensado),
            PendenteTexto = Formatador.Moeda(resumo.Saldo.NaoCompensado),
            VencidoTexto = Formatador.Moeda(resumo.Saldo.NaoCompensadoVencido),
            TotalTexto = Formatador.Moeda(resumo.Saldo.Total),
            Principal = resumo.Principal,
            Gradiente = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(baseCor, 0f),
                    new(fim, 1f)
                },
                new Point(0, 0),
                new Point(1, 1))
        };
    }
}

/// <summary>Item do menu lateral.</summary>
/// <summary>Amostra de cor para o seletor de contas.</summary>
/// <summary>Chip de cor do formulário de conta (com estado de selecionado).</summary>
public sealed class CorOpcao : System.ComponentModel.INotifyPropertyChanged
{
    private bool _selecionada;

    public CorOpcao(string hex) => Hex = hex;

    public string Hex { get; }

    public Color Cor => Paleta.DeHex(Hex);

    public bool Selecionada
    {
        get => _selecionada;
        set
        {
            if (_selecionada == value) return;
            _selecionada = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Selecionada)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CorBorda)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(EspessuraBorda)));
        }
    }

    public Color CorBorda => Selecionada ? Colors.White : Colors.Transparent;

    public double EspessuraBorda => Selecionada ? 3 : 0;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Ícone do formulário de centro de custo (chip com estado de selecionado).</summary>
public sealed class IconeOpcao : System.ComponentModel.INotifyPropertyChanged
{
    private bool _selecionado;

    public IconeOpcao(string valor) => Valor = valor;

    public string Valor { get; }

    public bool Selecionado
    {
        get => _selecionado;
        set
        {
            if (_selecionado == value) return;
            _selecionado = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Selecionado)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Fundo)));
        }
    }

    public Color Fundo => Selecionado ? Paleta.Primaria : Paleta.SuperficieElevada;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ItemMenu
{
    public string Titulo { get; init; }
    public string Icone { get; init; }
    public string Rota { get; init; }
    public bool Selecionado { get; init; }

    public Color CorFundo => Selecionado ? Color.FromArgb("#1C2743") : Colors.Transparent;
    public Color CorTexto => Selecionado ? Paleta.Texto : Paleta.TextoSecundario;
}

public sealed record CentroCustoVm(long Id, string Descricao, string Icone, bool Principal, bool Ativo)
{
    public string Situacao => Ativo ? (Principal ? "Principal" : "Ativo") : "Inativo";
    public string RotuloAcao => Ativo ? "Desativar" : "Ativar";
    public Color CorSituacao => Ativo ? (Principal ? Paleta.Positivo : Paleta.TextoSecundario) : Paleta.TextoFraco;
    public double Opacidade => Ativo ? 1 : 0.5;
}

public sealed record ContaVm(long Id, string Descricao, string Icone, string CorHex, bool Principal, bool Ativo)
{
    public Color Cor => Paleta.DeHex(CorHex);
    public string Situacao => Ativo ? (Principal ? "Principal" : "Ativa") : "Inativa";
    public string RotuloAcao => Ativo ? "Desativar" : "Ativar";
    public Color CorSituacao => Ativo ? (Principal ? Paleta.Positivo : Paleta.TextoSecundario) : Paleta.TextoFraco;
    public double Opacidade => Ativo ? 1 : 0.5;
}

public sealed record UsuarioVm(long Id, string Nome, string PerfilDescricao, string Iniciais, bool Ativo, bool EhAdministrador, string ContasResumo)
{
    public Color CorPerfil => EhAdministrador ? Paleta.Primaria : Paleta.TextoSecundario;
    public string Situacao => Ativo ? "Ativo" : "Inativo";
    public Color CorSituacao => Ativo ? Paleta.Positivo : Paleta.Negativo;
    public double Opacidade => Ativo ? 1 : 0.55;
}
