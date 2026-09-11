using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using TeronClaudeCodeVS.Controls;
using TeronClaudeCodeVS.Tests.Infrastructure;
using Xunit;

namespace TeronClaudeCodeVS.Tests.Phases
{
    public class MarkdownViewerHyperlinkTests
    {
        /// <summary>
        /// Markdig.Wpf's FlowDocument renderer sets Hyperlink.Command to its own
        /// Markdig.Wpf.Commands.Hyperlink RoutedCommand rather than leaving it null - confirmed by
        /// inspecting a real rendered link live 2026-09-11. Per WPF's Hyperlink.OnClick, a non-null
        /// Command always wins over NavigateUri, so a RequestNavigate handler alone never fires, and
        /// with no CommandBinding to satisfy that command's CanExecute, WPF auto-disables the
        /// Hyperlink outright (IsEnabled=false) - which is what made every rendered link genuinely
        /// unclickable despite looking right. MarkdownViewer's constructor must bind that command.
        /// </summary>
        [Fact]
        public void Markdown_link_is_enabled_and_wired_to_the_markdig_hyperlink_command()
        {
            Sta.Run(() =>
            {
                var viewer = new MarkdownViewer();
                viewer.Viewer.Document = MarkdownRenderer.Render("See [the docs](https://example.com/docs).");

                viewer.Measure(new Size(600, 400));
                viewer.Arrange(new Rect(0, 0, 600, 400));
                viewer.UpdateLayout();

                CommandManager.InvalidateRequerySuggested();
                Sta.Pump(300);

                Hyperlink? link = viewer.Viewer.Document.Blocks
                    .OfType<Paragraph>()
                    .SelectMany(p => p.Inlines)
                    .OfType<Hyperlink>()
                    .FirstOrDefault();

                Assert.NotNull(link);
                // Markdig.Wpf.Commands.Hyperlink itself - checked by name/owner rather than a direct
                // type reference so this test project doesn't need its own Markdig.Wpf package reference.
                RoutedCommand? command = Assert.IsType<RoutedCommand>(link!.Command);
                Assert.Equal("Hyperlink", command.Name);
                Assert.Equal("Markdig.Wpf.Commands", command.OwnerType.FullName);
                Assert.True(link.IsEnabled, "Hyperlink is disabled - no CommandBinding is satisfying Markdig.Wpf's link command.");
                Assert.Equal("https://example.com/docs", link.CommandParameter);
            });
        }
    }
}
