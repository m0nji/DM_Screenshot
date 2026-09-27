using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace DMShot.Localization;

/// <summary>
/// XAML usage: <c>Text="{loc:Tr historyHeader}"</c>. Binds the target property to
/// <c>Loc.Instance[key]</c>; the indexer's "Item[]" change notification makes every
/// bound string update live when the language changes.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) { Key = key; }

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
