using CommunityToolkit.Mvvm.ComponentModel;
using Forensic.Cliente.Api;

namespace Forensic.Cliente.Nucleo.Casos;

/// <summary>Lista de casos del analista. Se completa en el paso 3.</summary>
public sealed partial class CasosViewModel(ClienteForense api, INavegacion navegacion) : ObservableObject
{
    internal ClienteForense Api { get; } = api;
    internal INavegacion Navegacion { get; } = navegacion;
}
