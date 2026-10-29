using System;
using System.Linq;
using System.Text;
using SSF2ModManager.Services;

namespace SSF2ModManager.Views
{
    public partial class CreditsPage : System.Windows.Controls.UserControl
    {
        public CreditsPage(string? shell)
        {
            InitializeComponent();
            TxtGeneral.Text = CreditsService.GeneralCreditsText();
            Loaded += (_, _) => Refresh(shell);
        }

        private void Refresh(string? shell)
        {
            if (string.IsNullOrWhiteSpace(shell))
            {
                TxtEmpty.Visibility = System.Windows.Visibility.Visible;
                TxtMods.Text = "";
                return;
            }

            CreditsService.Update(shell);
            var entries = CreditsService.Scan(shell)
                .Where(e => e.Enabled)
                .Where(e => !e.Section.Equals("essentials", StringComparison.OrdinalIgnoreCase))
                .ToList();

            TxtEmpty.Visibility = entries.Count == 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                var creator = string.IsNullOrWhiteSpace(e.Creator) ? "Unknown" : e.Creator;
                sb.Append("- ").Append(e.Name).Append(" — ").Append(creator)
                    .Append("  [").Append(e.Type).Append("]  (")
                    .Append(e.RelativePath).AppendLine(")");
            }
            TxtMods.Text = sb.ToString().TrimEnd();
        }
    }
}
