using Android.Text;
using Android.Text.Method;
using Microsoft.Maui.Handlers;

namespace zFinancas.App.Platforms.Android;

/// <summary>
/// Ajustes de teclado no Android.
/// <para>
/// O <c>Keyboard="Numeric"</c> do MAUI cria um <c>DigitsKeyListener</c> que só aceita o
/// separador decimal do <b>locale do aparelho</b>. Com o celular em inglês, a vírgula é
/// descartada na digitação — e o usuário fica sem conseguir lançar centavos.
/// Aqui liberamos ponto <b>e</b> vírgula; o ViewModel normaliza para vírgula.
/// </para>
/// </summary>
public static class TecladoNumerico
{
    private const string Separadores = "0123456789,.";

    private static bool _configurado;

    public static void Configurar()
    {
        if (_configurado)
        {
            return;
        }

        _configurado = true;

        EntryHandler.Mapper.AppendToMapping("zFinancas.SeparadorDecimal", (handler, entry) =>
        {
            if (entry.Keyboard != Keyboard.Numeric)
            {
                return;
            }

            handler.PlatformView.InputType =
                InputTypes.ClassNumber | InputTypes.NumberFlagDecimal;

            // Aceita os dois separadores, independente do idioma do aparelho.
            handler.PlatformView.KeyListener = DigitsKeyListener.GetInstance(Separadores)!;
        });
    }
}
