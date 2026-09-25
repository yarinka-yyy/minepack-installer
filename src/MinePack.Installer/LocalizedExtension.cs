using System.Windows.Markup;
using MinePack.Core;

namespace MinePack.Installer;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocalizedExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => LocalizedText.Get(Key);
}
