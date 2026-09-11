using TeronClaudeCodeVS.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace TeronClaudeCodeVS.Controls
{
    /// <summary>Renders an <see cref="IMarkdownContent"/> view model's markdown, refreshing as it streams in.</summary>
    public partial class MarkdownViewer : UserControl
    {
        public MarkdownViewer()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;

            // FlowDocumentScrollViewer marks MouseWheel as handled even when its scroll is
            // disabled, which prevents the event from reaching the outer ChatScrollViewer.
            // Re-raise on ourselves so it bubbles up normally.
            Viewer.AddHandler(
                System.Windows.UIElement.MouseWheelEvent,
                new System.Windows.Input.MouseWheelEventHandler(OnViewerMouseWheel),
                handledEventsToo: true);

            // A real markdown [text](url) link renders as a genuine WPF Hyperlink, but Markdig.Wpf's
            // own FlowDocument renderer sets Hyperlink.Command to its own Markdig.Wpf.Commands.Hyperlink
            // RoutedCommand (confirmed by inspecting the rendered element live - CommandName="Hyperlink",
            // CommandOwnerType="Markdig.Wpf.Commands") rather than leaving Command null. Per WPF's own
            // Hyperlink.OnClick, a non-null Command always wins over NavigateUri - RequestNavigate is
            // only ever raised when Command is null - so a RequestNavigate handler alone (tried first,
            // 2026-09-09) is dead code here and never fires. Worse, with no CommandBinding anywhere in
            // the tree to satisfy that RoutedCommand's CanExecute, WPF auto-disables the Hyperlink
            // (IsEnabled=false) as any unsatisfiable ICommandSource does, which is why it doesn't even
            // show a hand cursor. The fix Markdig.Wpf itself expects: bind that command, not NavigateUri.
            CommandBindings.Add(new CommandBinding(
                Markdig.Wpf.Commands.Hyperlink, OnHyperlinkCommandExecuted, OnHyperlinkCommandCanExecute));
        }

        private void OnHyperlinkCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
            e.Handled = true;
        }

        private void OnHyperlinkCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            // Markdig.Wpf passes the link's URL as the command parameter (confirmed live).
            if (e.Parameter is string url && !string.IsNullOrEmpty(url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch
                {
                    // No browser, or the shell refused - nothing useful to say about it here.
                }
            }

            e.Handled = true;
        }

        private void OnViewerMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (e.Delta == 0) return;
            e.Handled = true;
            MouseWheelEventArgs args = new(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = System.Windows.UIElement.MouseWheelEvent
            };
            RaiseEvent(args);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged old)
                old.PropertyChanged -= OnContentPropertyChanged;

            if (e.NewValue is IMarkdownContent content)
            {
                ((INotifyPropertyChanged)content).PropertyChanged += OnContentPropertyChanged;
                Viewer.Document = content.Document;
            }
            else
            {
                Viewer.Document = new FlowDocument();
            }
        }

        private void OnContentPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IMarkdownContent.Document)) return;
            if (sender is IMarkdownContent content)
                Viewer.Document = content.Document;
        }
    }
}
