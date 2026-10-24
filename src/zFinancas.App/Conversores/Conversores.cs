namespace zFinancas.App.Conversores;

/// <summary>Verdadeiro quando a string tem conteúdo (para controlar visibilidade).</summary>
public sealed class TextoPreenchidoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverte um booleano (útil para IsVisible/IsEnabled).</summary>
public sealed class InverterBooleanoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is not true;
}

/// <summary>Converte um valor decimal em cor (negativo = vermelho, positivo = menta).</summary>
public sealed class ValorParaCorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is decimal numero && numero < 0
            ? ViewModels.Paleta.Negativo
            : ViewModels.Paleta.Positivo;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converte um booleano em opacidade (itens inativos ficam esmaecidos).</summary>
public sealed class BooleanoParaOpacidadeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? 1.0 : 0.5;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
