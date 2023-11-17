using Avalonia;
using Avalonia.Controls;

namespace ServerProxy.Controls;

public class CustomMenuItem : NativeMenuItem
{
    public static readonly StyledProperty<string?> NameProperty =
        AvaloniaProperty.Register<NativeMenuItem, string?>(nameof(Name));

    public string? Name
    {
        get => GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }
}