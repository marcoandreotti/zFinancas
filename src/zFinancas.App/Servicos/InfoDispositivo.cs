using zFinancas.Core.Abstracoes;

namespace zFinancas.App.Servicos;

/// <summary>Informações do aparelho em execução.</summary>
public sealed class InfoDispositivo : IInfoDispositivo
{
    public string Nome
    {
        get
        {
            try
            {
                var nome = DeviceInfo.Current.Name;
                return string.IsNullOrWhiteSpace(nome) ? Plataforma : nome;
            }
            catch
            {
                return Plataforma;
            }
        }
    }

    public string Plataforma => DeviceInfo.Current.Platform.ToString();

    public bool EhMovel => DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                           || DeviceInfo.Current.Idiom == DeviceIdiom.Tablet;
}
