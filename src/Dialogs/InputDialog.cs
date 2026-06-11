using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            DialogTheme.ApplyWindow(this);

            var panel = new StackPanel { Margin = new Thickness(20) };

            var promptBlock = DialogTheme.Text(prompt, wrap: TextWrapping.Wrap);
            promptBlock.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(promptBlock);

            _textBox = DialogTheme.Input(fontSize: 14, maxLength: maxLength);
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

            var btnOk = DialogTheme.StyledButton("  OK  ", "ModernButton", new Thickness(0, 0, 8, 0));
            btnOk.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(_textBox.Text))
                    DialogResult = true;
            };

            var btnCancel = DialogTheme.StyledButton("Cancel", "DangerButton");
            btnCancel.IsCancel = true;
            btnCancel.Click += (_, _) => DialogResult = false;

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            panel.Children.Add(btnPanel);

            Content = panel;

            Loaded += (s, e) => _textBox.Focus();
        }
    }
}
