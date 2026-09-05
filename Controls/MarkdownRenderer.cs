using Markdig;
using Microsoft.VisualStudio.Language.StandardClassification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        // Card background for an INLINE `code` span only (a word or two sitting in the middle of a
        // prose line) - not the fenced-code-block chrome, see GetCodeBlockBackground for that. Went
        // through two revisions on 2026-09-05: first just bumping a neutral grey's alpha
        // (0x18 -> 0x40), which fixed "barely visible" but was called out live as still the wrong
        // idea - a shade/alpha tweak on a neutral tone reads as a washed-out chip either way, not
        // something that actually stands out. Switched to the app's own accent hue (ChatTheme.xaml's
        // ClaudeAccentBrush #D97757, kept in sync manually here since that dictionary isn't
        // reachable from this static, no-visual-tree renderer) at low alpha - alpha-blending over
        // whatever the real background is keeps the "never needs a separate light/dark value"
        // property, but a warm, branded tint reads as an intentional highlight on both VS light and
        // dark instead of a generic grey box. A subtle translucent tint like this is fine for a
        // couple of inline words; see GetCodeBlockBackground for why it reads as broken chrome once
        // it is the fill for an entire multi-line block instead.
        private static readonly SolidColorBrush s_codeBg = Frozen(Color.FromArgb(0x33, 0xD9, 0x77, 0x57));

        private static readonly SolidColorBrush s_codeBorderBrush = Frozen(Color.FromArgb(0x40, 0x80, 0x80, 0x80));

        // The fill for BOTH the header strip and the body of a fenced code block. Header and body
        // deliberately share this exact brush; the header's own bottom border is the only seam
        // between them, matching GitHub Copilot Chat's one-flat-surface-plus-a-divider look.
        //
        // Went through THREE revisions chasing this. First: a translucent accent tint copied from
        // the inline s_codeBg - reads fine for a couple of words, reads as a washed-out, oddly
        // colored box for a whole block (2026-09-05). Second: the real VS editor's own background
        // via ClaudeCodePackage.GetEditorBackground() (IEditorFormatMapService's "Plain Text" ->
        // "Background", the same source Fonts and Colors' own preview swatch reads from) - shape
        // confirmed correct via reflection against the real SDK assembly, but called out live
        // 2026-09-06 as STILL solid white on an otherwise dark theme. "Plain Text"'s configured
        // background is apparently not a reliable proxy for what the user actually sees the editor
        // painted with - a real, confirmed-live divergence between the documented API contract and
        // its live behavior, not something to keep chasing with a third VS SDK lookup. Settled on
        // the same self-adapting technique CardBackgroundBrush (ChatTheme.xaml) already uses
        // everywhere else in this UI for a neutral card surface: a low-alpha grey that blends over
        // whatever the real background is, so it is correct on any theme by construction instead
        // of by asking VS what that theme's color happens to be. Slightly stronger alpha than
        // CardBackgroundBrush's 0x14 (a code block should read as a distinct surface, not just
        // another card).
        private static readonly SolidColorBrush s_codeBlockBg = Frozen(Color.FromArgb(0x22, 0x80, 0x80, 0x80));

        private static Brush GetCodeBlockBackground() => s_codeBlockBg;
        private static readonly FontFamily s_inlineCodeFont = new("Consolas");

        // Diff line colors (same hues as GitHub's diff view).
        private static readonly SolidColorBrush s_diffAdd = Frozen(Color.FromArgb(0xFF, 0x3F, 0xB9, 0x50));
        private static readonly SolidColorBrush s_diffRem = Frozen(Color.FromArgb(0xFF, 0xE5, 0x48, 0x4D));
        private static readonly SolidColorBrush s_diffHunk = Frozen(Color.FromArgb(0xFF, 0x79, 0xB8, 0xFF));

        // Code-block copy button's three icon states, from Kaloyan's own SVG set
        // (Resources/copy|done|warning_light|dark.svg). Kept as local Geometry fields rather than
        // added to Resources/IconGeometries.xaml: that dictionary is merged into
        // ClaudeCodeChatControl.xaml's own UserControl.Resources, unreachable from this static,
        // no-visual-tree renderer (same reason the code-block brushes above are hand-rolled instead
        // of pulled from ChatTheme.xaml). Selected via ThemeService.Instance.IsDarkTheme - reading
        // that property alone never touches the real VS SDK theme API (only Refresh() does, called
        // once from ClaudeCodeChatControl.OnLoaded), so this stays safe under the xUnit tests too.
        private static readonly Geometry s_copyIconLight = Geometry.Parse("F1 m13 20a5.006 5.006 0 0 0 5-5v-8.757a3.972 3.972 0 0 0 -1.172-2.829l-2.242-2.242a3.972 3.972 0 0 0 -2.829-1.172h-4.757a5.006 5.006 0 0 0 -5 5v10a5.006 5.006 0 0 0 5 5zm-9-5v-10a3 3 0 0 1 3-3s4.919 .014 5 .024v1.976a2 2 0 0 0 2 2h1.976c.01 .081 .024 9 .024 9a3 3 0 0 1 -3 3h-6a3 3 0 0 1 -3-3zm18-7v11a5.006 5.006 0 0 1 -5 5h-9a1 1 0 0 1 0-2h9a3 3 0 0 0 3-3v-11a1 1 0 0 1 2 0z");
        private static readonly Geometry s_copyIconDark = Geometry.Parse("F1 m13 4a1 1 0 0 0 1 1h3.966a2.981 2.981 0 0 0 -.811-1.728l-2.284-2.359a3.011 3.011 0 0 0 -1.871-.884zm-2 0v-4h-4a5.006 5.006 0 0 0 -5 5v10a5.006 5.006 0 0 0 5 5h6a5.006 5.006 0 0 0 5-5v-8h-4a3 3 0 0 1 -3-3zm6 20h-9a1 1 0 0 1 0-2h9a3 3 0 0 0 3-3v-11a1 1 0 0 1 2 0v11a5.006 5.006 0 0 1 -5 5z");
        private static readonly Geometry s_doneIconLight = Geometry.Parse("F1 M19,0H5A5.006,5.006,0,0,0,0,5V19a5.006,5.006,0,0,0,5,5H19a5.006,5.006,0,0,0,5-5V5A5.006,5.006,0,0,0,19,0Zm3,19a3,3,0,0,1-3,3H5a3,3,0,0,1-3-3V5A3,3,0,0,1,5,2H19a3,3,0,0,1,3,3Z M9.333,15.919,5.414,12A1,1,0,0,0,4,12H4a1,1,0,0,0,0,1.414l3.919,3.919a2,2,0,0,0,2.829,0L20,8.081a1,1,0,0,0,0-1.414h0a1,1,0,0,0-1.414,0Z");
        private static readonly Geometry s_doneIconDark = Geometry.Parse("F1 M405.333,0H106.667C47.786,0.071,0.071,47.786,0,106.667v298.667C0.071,464.214,47.786,511.93,106.667,512h298.667 C464.214,511.93,511.93,464.214,512,405.333V106.667C511.93,47.786,464.214,0.071,405.333,0z M426.667,172.352L229.248,369.771 c-16.659,16.666-43.674,16.671-60.34,0.012c-0.004-0.004-0.008-0.008-0.012-0.012l-83.563-83.541 c-8.348-8.348-8.348-21.882,0-30.229s21.882-8.348,30.229,0l83.541,83.541l197.44-197.419c8.348-8.318,21.858-8.294,30.176,0.053 C435.038,150.524,435.014,164.034,426.667,172.352z");
        private static readonly Geometry s_warningIconLight = Geometry.Parse("F1 M11,13V7c0-.55,.45-1,1-1s1,.45,1,1v6c0,.55-.45,1-1,1s-1-.45-1-1Zm1,2c-.83,0-1.5,.67-1.5,1.5s.67,1.5,1.5,1.5,1.5-.67,1.5-1.5-.67-1.5-1.5-1.5Zm11.58,4.88c-.7,1.35-2.17,2.12-4.01,2.12H4.44c-1.85,0-3.31-.77-4.01-2.12-.71-1.36-.51-3.1,.5-4.56L8.97,2.6c.71-1.02,1.83-1.6,3.03-1.6s2.32,.58,3,1.57l8.08,12.77c1.01,1.46,1.2,3.19,.49,4.54Zm-2.15-3.42s-.02-.02-.02-.04L13.34,3.67c-.29-.41-.79-.67-1.34-.67s-1.05,.26-1.36,.71L2.59,16.42c-.62,.88-.76,1.84-.4,2.53,.35,.68,1.15,1.05,2.24,1.05h15.12c1.09,0,1.89-.37,2.24-1.05,.36-.69,.22-1.65-.37-2.49Z");
        private static readonly Geometry s_warningIconDark = Geometry.Parse("F1 M23.08,15.33L15,2.57c-.68-.98-1.81-1.57-3-1.57s-2.32,.58-3.03,1.6L.93,15.31c-1.02,1.46-1.21,3.21-.5,4.56,.7,1.35,2.17,2.12,4.01,2.12h15.12c1.85,0,3.31-.77,4.01-2.12,.7-1.35,.51-3.09-.49-4.54ZM11,7c0-.55,.45-1,1-1s1,.45,1,1v6c0,.55-.45,1-1,1s-1-.45-1-1V7Zm1,12c-.83,0-1.5-.67-1.5-1.5s.67-1.5,1.5-1.5,1.5,.67,1.5,1.5-.67,1.5-1.5,1.5Z");

        private static Geometry CopyIcon => TeronClaudeCodeVS.Controls.ThemeService.Instance.IsDarkTheme ? s_copyIconLight : s_copyIconDark;
        private static Geometry DoneIcon => TeronClaudeCodeVS.Controls.ThemeService.Instance.IsDarkTheme ? s_doneIconLight : s_doneIconDark;
        private static Geometry WarningIcon => TeronClaudeCodeVS.Controls.ThemeService.Instance.IsDarkTheme ? s_warningIconLight : s_warningIconDark;

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
                        Background = GetCodeBlockBackground(),
                        BorderBrush = s_codeBorderBrush,
                        BorderThickness = new Thickness(1),
                        Margin = codePara.Margin,
                        Padding = new Thickness(0),
                    };

                    blocks.InsertBefore(codePara, section);
                    blocks.Remove(codePara);

                    // Painted with the exact same brush as the Section around it, not cleared to
                    // fall through to it - found live 2026-09-06 still showing Markdig.Wpf's own
                    // light default even after this same brush visibly took effect on the header
                    // (a plain Border built fresh, nowhere near Markdig.Wpf's renderer). Whatever
                    // "cleared" was actually resolving to for this specific paragraph, painting it
                    // explicitly removes the ambiguity instead of relying on it.
                    codePara.Background = GetCodeBlockBackground();
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
                        section.Background = GetCodeBlockBackground();
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
        /// clickable link that opens it in the real editor, plus a copy button and (for real,
        /// insertable file content only - see <see cref="ShouldShowInsertActions"/>) the
        /// editor-actions dropdown on the right.
        /// <para>
        /// A native <see cref="DockPanel"/> inside a <see cref="BlockUIContainer"/>, not a
        /// Paragraph built from Inlines/Floaters (Phase 24's original design here, and the "Insert
        /// at Cursor" dropdown's own first cut) - live testing 2026-09-06 found the taller dropdown
        /// button rendering outside the header strip entirely. FlowDocument's Floater does
        /// CSS-float-style layout with no guaranteed vertical containment inside the paragraph that
        /// hosts it; a real WPF panel lays out deterministically instead of relying on that.
        /// </para>
        /// </summary>
        private static Block BuildCodeHeader(string? language, string? filePath, string code)
        {
            bool showInsertActions = ShouldShowInsertActions(language, filePath);

            Border headerBorder = new()
            {
                Background = GetCodeBlockBackground(),
                BorderBrush = s_codeBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 5, 6, 5),
            };

            DockPanel dock = new();

            StackPanel actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (showInsertActions)
                actions.Children.Add(BuildInsertActionsButton(code));
            actions.Children.Add(BuildCopyButton(code));
            DockPanel.SetDock(actions, Dock.Right);
            dock.Children.Add(actions);

            TextBlock label = new()
            {
                // Bumped from 11 (FontSizeChrome) to 12 (FontSizeComposerChip) - called out live
                // 2026-09-06 as too small next to the code itself. Literal, not a resource
                // reference, for the same reason the brushes above are hand-rolled: this is a
                // static, no-visual-tree renderer with no easy path to ChatTheme.xaml's tokens.
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty,
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
                label.Inlines.Add(link);
            }
            else
            {
                label.Text = string.IsNullOrEmpty(language) ? "text" : language!;
            }

            dock.Children.Add(label);
            headerBorder.Child = dock;

            return new BlockUIContainer(headerBorder) { Margin = new Thickness(0) };
        }

        /// <summary>
        /// GitHub Copilot Chat's own "Insert at Cursor" dropdown only ever appears on a real
        /// generated-code proposal, never on a command's output or a plain diff. Flagged live
        /// 2026-09-06 on a tool call's "**Output:**" wrap (a plain-text success/failure message,
        /// nothing to insert) - traced to <see cref="ViewModels.ContentBlocks.ToolCallViewModel"/>
        /// passing the tool's file path to a block that was never that file's own content (fixed
        /// there too). Restated here as a second, independent guard: a block only qualifies when it
        /// is tied to a specific file (Write's whole content, an Edit's own code) AND is not a
        /// "```diff" fence - a unified diff's +/- lines are not valid file content to insert either.
        /// </summary>
        private static bool ShouldShowInsertActions(string? language, string? filePath) =>
            !string.IsNullOrEmpty(filePath) &&
            !string.Equals(language, "diff", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The "Insert at Cursor ▾" dropdown: Insert at Cursor / Insert in New File / Apply in
        /// Active Document - see <see cref="Core.VsIdeToolHandlers"/> for what each one actually
        /// does.
        /// </summary>
        private static Button BuildInsertActionsButton(string code)
        {
            Button insertButton = new()
            {
                Content = "Insert at Cursor ▾",
                FontSize = 11,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 4, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = s_codeBorderBrush,
                Cursor = System.Windows.Input.Cursors.Hand,
                Opacity = 0.85,
                ToolTip = "Insert, apply, or open this code in the editor",
                Focusable = false,
            };
            insertButton.SetResourceReference(Control.ForegroundProperty,
                Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);
            insertButton.MouseEnter += (_, __) => { insertButton.Opacity = 1.0; insertButton.Background = s_codeBorderBrush; };
            insertButton.MouseLeave += (_, __) => { insertButton.Opacity = 0.85; insertButton.Background = Brushes.Transparent; };

            ContextMenu menu = new() { PlacementTarget = insertButton, Placement = PlacementMode.Bottom };
            menu.Items.Add(BuildHeaderActionMenuItem("Insert at Cursor",
                () => _ = Core.VsIdeToolHandlers.InsertAtCursorAsync(code)));
            menu.Items.Add(BuildHeaderActionMenuItem("Insert in New File",
                () => _ = Core.VsIdeToolHandlers.InsertInNewFileAsync(code)));
            menu.Items.Add(BuildHeaderActionMenuItem("Apply in Active Document",
                () => _ = Core.VsIdeToolHandlers.ApplyInActiveDocumentAsync(code)));
            insertButton.Click += (_, __) => menu.IsOpen = true;

            return insertButton;
        }

        private static MenuItem BuildHeaderActionMenuItem(string text, Action action)
        {
            MenuItem item = new() { Header = text };
            item.Click += (_, __) => action();
            return item;
        }

        /// <summary>
        /// A flat, square icon tile with a rounded-corner hover fill, parsed from XAML the same way
        /// this file already builds a FlowDocument from markup elsewhere - simpler than assembling
        /// a ControlTemplate's visual tree by hand with FrameworkElementFactory. Replaces the
        /// default WPF Button chrome, which was called out live 2026-09-06 as producing a
        /// noticeably rectangular button (unequal horizontal/vertical padding around the icon) with
        /// a crude hard-edged hover rectangle.
        /// </summary>
        private static readonly ControlTemplate s_flatIconButtonTemplate = (ControlTemplate)XamlReader.Parse(
            """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                              TargetType="Button">
                <Border x:Name="Bg" Background="{TemplateBinding Background}" CornerRadius="4">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Bg" Property="Background" Value="#40808080"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
            """);

        private static Button BuildCopyButton(string code)
        {
            System.Windows.Shapes.Path icon = new() { Data = CopyIcon, Stretch = Stretch.Uniform, Width = 13, Height = 13 };
            icon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
                Microsoft.VisualStudio.Shell.VsBrushes.ToolWindowTextKey);

            Button button = new()
            {
                Content = icon,
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Template = s_flatIconButtonTemplate,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Copy this code block",
                Focusable = false,
            };

            button.Click += (_, __) => CopyToClipboard(button, icon, code);

            return button;
        }

        private static void CopyToClipboard(Button button, System.Windows.Shapes.Path icon, string code)
        {
            try
            {
                Clipboard.SetText(code);
                icon.Data = DoneIcon;
                button.ToolTip = "Copied";

                // Revert the icon so the button does not read "Copied" forever on a block the
                // user copied ten minutes ago.
                DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (_, __) =>
                {
                    timer.Stop();
                    icon.Data = CopyIcon;
                    button.ToolTip = "Copy this code block";
                };
                timer.Start();
            }
            catch
            {
                // Another process can hold the clipboard open; say so rather than failing silently.
                icon.Data = WarningIcon;
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

        // A plain word.ext shape, optionally preceded by its own directory segments
        // ("TestConsoleApp\Program.cs", not just "Program.cs") so a mention that already carries
        // folder context isn't truncated down to the bare leaf name before it ever reaches the
        // index lookup below. Lookaround instead of \b at the edges: \b can't sit in front of a
        // segment starting with '.' (".vscode\launch.json") because '.' isn't a word character,
        // so the transition from whitespace to '.' is not a word boundary at all.
        private static readonly Regex s_bareFileNamePattern = new(
            @"(?<![\w.\\/-])(?<name>(?:[A-Za-z0-9_.\-]+[\\/])*[A-Za-z0-9_\-]+\.[A-Za-z0-9]{1,10})(?![\w.\\/-])",
            RegexOptions.Compiled);

        /// <summary>
        /// UX. Auto-links a bare filename Claude's own prose mentions ("...in
        /// ClaudeCodePackage.cs...") against the real, currently-indexed project files - not just
        /// the user-typed "@path" mentions <see cref="LinkifyFileReferences"/> handles. Requested
        /// live 2026-09-05 after a GitHub Copilot Chat comparison screenshot showed exactly this.
        /// Deliberately conservative: a candidate word only becomes a link if it resolves to
        /// exactly one real file already discovered by the composer's own "@"-mention index (<see
        /// cref="Core.ClaudeCodePackage.IndexedProjectFiles"/>) - a version number, "e.g.", or a
        /// filename Claude invented that isn't actually in this workspace is left as plain text
        /// rather than risking a dead or wrong link. Runs strictly after LinkifyFileReferences and
        /// re-queries Inlines fresh, so an already-linked "@path" mention (now a Hyperlink, not a
        /// Run) can never be double-processed.
        /// <para>
        /// Two projects can genuinely have a same-named file in different folders (two
        /// Program.cs) - found live 2026-09-05 when a workspace listing mentioned both
        /// "TestConsoleApp\Program.cs" and "TestProjectClaude2\Program.cs" and only the first one
        /// in <see cref="Core.ClaudeCodePackage.IndexedProjectFiles"/> ever got linked, silently
        /// pointing BOTH mentions at the same file. A candidate that carries its own directory
        /// segments is now matched against that whole relative tail instead of just the leaf name;
        /// a bare leaf mention with no folder context still only links when it is unique across
        /// the project, and is left as plain text rather than guessing which of several
        /// same-named files it means.
        /// </para>
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
                string? fullPath = ResolveIndexedFile(candidate, indexedFiles);
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

        /// <summary>
        /// Resolves a bare-filename candidate against the real project index. A candidate that
        /// carries its own directory segments ("TestConsoleApp\Program.cs") is matched against the
        /// whole relative tail of each indexed path, not just the leaf name, so two files sharing a
        /// name in different folders resolve independently. A bare leaf name with no folder context
        /// only resolves when it is unique across the project - ambiguous either way means no link,
        /// never a guess.
        /// </summary>
        private static string? ResolveIndexedFile(string candidate, string[] indexedFiles)
        {
            if (candidate.IndexOfAny(['\\', '/']) >= 0)
            {
                string[] pathMatches = [.. indexedFiles.Where(f => PathEndsWithSegments(f, candidate))];
                return pathMatches.Length == 1 ? pathMatches[0] : null;
            }

            string[] leafMatches = [.. indexedFiles.Where(f =>
                string.Equals(Path.GetFileName(f), candidate, StringComparison.OrdinalIgnoreCase))];
            return leafMatches.Length == 1 ? leafMatches[0] : null;
        }

        /// <summary>
        /// True when <paramref name="fullPath"/>'s own directory segments end with
        /// <paramref name="candidate"/>'s, e.g. "...\TestConsoleApp\Program.cs" against
        /// "TestConsoleApp\Program.cs". A plain string.EndsWith would also true-positive on
        /// "...\OtherTestConsoleApp\Program.cs" - guarded against by requiring the character right
        /// before the match to be a path separator, or the match to consume the whole path.
        /// </summary>
        private static bool PathEndsWithSegments(string fullPath, string candidate)
        {
            string normalizedFullPath = fullPath.Replace('/', '\\');
            string normalizedCandidate = candidate.Replace('/', '\\');
            if (!normalizedFullPath.EndsWith(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                return false;

            int cutIndex = normalizedFullPath.Length - normalizedCandidate.Length;
            return cutIndex == 0 || normalizedFullPath[cutIndex - 1] == '\\';
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
