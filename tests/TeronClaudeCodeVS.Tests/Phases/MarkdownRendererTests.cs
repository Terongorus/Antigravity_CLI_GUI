using System.Linq;
using System.Windows.Controls;
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

        /// <summary>
        /// Found live 2026-09-06: the CLI's shell-execution tool is literally named "PowerShell"
        /// on Windows, not "Bash" - ToolPresentation only special-cased "Bash", so a PowerShell
        /// call fell through to the generic default handler and showed the raw tool-call JSON
        /// (truncated) as its one-line summary and a "```json" dump of that same JSON as its detail
        /// block, instead of the description and the actual command. "PowerShell" is now treated
        /// as an alias of "Bash" in GetSummary/GetDetailMarkdown (not GetDisplayName - its own name
        /// is more informative there than the generic "Run command" title).
        /// </summary>
        [Fact]
        public void A_PowerShell_call_is_summarised_and_detailed_like_a_Bash_call_not_dumped_as_JSON()
        {
            var input = new Newtonsoft.Json.Linq.JObject
            {
                ["command"] = "dotnet build --no-restore",
                ["description"] = "Verify project builds without errors",
            };

            string summary = ToolPresentation.GetSummary("PowerShell", input);
            Assert.Equal("Verify project builds without errors", summary);
            Assert.DoesNotContain("{", summary);

            string? detail = ToolPresentation.GetDetailMarkdown("PowerShell", input, output: null, isError: false);
            Assert.NotNull(detail);
            Assert.Contains("```powershell", detail);
            Assert.Contains("dotnet build --no-restore", detail!);
            Assert.DoesNotContain("```json", detail!);
        }

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

        /// <summary>
        /// The header moved from a Paragraph built out of Inlines/Floaters to a native
        /// DockPanel-in-a-BlockUIContainer (see MarkdownRenderer.BuildCodeHeader's own doc comment
        /// - a Floater tall enough to hold the "Insert at Cursor" dropdown rendered outside the
        /// header strip entirely, found live 2026-09-06). This walks that same real structure
        /// instead of TextRange/Inlines, which only ever see FlowDocument text content, not what is
        /// inside an embedded UIElement.
        /// </summary>
        private static TextBlock GetHeaderLabel(Section section)
        {
            BlockUIContainer headerContainer = Assert.IsType<BlockUIContainer>(section.Blocks.FirstBlock);
            Border headerBorder = Assert.IsType<Border>(headerContainer.Child);
            DockPanel dock = Assert.IsType<DockPanel>(headerBorder.Child);
            return dock.Children.OfType<TextBlock>().Single();
        }

        [Fact]
        public void A_code_block_header_shows_the_language_when_there_is_no_file()
        {
            Sta.Run(() =>
            {
                FlowDocument doc = MarkdownRenderer.Render("```bash\necho hi\n```");

                Section section = Assert.Single(FindCodeSections(doc));
                TextBlock label = GetHeaderLabel(section);

                Assert.Equal("bash", label.Text);
                Assert.Empty(label.Inlines.OfType<Hyperlink>());
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
                TextBlock label = GetHeaderLabel(section);

                Hyperlink link = Assert.Single(label.Inlines.OfType<Hyperlink>());
                string linkText = new TextRange(link.ContentStart, link.ContentEnd).Text;
                Assert.Equal("Foo.cs", linkText);
            });
        }

        [Fact]
        public void The_insert_actions_dropdown_only_appears_for_real_file_content_not_a_diff()
        {
            // Real bug found live 2026-09-06: a tool call's plain "Output:" text wrap inherited the
            // edited file's path (see ContentBlocks.ToolCallViewModel.DetailDocument's own fix) and
            // so showed an "Insert at Cursor" dropdown on a message with nothing to insert. Even
            // with a real file path, a "```diff" fence's +/- lines aren't valid file content to
            // insert either - both must be denied the dropdown, and a genuine primary file's own
            // code fence must still get it.
            Sta.Run(() =>
            {
                FlowDocument diffDoc = MarkdownRenderer.Render(
                    "```diff\n+added\n```", primaryFilePath: @"D:\Repo\Foo.cs");
                Assert.False(HasInsertActionsButton(Assert.Single(FindCodeSections(diffDoc))));

                FlowDocument outputDoc = MarkdownRenderer.Render("```\nsome output\n```");
                Assert.False(HasInsertActionsButton(Assert.Single(FindCodeSections(outputDoc))));

                FlowDocument codeDoc = MarkdownRenderer.Render(
                    "```csharp\nclass Foo {}\n```", primaryFilePath: @"D:\Repo\Foo.cs");
                Assert.True(HasInsertActionsButton(Assert.Single(FindCodeSections(codeDoc))));
            });

            static bool HasInsertActionsButton(Section section)
            {
                BlockUIContainer headerContainer = Assert.IsType<BlockUIContainer>(section.Blocks.FirstBlock);
                Border headerBorder = Assert.IsType<Border>(headerContainer.Child);
                DockPanel dock = Assert.IsType<DockPanel>(headerBorder.Child);
                StackPanel actions = Assert.Single(dock.Children.OfType<StackPanel>());
                return actions.Children.OfType<Button>().Any(b => b.Content is string s && s == "Insert at Cursor ▾");
            }
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
        public void A_bare_filename_mention_stays_plain_text_when_no_project_is_indexed()
        {
            // LinkifyBareFilenames reads Core.ClaudeCodePackage.Instance.IndexedProjectFiles, which
            // is null/empty outside a real VS host - same reason this suite never instantiates
            // ClaudeCodePackage at all (see ChatControl.cs). This only proves the degrade-gracefully
            // path: no exception, and nothing gets linked without a real project index to check
            // against. The actual matching behavior needs a live F5 pass, same as the VS
            // classification colors elsewhere in this file.
            Sta.Run(() =>
            {
                FlowDocument doc = MarkdownRenderer.Render("See ClaudeCodePackage.cs for details.");

                Paragraph para = Assert.IsType<Paragraph>(doc.Blocks.FirstBlock);
                Assert.Empty(para.Inlines.OfType<Hyperlink>());
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

        // ─── Bare-filename resolution against the project index ─────────────────────────────────
        //
        // Real bug found live 2026-09-05: a workspace listing mentioned both
        // "TestConsoleApp\Program.cs" and "TestProjectClaude2\Program.cs" and only the FIRST
        // Program.cs in IndexedProjectFiles ever got linked - the old FirstOrDefault-by-leaf-name
        // lookup silently pointed BOTH mentions at the same file. These call ResolveIndexedFile
        // directly (see Infrastructure/Reflect.cs) since the actual auto-link pass is gated behind
        // Core.ClaudeCodePackage.Instance, which is null outside a real VS host - same reason as
        // A_bare_filename_mention_stays_plain_text_when_no_project_is_indexed above.

        [Fact]
        public void A_bare_leaf_name_resolves_when_it_is_unique_in_the_project()
        {
            string[] indexed = [@"D:\Solution\Class1.cs", @"D:\Solution\TestConsoleApp\Program.cs"];

            string? resolved = Reflect.StaticCall<string>(typeof(MarkdownRenderer), "ResolveIndexedFile", "Program.cs", indexed);

            Assert.Equal(@"D:\Solution\TestConsoleApp\Program.cs", resolved);
        }

        [Fact]
        public void A_bare_leaf_name_shared_by_two_files_resolves_to_neither()
        {
            string[] indexed =
            [
                @"D:\Solution\TestConsoleApp\Program.cs",
                @"D:\Solution\TestProjectClaude2\Program.cs",
            ];

            string? resolved = Reflect.StaticCall<string>(typeof(MarkdownRenderer), "ResolveIndexedFile", "Program.cs", indexed);

            Assert.Null(resolved);
        }

        [Fact]
        public void A_candidate_carrying_its_own_directory_disambiguates_a_shared_leaf_name()
        {
            string[] indexed =
            [
                @"D:\Solution\TestConsoleApp\Program.cs",
                @"D:\Solution\TestProjectClaude2\Program.cs",
            ];

            string? resolved = Reflect.StaticCall<string>(
                typeof(MarkdownRenderer), "ResolveIndexedFile", @"TestProjectClaude2\Program.cs", indexed);

            Assert.Equal(@"D:\Solution\TestProjectClaude2\Program.cs", resolved);
        }

        [Fact]
        public void A_directory_qualified_candidate_never_matches_a_path_that_merely_ends_with_the_same_letters()
        {
            // "TestConsoleApp\Program.cs" must not match "...\OtherTestConsoleApp\Program.cs" just
            // because the raw string happens to end the same way.
            string[] indexed = [@"D:\Solution\OtherTestConsoleApp\Program.cs"];

            string? resolved = Reflect.StaticCall<string>(
                typeof(MarkdownRenderer), "ResolveIndexedFile", @"TestConsoleApp\Program.cs", indexed);

            Assert.Null(resolved);
        }
    }
}
