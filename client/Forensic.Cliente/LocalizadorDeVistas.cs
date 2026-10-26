using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Forensic.Cliente;

/// <summary>
/// Busca la vista de cada ViewModel por convención: <c>Nucleo.X.FooViewModel</c> → <c>Vistas.FooVista</c>.
/// Las vistas no tienen lógica: sólo bindings al ViewModel.
/// </summary>
public sealed class LocalizadorDeVistas : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;
        var nombre = "Forensic.Cliente.Vistas." + param.GetType().Name.Replace("ViewModel", "Vista", StringComparison.Ordinal);
        return Type.GetType(nombre) is { } tipo
            ? (Control)Activator.CreateInstance(tipo)!
            : new TextBlock { Text = $"Falta la vista {nombre}." };
    }

    public bool Match(object? data) => data is ObservableObject;
}
