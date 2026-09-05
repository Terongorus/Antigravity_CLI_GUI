using Markdig;
using Microsoft.VisualStudio.Language.StandardClassification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;

namespace TeronClaudeCodeVS.Controls
{
    public static class MarkdownRenderer
    {
        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UsePipeTables()
                .UseTaskLists()
                .UseEmojiAndSmiley()
                .Build();

        // Card background for a fenced code block - inline `code` spans keep this same tint too.
        // Went through two revisions on 2026-09-05: first just bumping a neutral grey's alpha
        // (0x18 -> 0x40), which fixed "barely visible" but was called out live as still the wrong
        // idea - a shade/alpha tweak on a neutral tone reads as a washed-out chip either way, not
        // something that actually stands out. Switched to the app's own accent hue (ChatTheme.xaml's
        // ClaudeAccentBrush #D97757, kept in sync manually here since that dictionary isn't
        // reachable from this static, no-visual-tree renderer) at low alpha - alpha-blending over
        // whatever the real background is keeps the "never needs a separate light/dark value"
        // property, but a warm, branded tint reads as an intentional highlight on both VS light and
        // dark instead of a generic grey box.
        private static readonly SolidColorBrush s_codeBg = Frozen(Color.FromArgb(0x33, 0xD9, 0x77, 0x57));

        // The header strip needs to read as visibly distinct from the body beneath it (see the
        // GitHub Copilot Chat reference screenshots) - same hue, stronger alpha, so it visually
        // deepens where it's painted over the section's own s_codeBg rather than introducing a
        // second hardcoded color that would need its own light/dark justification.
        private static readonly SolidColorBrush s_codeHeaderBg = Frozen(Color.FromArgb(0x50, 0xD9, 0x77, 0x57));
        private static readonly SolidColorBrush s_codeBorderBrush = Frozen(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
        private static readonly FontFamily s_inlineCodeFont = new("Consolas");

        // Diff line colors (same hues as GitHub's diff view).
        private static readonly SolidColorBrush s_diffAdd = Frozen(Color.FromArgb(0xFF, 0x3F, 0xB9, 0x50));
        private static readonly SolidColorBrush s_diffRem = Frozen(Color.FromArgb(0xFF, 0xE5, 0x48, 0x4D));
        private static readonly SolidColorBrush s_diffHunk = Frozen(Color.FromArgb(0xFF, 0x79, 0xB8, 0xFF));

        // Code-block copy button: an icon reads faster than a text label at this size, and Segoe
        // UI Symbol carries these glyphs as plain monochrome shapes (no color-emoji presentation)
        // on every Windows version this extension targets.
        private static readonly FontFamily s_symbolFont = new("Segoe UI Symbol");
        private const string s_copyGlyph = "⧉";       // ⧉ two overlapping squares
        private const string s_copiedGlyph = "✓";     // ✓ check mark
        private const string s_copyFailedGlyph = "⚠"; // ⚠ warning sign

        private static SolidColorBrush Frozen(Color c) { SolidColorBrush b = new(c); b.Freeze(); return b; }

        /// <summary>Threaded through one Render() call: which language each top-level fenced code
        /// block is written in (in document order, from Markdig's own AST - lost once Markdig.Wpf
        /// has already turned everything into plain WPF TextElements), and which single file path
        /// (if any) the FIRST such block should link to in its header.</summary>
        private sealed class CodeBlockContext
        {
            public Queue<string?> Languages { get; } = new();
            public string? PrimaryFilePath { get; set; }
            private bool _primaryPathConsumed;

            public string? NextLanguage() => Languages.Count > 0 ? Languages.Dequeue() : null;

            public string? ConsumePrimaryFilePath()
            {
                if (_primaryPathConsumed || string.IsNullOrEmpty(PrimaryFilePath)) return null;
                _primaryPathConsumed = true;
                return PrimaryFilePath;
            }
        }

        public static FlowDocument Render(string markdown) => Render(markdown, primaryFilePath: null);

        /// <param name="primaryFilePath">
        /// The file the FIRST fenced code block in <paramref name="markdown"/> is about (an Edit's
        /// diff, a Write's new content) - shown as a clickable filename in that block's header
        /// instead of a plain language label. Null for tool calls with no single file (Bash, a
        /// generic JSON dump) or for plain assistant prose.
        /// </param>
        public static FlowDocument Render(string markdown, string? primaryFilePath)
        {
            if (string.IsNullOrEmpty(markdown))
                return new FlowDocument();

            try
            {
                string xaml = Markdig.Wpf.Markdown.ToXaml(markdown, Pipeline);

                using StringReader reader = new(xaml);
                using XmlReader xml = System.Xml.XmlReader.Create(reader);

                FlowDocument doc = (FlowDocument)XamlReader.Load(xml);

                // FlowDocument defaults to a fixed ~768px column width meant for paginated
                // documents; without this, content gets clipped inside a narrow tool window.
                doc.PagePadding = new Thickness(0);
                doc.ColumnWidth = double.PositiveInfinity;

                CodeBlockContext ctx = new() { PrimaryFilePath = primaryFilePath };
                // A second, independent parse of the same source text: Markdig.Wpf's ToXaml already
                // discarded the AST (language info strings included) by the time it produced plain
                // WPF TextElements above. CodeBlock (FencedCodeBlock's base) also covers a 4-space
                // indented block, which carries no Info string - same as an unrecognized language.
                Markdig.Syntax.MarkdownDocument ast = Markdig.Markdown.Parse(markdown, Pipeline);
                var codeBlocks = Markdig.Syntax.MarkdownObjectExtensions.Descendants<Markdig.Syntax.CodeBlock>(ast);
                foreach (Markdig.Syntax.CodeBlock codeBlock in codeBlocks)
                    ctx.Languages.Enqueue((codeBlock as Markdig.Syntax.FencedCodeBlock)?.Info);

                PostProcess(doc, ctx);

                return doc;
            }
            catch
            {
                FlowDocument doc = new();
                doc.Blocks.Add(new Paragraph(new Run(markdown)));
                return doc;
            }
        }

        // ─── Post-processing ──────────────────────────────────────────────────────

        private static void PostProcess(FlowDocument doc, CodeBlockContext ctx)
        {
            try
            {
                // Markdig.Wpf sets FlowDocument.Foreground="Black" and sometimes a light Background.
                // Clear both at the document root so the FlowDocumentScrollViewer.Foreground binding
                // (which carries the VS theme text color) wins for all text in the document.
                if (IsBlackForeground(doc.Foreground))
                    doc.ClearValue(TextElement.ForegroundProperty);
                if (IsLightBackground(doc.Background))
                    doc.ClearValue(TextElement.BackgroundProperty);

                WalkBlocks(doc.Blocks, ctx);
            }
            catch
            {
                // Never break rendering on a post-processing error.
            }
        }

        private static void WalkBlocks(BlockCollection blocks, CodeBlockContext ctx)
        {
            // Snapshotted, not a live foreach: every Block/Inline in a FlowDocument shares one
            // underlying TextContainer, so replacing/mutating an EARLIER sibling bumps a version
            // counter that invalidates THIS loop's own enumerator over the outer BlockCollection
            // too - a `foreach` over `blocks` throws "Collection was modified" on its next
            // MoveNext() once any earlier block has been touched that way. PostProcess's own
            // try/catch was silently swallowing this, so every document with 2+ top-level blocks
            // (a command followed by its output, for instance) quietly stopped processing after the
            // first one - found live 2026-09-05 as "the output block still has the old ugly
            // highlight" one block down from a command block that looked fine.
            foreach (var block in blocks.Cast<Block>().ToList())
            {
                // Markdig.Wpf's own tell for "this Paragraph came from a fenced/indented code
                // block": a light background sized for a white page. Replaced wholesale with a
                // header+body chrome (language/filename + copy button over the actual code) rather
                // than just recolored in place, to match a real code-editor card instead of a flat
                // highlighted rectangle - see docs/Phase 24.
                if (block is Paragraph codePara && IsLightBackground(codePara.Background))
                {
                    string? language = ctx.NextLanguage();
                    string? filePath = ctx.ConsumePrimaryFilePath();

                    string rawCode = new TextRange(codePara.ContentStart, codePara.ContentEnd).Text;

                    // Neutralize Markdig.Wpf's own black-foreground default before any recoloring
                    // below - ApplyTokenColors leaves an unsupported language's Inlines completely
                    // untouched (see its own doc comment), and would otherwise render as literal
                    // black text regardless of VS theme now that this whole block no longer goes
                    // through FixupParagraph's black-clearing at all.
                    if (IsBlackForeground(codePara.Foreground))
                        codePara.ClearValue(TextElement.ForegroundProperty);
                    foreach (Run run in codePara.Inlines.OfType<Run>())
                        if (IsBlackForeground(run.Foreground))
                            run.ClearValue(TextElement.ForegroundProperty);

                    bool isDiff = string.Equals(language, "diff", StringComparison.OrdinalIgnoreCase);
                    if (isDiff)
                        ApplyDiffColors(codePara.Inlines);
                    else
                        ApplyTokenColors(codePara, language, rawCode);

                    Section section = new()
                    {
                        Background = s_codeBg,
                        BorderBrush = s_codeBorderBrush,
                        BorderThickness = new Thickness(1),
                        Margin = codePara.Margin,
                        Padding = new Thickness(0),
                    };

                    blocks.InsertBefore(codePara, section);
                    blocks.Remove(codePara);

                    codePara.ClearValue(TextElement.BackgroundProperty);
                    codePara.BorderThickness = new Thickness(0);
                    codePara.Margin = new Thickness(10, 6, 10, 10);

                    section.Blocks.Add(BuildCodeHeader(language, filePath, rawCode));
                    section.Blocks.Add(codePara);
                    continue;
                }

                WalkBlock(block, ctx);
            }
        }

        private static void WalkBlock(Block block, CodeBlockContext ctx)
        {
            switch (block)
            {
                case Paragraph para:
                    FixupParagraph(para);
                    break;

                case Section section:
                    if (IsLightBackground(section.Background))
                        section.Background = s_codeBg;
                    if (IsBlackForeground(section.Foreground))
                        section.ClearValue(TextElement.ForegroundProperty);
                    WalkBlocks(section.Blocks, ctx);
                    break;

                case List list:
                    // Same shared-TextContainer hazard as WalkBlocks: WalkBlocks(li.Blocks) can
                    // mutate an earlier list item, which would invalidate this loop's own live
                    // enumerator over ListItems on its next MoveNext() - snapshot first.
                    foreach (ListItem li in list.ListItems.Cast<ListItem>().ToList())
                    {
                        WalkBlocks(li.Blocks, ctx);
                        OverrideStyledForeground(li);
                    }
                    break;

                case Table table:
                    // Same hazard, three levels deep - every level snapshotted for the same reason.
                    foreach (var rg in table.RowGroups.Cast<TableRowGroup>().ToList())
                        foreach (TableRow row in rg.Rows.Cast<TableRow>().ToList())
                            foreach (TableCell cell in row.Cells.Cast<TableCell>().ToList())
                            {
                                WalkBlocks(cell.Blocks, ctx);
                                OverrideStyledForeground(cell);
                            }
                    break;
            }

            // Applied LAST, after the black-foreground clearing above (FixupParagraph, the Section
            // case): that existing check reads the CURRENT resolved Foreground and clears it if it
            // looks black, which would immediately undo this override too, since an unresolved
            // resource reference reads back as TextElement.Foreground's own default (black) before
            // ever reaching a real resource tree - and even once resolved for real inside VS, VS's
            // light theme's real text color legitimately IS near-black, which the same check can't
            // tell apart from Markdig's hardcoded one. Running last means this is always the
            // final, winning local value regardless of what either check decided.
            OverrideStyledForeground(block);
        }

        /// <summary>
        /// Markdig.Wpf assigns several block/row/cell types (headings, blockquotes, tables, ...)
        /// one of its own baked-in styles (<c>Styles.HeadingNStyleKey</c> and friends, resolved via
        /// a <c>ComponentResourceKey</c> against the package's own embedded theme resources - which
        /// is why loading XAML with no matching resource dictionary merged in doesn't throw). Those
        /// styles were authored for a plain white page and set their own Foreground.
        /// <para>
        /// Crucially, a Style's setters are not resolved until the element is actually attached to
        /// a live visual tree - so checking the *current* Foreground value right after
        /// <c>XamlReader.Load</c> (as <see cref="IsBlackForeground"/> does for plain, unstyled
        /// elements) can never see it here: at this point it is not yet a "black" value to clear,
        /// it is a Style Setter that only takes effect later, once rendered. A resource-reference
        /// value set here is still a *local* value in WPF's property-precedence terms, so it wins
        /// over that later-applied Style Setter regardless of timing - unlike clearing, which would
        /// do nothing useful before the style has ever applied.
        /// </para>
        /// </summary>
        private static void OverrideStyledForeground(FrameworkContentElement element)
        {
            if (element.ReadLocalValue(FrameworkContentElement.StyleProperty) == DependencyProperty.UnsetValue)
                return;

            element.SetResourceReference(TextElement.ForegroundProperty,
                Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
        }

        private static void FixupParagraph(Paragraph para)
        {
            // Remove any explicitly-set black foreground so VS theme text color is inherited.
            if (IsBlackForeground(para.Foreground))
                para.ClearValue(TextElement.ForegroundProperty);

            // A fenced/indented code block never reaches here - WalkBlocks intercepts and replaces
            // those before calling WalkBlock/FixupParagraph at all. This is prose, so file
            // references are worth linkifying.
            LinkifyFileReferences(para);   // user-typed "@path#Lstart-Lend" mentions
            LinkifyBareFilenames(para);    // Claude's own prose ("...in ClaudeCodePackage.cs...")

            // Inline `code` spans can come through as a bare Run with its own Background rather
            // than wrapped in a Span - normalize those directly (see FixupSpan for why).
            foreach (var run in para.Inlines.OfType<Run>())
            {
                if (IsLightBackground(run.Background))
                {
                    run.Background = s_codeBg;
                    run.FontFamily = s_inlineCodeFont;
                }
            }

            // Walk inline containers (Span, Hyperlink, etc.) for nested runs.
            foreach (var inline in para.Inlines.OfType<Span>())
                FixupSpan(inline);
        }

        /// <summary>
        /// Builds the strip above a code block's body: the language name, or - when this block is
        /// about one specific file (an Edit's diff, a Write's new content) - that file's name as a
        /// clickable link that opens it in the real editor, plus a copy button on the right. Modeled
        /// on GitHub Copilot Chat's own code-block header rather than the plain floating corner
        /// button this replaced (see docs/Phase 24).
        /// </summary>
        private static Paragraph BuildCodeHeader(string? language, string? filePath, string code)
        {
            Paragraph header = new()
            {
                Margin = new Thickness(0),
                Padding = new Thickness(10, 5, 6, 5),
                Background = s_codeHeaderBg,
                BorderBrush = s_codeBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                FontSize = 11,
            };
            header.SetResourceReference(TextElement.ForegroundProperty,
                Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);

            if (!string.IsNullOrEmpty(filePath))
            {
                Hyperlink link = new(new Run(Path.GetFileName(filePath)))
                {
                    ToolTip = $"Open {filePath}",
                };
                // Fire-and-forget rather than an async lambda: OpenReferenceAsync already
                // try/catches its entire body, so nothing here can throw unobserved.
                link.Click += (_, __) => _ = OpenFileReferenceAsync(filePath!, null, null);
                header.Inlines.Add(link);
            }
            else
            {
                header.Inlines.Add(new Run(string.IsNullOrEmpty(language) ? "text" : language!));
            }

            header.Inlines.Add(BuildCopyFloater(code));
            return header;
        }

        private static Floater BuildCopyFloater(string code)
        {
            Button button = new()
            {
                Content = s_copyGlyph,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = s_symbolFont,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Opacity = 0.7,
                ToolTip = "Copy this code block",
                Focusable = false,
            };
            button.SetResourceReference(Control.ForegroundProperty,
                Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);

            button.MouseEnter += (_, __) => { button.Opacity = 1.0; button.Background = s_codeBorderBrush; };
            button.MouseLeave += (_, __) => { button.Opacity = 0.7; button.Background = Brushes.Transparent; };
            button.Click += (_, __) => CopyToClipboard(button, code);

            return new Floater(new BlockUIContainer(button)
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
            })
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Width = 28,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
            };
        }

        private static void CopyToClipboard(Button button, string code)
        {
            try
            {
                Clipboard.SetText(code);
                button.Content = s_copiedGlyph;
                button.ToolTip = "Copied";

                // Revert the icon so the button does not read "Copied" forever on a block the
                // user copied ten minutes ago.
                DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (_, __) =>
                {
                    timer.Stop();
                    button.Content = s_copyGlyph;
                    button.ToolTip = "Copy this code block";
                };
                timer.Start();
            }
            catch
            {
                // Another process can hold the clipboard open; say so rather than failing silently.
                button.Content = s_copyFailedGlyph;
                button.ToolTip = "Copy failed";
            }
        }

        /// <summary>
        /// Recolors a code block's own text per-token using the real Visual Studio editor's own
        /// classification colors (see <see cref="Core.ClaudeCodePackage.GetClassificationForeground"/>)
        /// rather than a hand-picked palette, so it tracks the user's actual theme/Fonts-and-Colors
        /// setup - the same source GitHub Copilot Chat's own code blocks read from. A language with
        /// no tokenizer profile (see <see cref="SyntaxHighlighter"/>) is left exactly as Markdig.Wpf
        /// rendered it - flat text in the theme's plain foreground, same as before this phase.
        /// </summary>
        private static void ApplyTokenColors(Paragraph contentPara, string? language, string rawCode)
        {
            IReadOnlyList<SyntaxToken> tokens = SyntaxHighlighter.Tokenize(language, rawCode);
            if (tokens.Count == 1 && tokens[0].Kind == TokenKind.Plain)
                return;

            contentPara.Inlines.Clear();
            foreach (SyntaxToken token in tokens)
            {
                Run run = new(token.Text) { FontFamily = s_inlineCodeFont };
                Brush? brush = GetTokenBrush(token.Kind);
                if (brush != null)
                    run.Foreground = brush;
                contentPara.Inlines.Add(run);
            }
        }

        private static Brush? GetTokenBrush(TokenKind kind)
        {
            string? classificationName = kind switch
            {
                TokenKind.Keyword => PredefinedClassificationTypeNames.Keyword,
                TokenKind.String => PredefinedClassificationTypeNames.String,
                TokenKind.Comment => PredefinedClassificationTypeNames.Comment,
                TokenKind.Number => PredefinedClassificationTypeNames.Number,
                _ => null,
            };
            return classificationName == null
                ? null
                : TeronClaudeCodeVS.Core.ClaudeCodePackage.Instance?.GetClassificationForeground(classificationName);
        }

        // Requires a real extension on the path segment (Class1.cs, src/Foo/Bar.tsx) so it never
        // matches a bare "@word" - the shape of a code decorator (@property, @Override,
        // @Injectable()) or an @-mention, neither of which is a file reference.
        private static readonly Regex s_fileRefPattern = new(
            @"@(?<path>(?:[\w.\-]+[/\\])*[\w.\-]+\.[A-Za-z0-9]{1,10})(?:#L(?<start>\d+)(?:-L(?<end>\d+))?)?",
            RegexOptions.Compiled);

        /// <summary>
        /// UX. Turns every "@path#Lstart-Lend" token in a plain-text run into a clickable link,
        /// so a reference the composer wrote (see ClaudeCodeChatControl.InsertContextReference)
        /// can be followed back to the file/line it named once the message has been sent, instead
        /// of only ever being read as inert text.
        /// </summary>
        private static void LinkifyFileReferences(Paragraph para)
        {
            foreach (Run run in para.Inlines.OfType<Run>().ToList())
            {
                // Inline `code` spans are still on their original Markdig light background at
                // this point (the fixup loop that neutralizes it runs after this one) - skip
                // them, since matching inside real code is exactly what the regex is guarding
                // against by requiring an extension.
                if (IsLightBackground(run.Background)) continue;
                LinkifyRun(para.Inlines, run);
            }
        }

        private static void LinkifyRun(InlineCollection inlines, Run run)
        {
            string text = run.Text;
            MatchCollection matches = s_fileRefPattern.Matches(text);
            if (matches.Count == 0) return;

            Inline anchor = run;
            int last = 0;

            foreach (Match m in matches)
            {
                if (m.Index > last)
                    inlines.InsertAfter(anchor, anchor = new Run(text.Substring(last, m.Index - last)));

                string path = m.Groups["path"].Value;
                int? start = m.Groups["start"].Success ? int.Parse(m.Groups["start"].Value) : null;
                int? end = m.Groups["end"].Success ? int.Parse(m.Groups["end"].Value) : start;

                Hyperlink link = new(new Run(m.Value))
                {
                    ToolTip = start.HasValue ? $"Open {path} at line {start}" : $"Open {path}",
                };
                // Fire-and-forget rather than an async lambda: OpenReferenceAsync already
                // try/catches its entire body, so nothing here can throw unobserved.
                link.Click += (_, __) => _ = OpenFileReferenceAsync(path, start, end);
                inlines.InsertAfter(anchor, anchor = link);

                last = m.Index + m.Length;
            }

            if (last < text.Length)
                inlines.InsertAfter(anchor, new Run(text.Substring(last)));

            inlines.Remove(run);
        }

        // A plain word.ext shape, no "@" required and no path separators - just enough to find a
        // candidate. Loose on purpose: real precision comes from checking the candidate against
        // the actual project index below, not from the regex.
        private static readonly Regex s_bareFileNamePattern = new(
            @"\b(?<name>[A-Za-z0-9_\-]+\.[A-Za-z0-9]{1,10})\b",
            RegexOptions.Compiled);

        /// <summary>
        /// UX. Auto-links a bare filename Claude's own prose mentions ("...in
        /// ClaudeCodePackage.cs...") against the real, currently-indexed project files - not just
        /// the user-typed "@path" mentions <see cref="LinkifyFileReferences"/> handles. Requested
        /// live 2026-09-05 after a GitHub Copilot Chat comparison screenshot showed exactly this.
        /// Deliberately conservative: a candidate word only becomes a link if its filename exactly
        /// matches (case-insensitive) a real file already discovered by the composer's own
        /// "@"-mention index (<see cref="Core.ClaudeCodePackage.IndexedProjectFiles"/>) - a version
        /// number, "e.g.", or a filename Claude invented that isn't actually in this workspace is
        /// left as plain text rather than risking a dead or wrong link. Runs strictly after
        /// LinkifyFileReferences and re-queries Inlines fresh, so an already-linked "@path" mention
        /// (now a Hyperlink, not a Run) can never be double-processed.
        /// </summary>
        private static void LinkifyBareFilenames(Paragraph para)
        {
            string[] indexedFiles = TeronClaudeCodeVS.Core.ClaudeCodePackage.Instance?.IndexedProjectFiles ?? [];
            if (indexedFiles.Length == 0) return;

            foreach (Run run in para.Inlines.OfType<Run>().ToList())
            {
                if (IsLightBackground(run.Background)) continue; // inline `code` span - never auto-link inside real code
                LinkifyBareFilenameRun(para.Inlines, run, indexedFiles);
            }
        }

        private static void LinkifyBareFilenameRun(InlineCollection inlines, Run run, string[] indexedFiles)
        {
            string text = run.Text;
            MatchCollection matches = s_bareFileNamePattern.Matches(text);
            if (matches.Count == 0) return;

            Inline anchor = run;
            int last = 0;
            bool any = false;

            foreach (Match m in matches)
            {
                string candidate = m.Groups["name"].Value;
                string? fullPath = indexedFiles.FirstOrDefault(f =>
                    string.Equals(Path.GetFileName(f), candidate, StringComparison.OrdinalIgnoreCase));
                if (fullPath == null) continue;

                if (m.Index > last)
                    inlines.InsertAfter(anchor, anchor = new Run(text.Substring(last, m.Index - last)));

                Hyperlink link = new(new Run(candidate)) { ToolTip = $"Open {fullPath}" };
                link.Click += (_, __) => _ = OpenFileReferenceAsync(fullPath, null, null);
                inlines.InsertAfter(anchor, anchor = link);

                last = m.Index + m.Length;
                any = true;
            }

            if (!any) return;

            if (last < text.Length)
                inlines.InsertAfter(anchor, new Run(text.Substring(last)));

            inlines.Remove(run);
        }

        /// <summary>Resolves a possibly-relative file path against the current workspace root and
        /// opens it in a real VS editor tab, optionally at a line range. Public so other UI (the
        /// Active File/Selection attachment chip, not just an inline "@path" link) can reuse the
        /// exact same open behavior.</summary>
        public static async System.Threading.Tasks.Task OpenFileReferenceAsync(string path, int? startLine, int? endLine)
        {
            try
            {
                string resolved = path;
                if (!Path.IsPathRooted(resolved))
                {
                    string root = await TeronClaudeCodeVS.Core.VsIdeToolHandlers.GetWorkingDirectoryAsync();
                    resolved = Path.Combine(root, resolved);
                }

                await TeronClaudeCodeVS.Core.VsIdeToolHandlers.OpenFileAtLineAsync(resolved, startLine, endLine);
            }
            catch
            {
                // The file may have moved or been deleted since the message was sent - a broken
                // reference is not worth interrupting the user over.
            }
        }

        private static void FixupSpan(Span span)
        {
            if (IsBlackForeground(span.Foreground))
                span.ClearValue(TextElement.ForegroundProperty);

            // Markdig.Wpf renders inline `code` spans with a light background sized for a white
            // page; left as-is it shows as a stark, undifferentiated light block in a dark theme
            // instead of a subtle inline-code chip. Same theme-neutral tint as fenced code blocks,
            // so it reads correctly in both themes automatically instead of needing its own
            // hardcoded color.
            if (IsLightBackground(span.Background))
            {
                span.Background = s_codeBg;
                span.FontFamily = s_inlineCodeFont;
            }

            foreach (var run in span.Inlines.OfType<Run>())
            {
                if (IsLightBackground(run.Background))
                {
                    run.Background = s_codeBg;
                    run.FontFamily = s_inlineCodeFont;
                }
            }

            foreach (var child in span.Inlines.OfType<Span>())
                FixupSpan(child);
        }

        /// <summary>
        /// Markdig.Wpf renders an entire fenced code block as ONE Run with embedded '\n' characters
        /// (confirmed live 2026-09-05, not one Run per line as the coloring below used to assume) -
        /// WPF's text layout still wraps that Run onto separate visual lines at each '\n', so it
        /// always LOOKED right, but every line in the block was silently getting the same single
        /// color (whatever the very first line's prefix happened to be), never real per-line +/-
        /// coloring. Fixed by splitting each Run on '\n' into its own colored Run, rejoined with
        /// explicit LineBreaks so the line-by-line layout is unchanged.
        /// </summary>
        private static void ApplyDiffColors(InlineCollection inlines)
        {
            foreach (Run run in inlines.OfType<Run>().ToList())
            {
                string[] lines = run.Text.Split('\n');

                // Only activate for blocks that actually look like a unified diff.
                bool hasDiff = lines.Any(l =>
                    l.StartsWith("+", StringComparison.Ordinal) || l.StartsWith("-", StringComparison.Ordinal));
                if (!hasDiff) continue;

                Inline anchor = run;
                for (int i = 0; i < lines.Length; i++)
                {
                    Run lineRun = new(lines[i]);
                    Brush? color = DiffLineColor(lines[i]);
                    if (color != null) lineRun.Foreground = color;

                    inlines.InsertAfter(anchor, anchor = lineRun);
                    if (i < lines.Length - 1)
                        inlines.InsertAfter(anchor, anchor = new LineBreak());
                }

                inlines.Remove(run);
            }
        }

        private static Brush? DiffLineColor(string line)
        {
            if (line.StartsWith("+++", StringComparison.Ordinal) ||
                line.StartsWith("---", StringComparison.Ordinal) ||
                line.StartsWith("@@", StringComparison.Ordinal))
                return s_diffHunk;
            if (line.StartsWith("+", StringComparison.Ordinal)) return s_diffAdd;
            if (line.StartsWith("-", StringComparison.Ordinal)) return s_diffRem;
            return null;
        }

        private static bool IsLightBackground(Brush? brush)
        {
            if (brush is SolidColorBrush scb)
            {
                var c = scb.Color;
                // Light neutral colors commonly used by Markdig.Wpf for code blocks.
                return c.A > 0x80 && c.R > 0xCC && c.G > 0xCC && c.B > 0xCC;
            }
            return false;
        }

        private static bool IsBlackForeground(Brush? brush)
        {
            // Only clear truly-black (or near-black) explicit foreground values —
            // not coloured runs set by syntax highlighting.
            if (brush is SolidColorBrush scb)
            {
                var c = scb.Color;
                return c.A > 0x80 && c.R < 0x30 && c.G < 0x30 && c.B < 0x30;
            }
            return false;
        }
    }
}
