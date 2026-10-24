namespace zFinancas.App.Conversores;

/// <summary>
/// Converte um booleano em cor. Parâmetro no formato <c>"#COR_TRUE|#COR_FALSE"</c>.
/// </summary>
public sealed class BooleanoParaCorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var cores = (parameter as string ?? string.Empty).Split('|');
        var cor = value is true
            ? cores.ElementAtOrDefault(0)
            : cores.ElementAtOrDefault(1);

        return string.IsNullOrWhiteSpace(cor) ? Colors.Transparent : Color.FromArgb(cor);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
