using System;
using System.Collections.Generic;
using System.Windows;

namespace SSF2ModManager.Views
{
    public partial class TutorialWindow : Window
    {
        private List<(string title, string body, string? page)> _steps = new();
        private int _index = 0;

        // Raised when the active step changes. The string is the optional page key
        // (e.g. "Mods", "News", "Settings") provided by the step tuple.
        public event Action<string?>? StepChanged;

        public TutorialWindow()
        {
            InitializeComponent();
        }

        public void SetSteps(List<(string title, string body, string? page)> steps)
        {
            _steps = steps ?? new List<(string, string, string?)>();
            _index = 0;
            Refresh();
        }

        private void Refresh()
        {
            if (_index < 0) _index = 0;
            if (_index >= _steps.Count) { DialogResult = true; Close(); return; }
            var s = _steps[_index];
            TxtTitle.Text = s.title;
            TxtBody.Text = s.body;
            BtnBack.IsEnabled = _index > 0;
            BtnNext.Content = _index == _steps.Count - 1 ? "Finish" : "Next";

            try
            {
                StepChanged?.Invoke(s.page);
            }
            catch { }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            _index--;
            Refresh();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _index++;
            Refresh();
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
