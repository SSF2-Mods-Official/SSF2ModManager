using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;
using WpfApplication = System.Windows.Application;

namespace SSF2ModManager.Dialogs
{
    internal static class DialogTheme
    {
        public static void ApplyWindow(Window window) =>
            window.SetResourceReference(Window.BackgroundProperty, "BackgroundBrush");

        public static void Set(FrameworkElement element, DependencyProperty property, string resourceKey) =>
            element.SetResourceReference(property, resourceKey);

        public static TextBlock Text(string text, string foregroundKey = "TextPrimaryBrush", double fontSize = 14,
            TextWrapping wrap = TextWrapping.NoWrap)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                TextWrapping = wrap
            };
            Set(block, TextBlock.ForegroundProperty, foregroundKey);
            return block;
        }

        public static Button StyledButton(string content, string styleKey, Thickness? margin = null)
        {
            var btn = new Button
            {
                Content = content,
                Padding = new Thickness(16, 8, 16, 8),
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                Margin = margin ?? new Thickness(0)
            };
            if (WpfApplication.Current.TryFindResource(styleKey) is Style style)
                btn.Style = style;
            else
            {
                Set(btn, Button.BackgroundProperty, "PrimaryBrush");
                Set(btn, Button.ForegroundProperty, "TextPrimaryBrush");
            }
            return btn;
        }

        public static Button SurfaceButton(string content, string backgroundKey = "CardBrush", double fontSize = 13,
            Thickness? margin = null, HorizontalAlignment hAlign = HorizontalAlignment.Left)
        {
            var btn = new Button
            {
                Content = content,
                Padding = new Thickness(14, 7, 14, 7),
                BorderThickness = new Thickness(0),
                FontSize = fontSize,
                Cursor = Cursors.Hand,
                Margin = margin ?? new Thickness(0, 0, 8, 0),
                HorizontalAlignment = hAlign,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            Set(btn, Button.BackgroundProperty, backgroundKey);
            Set(btn, Button.ForegroundProperty, "TextPrimaryBrush");
            return btn;
        }

        public static TextBox Input(double fontSize = 14, int maxLength = 0)
        {
            var box = new TextBox
            {
                FontSize = fontSize,
                Padding = new Thickness(10, 8, 10, 8)
            };
            if (maxLength > 0)
                box.MaxLength = maxLength;
            Set(box, TextBox.BackgroundProperty, "CardBrush");
            Set(box, TextBox.ForegroundProperty, "TextPrimaryBrush");
            Set(box, TextBox.BorderBrushProperty, "BorderBrush");
            Set(box, TextBox.CaretBrushProperty, "TextPrimaryBrush");
            return box;
        }

        public static Border Separator()
        {
            var border = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) };
            border.SetResourceReference(Border.BackgroundProperty, "TextSecondaryBrush");
            border.Opacity = 0.35;
            return border;
        }
    }
}
