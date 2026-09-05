using System.Linq;
using System.Windows.Documents;
using System.Windows.Media;
using TeronClaudeCodeVS.Controls;
using TeronClaudeCodeVS.Tests.Infrastructure;
using TeronClaudeCodeVS.ViewModels;
using Xunit;

namespace TeronClaudeCodeVS.Tests.Phases
{
    /// <summary>
    /// Regression coverage for a real bug found live 2026-09-05: a tool-call card showing a
    /// command followed by its output ("Run command" + "Output:") rendered the command's code
    /// block with the fixed-up accent highlight but left the output's code block on Markdig.Wpf's
    /// raw, unfixed light-grey default - reported as "the output field STILL uses the old ugly
    /// highlight" right after the highlight color itself had just been fixed.
    /// <para>
    /// Root cause: every Block/Inline in a FlowDocument shares one underlying TextContainer, so
    /// inserting a copy-button Floater into an earlier sibling paragraph's Inlines
    /// (<c>MarkdownRenderer.AddCopyAffordance</c>) bumped a version counter that invalidated
    /// <c>WalkBlocks</c>' own live <c>foreach</c> over the outer <c>BlockCollection</c> - "Collection
    /// was modified" on the next block. <c>PostProcess</c>'s broad <c>catch</c> swallowed it
    /// silently, so post-processing quietly stopped after the first code block in ANY multi-block
    /// document, for as long as the copy-button feature has existed. These tests assert every
    /// top-level code block gets the same treatment, not just the first.
    /// </para>
    /// <para>
    /// Phase 24 replaced the flat highlighted-Paragraph code block with a header+body chrome (a
    /// <see cref="Section"/> per block, background on the Section rather than the Paragraph) - the
    /// helper below finds those Sections instead of top-level Paragraphs.
    /// </para>
    /// </summary>
    public sealed class MarkdownRendererTests
    {
        private static System.Collections.Generic.List<Section> FindCodeSections(FlowDocument doc) =>
            doc.Blocks.OfType<Section>()
                .Where(s => s.Background is SolidColorBrush { Color.A: > 0 })
                .ToList();

        [Fact]
        public void A_command_followed_by_its_output_colors_both_code_blocks_the_same_way()
        {
            Sta.Run(() =>
            {
                // Exactly the shape ToolPresentation.GetDetailMarkdown produces for a Bash tool
                // call: a ```bash command block, then a **Output:** heading and a plain ```` block.
                string? markdown = ToolPresentation.GetDetailMarkdown(
                    "Bash",
                    new Newtonsoft.Json.Linq.JObject { ["command"] = "grep -n \"#43\" foo.md" },
                    output: "3:description: \"contains [#43](https://example.com/43)\"",
                    isError: false);

                Assert.NotNull(markdown);

                FlowDocument doc = MarkdownRenderer.Render(markdown!);

                var codeSections = FindCodeSections(doc);
                Assert.Equal(2, codeSections.Count);

                var colors = codeSections
                    .Select(s => ((SolidColorBrush)s.Background).Color)
                    .Distinct()
                    .ToList();

                Assert.Single(colors); // both blocks got the same chrome brush, not one fixed and one left raw
                Assert.NotEqual(Color.FromArgb(0xFF, 0xD3, 0xD3, 0xD3), colors[0]); // not Markdig.Wpf's raw default

                // Each section is header paragraph + content paragraph.
                Assert.All(codeSections, s => Assert.Equal(2, s.Blocks.Count));
            });
        }

        [Fact]
        public void Three_sequential_fenced_code_blocks_all_get_fixed_up()
        {
            Sta.Run(() =>
            {
                string markdown =
                    "```bash\ncmd one\n```\n\n" +
                    "```bash\ncmd two\n```\n\n" +
                    "```bash\ncmd three\n```\n";

                FlowDocument doc = MarkdownRenderer.Render(markdown);

                var codeSections = FindCodeSections(doc);

                Assert.Equal(3, codeSections.Count);
                Assert.Single(codeSections.Select(s => ((SolidColorBrush)s.Background).Color).Distinct());
            });
        }

        [Fact]
        public void A_code_block_header_shows_the_language_when_there_is_no_file()
        {
            Sta.Run(() =>
            {
                FlowDocument doc = MarkdownRenderer.Render("```bash\necho hi\n```");

                Section section = Assert.Single(FindCodeSections(doc));
                Paragraph header = Assert.IsType<Paragraph>(section.Blocks.FirstBlock);
                string headerText = new TextRange(header.ContentStart, header.ContentEnd).Text;

                Assert.Contains("bash", headerText);
                Assert.DoesNotContain(header.Inlines.OfType<Hyperlink>(), _ => true);
            });
        }

        [Fact]
        public void A_code_block_header_links_to_the_primary_file_path_instead_of_the_language()
        {
            Sta.Run(() =>
            {
                FlowDocument doc = MarkdownRenderer.Render(
                    "```csharp\nclass Foo {}\n```",
                    primaryFilePath: @"D:\Repo\Foo.cs");

                Section section = Assert.Single(FindCodeSections(doc));
                Paragraph header = Assert.IsType<Paragraph>(section.Blocks.FirstBlock);

                Hyperlink link = Assert.Single(header.Inlines.OfType<Hyperlink>());
                string linkText = new TextRange(link.ContentStart, link.ContentEnd).Text;
                Assert.Equal("Foo.cs", linkText);
            });
        }

        [Fact]
        public void Keywords_and_strings_in_a_csharp_block_are_tokenized_into_separate_runs()
        {
            Sta.Run(() =>
            {
                FlowDocument doc = MarkdownRenderer.Render("```csharp\nreturn \"hi\";\n```");

                Section section = Assert.Single(FindCodeSections(doc));
                Paragraph content = Assert.IsType<Paragraph>(section.Blocks.LastBlock);

                var runs = content.Inlines.OfType<Run>().Select(r => r.Text).ToList();
                Assert.Contains("return", runs);
                Assert.Contains("\"hi\"", runs);
                Assert.True(runs.Count > 1); // one big flat Run would mean tokenizing didn't run at all
            });
        }

        [Fact]
        public void A_diff_block_still_gets_the_old_add_remove_line_coloring_not_tokenized()
        {
            Sta.Run(() =>
            {
                string markdown = "```diff\n+added line\n-removed line\n```";
                FlowDocument doc = MarkdownRenderer.Render(markdown);

                Section section = Assert.Single(FindCodeSections(doc));
                Paragraph content = Assert.IsType<Paragraph>(section.Blocks.LastBlock);

                var addRun = content.Inlines.OfType<Run>().First(r => r.Text.StartsWith("+"));
                var remRun = content.Inlines.OfType<Run>().First(r => r.Text.StartsWith("-"));
                Assert.NotEqual(((SolidColorBrush)addRun.Foreground).Color, ((SolidColorBrush)remRun.Foreground).Color);
            });
        }
    }
}
