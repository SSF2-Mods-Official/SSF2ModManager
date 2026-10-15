using System.Windows;

namespace SSF2ModManager.Views
{
    public partial class ShellScaffoldPage : System.Windows.Controls.UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(ShellScaffoldPage),
                new PropertyMetadata("", (d, e) =>
                {
                    var page = (ShellScaffoldPage)d;
                    if (page.TxtTitle != null)
                        page.TxtTitle.Text = e.NewValue as string ?? "";
                }));

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(ShellScaffoldPage),
                new PropertyMetadata("", (d, e) => ((ShellScaffoldPage)d).TxtDescription.Text = e.NewValue as string ?? ""));

        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(ShellScaffoldPage),
                new PropertyMetadata("", (d, e) => ((ShellScaffoldPage)d).TxtStatus.Text = e.NewValue as string ?? ""));

        public static readonly DependencyProperty ActionLabelProperty =
            DependencyProperty.Register(nameof(ActionLabel), typeof(string), typeof(ShellScaffoldPage),
                new PropertyMetadata("", (d, e) =>
                {
                    var page = (ShellScaffoldPage)d;
                    page.BtnAction.Content = e.NewValue;
                    page.BtnAction.Visibility = string.IsNullOrWhiteSpace(e.NewValue as string)
                        ? Visibility.Collapsed : Visibility.Visible;
                }));

        public static readonly DependencyProperty SecondaryLabelProperty =
            DependencyProperty.Register(nameof(SecondaryLabel), typeof(string), typeof(ShellScaffoldPage),
                new PropertyMetadata("", (d, e) =>
                {
                    var page = (ShellScaffoldPage)d;
                    page.BtnSecondary.Content = e.NewValue;
                    page.BtnSecondary.Visibility = string.IsNullOrWhiteSpace(e.NewValue as string)
                        ? Visibility.Collapsed : Visibility.Visible;
                }));

        public event RoutedEventHandler? ActionClick;
        public event RoutedEventHandler? SecondaryClick;

        public ShellScaffoldPage()
        {
            InitializeComponent();
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public string StatusText
        {
            get => (string)GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }

        public string ActionLabel
        {
            get => (string)GetValue(ActionLabelProperty);
            set => SetValue(ActionLabelProperty, value);
        }

        public string SecondaryLabel
        {
            get => (string)GetValue(SecondaryLabelProperty);
            set => SetValue(SecondaryLabelProperty, value);
        }

        private void BtnAction_Click(object sender, RoutedEventArgs e) =>
            ActionClick?.Invoke(this, e);

        private void BtnSecondary_Click(object sender, RoutedEventArgs e) =>
            SecondaryClick?.Invoke(this, e);
    }
}
