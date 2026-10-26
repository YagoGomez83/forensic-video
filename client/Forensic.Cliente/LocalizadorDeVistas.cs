using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Forensic.Cliente;

/// <summary>
/// Busca la vista de cada ViewModel por convención: <c>FooViewModel</c> → <c>Vistas.FooVista</c> y un
/// formulario <c>FormularioDeX</c> → <c>Vistas.FormularioDeXVista</c>.
/// Las vistas no tienen lógica: sólo bindings al ViewModel.
/// </summary>
public sealed class LocalizadorDeVistas : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;
        var tipo = param.GetType().Name;
        var nombre = "Forensic.Cliente.Vistas." + (tipo.EndsWith("ViewModel", StringComparison.Ordinal) ? tipo[..^"ViewModel".Length] : tipo) + "Vista";
        return Type.GetType(nombre) is { } vista
            ? (Control)Activator.CreateInstance(vista)!
            : new TextBlock { Text = $"Falta la vista {nombre}." };
    }

    public bool Match(object? data) => data is ObservableObject;
}
