using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace SSF2ModManager.Dialogs
{
    public class InputDialog : Window
    {
        private readonly TextBox _textBox;
        public string InputText => _textBox.Text;

        public InputDialog(string title, string prompt, int maxLength = 0)
        {
            Title = title;
            Width = 400;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 46));

            var panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = prompt,
                Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            _textBox = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 52, 96)),
                Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 51, 102)),
                CaretBrush = new SolidColorBrush(Colors.White),
                FontSize = 14,
                Padding = new Thickness(10, 8, 10, 8),
                MaxLength = maxLength > 0 ? maxLength : 0
            };
            _textBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(_textBox.Text))
                    DialogResult = true;
            };

            panel.Children.Add(_textBox);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var btnOk = new Button
            {
                Content = "  OK  ",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromRgb(30, 136, 229)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnOk.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(_textBox.Text))
                    DialogResult = true;
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromRgb(229, 57, 53)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => DialogResult = false;

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            panel.Children.Add(btnPanel);

            Content = panel;

            Loaded += (s, e) => _textBox.Focus();
        }
    }
}
