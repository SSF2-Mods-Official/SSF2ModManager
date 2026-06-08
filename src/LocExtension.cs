using System;
using System.Windows.Markup;

namespace SSF2ModManager
{
    // Usage in XAML: xmlns:local="clr-namespace:SSF2ModManager"
    // <TextBlock Text="{local:Loc AppTitle}" />
    public class LocExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public LocExtension() { }
        public LocExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            try
            {
                if (string.IsNullOrEmpty(Key)) return string.Empty;
                return Localization.Get(Key) ?? Key;
            }
            catch
            {
                return Key;
            }
        }
    }
}
