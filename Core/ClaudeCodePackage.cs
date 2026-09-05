using TeronClaudeCodeVS.Commands;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TeronClaudeCodeVS.Core
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Claude Code for Visual Studio", "Chat with Claude Code without leaving the editor.", "1.0")]
    [ProvideToolWindow(typeof(ClaudeCodeToolWindow))]
    [ProvideOptionPage(typeof(ClaudeCodeOptionsPage), "Claude Code", "General", 0, 0, true)]
    // The second argument is the command-table VERSION, and Visual Studio caches the merged
    // command table against it: a changed Menus.vsct is ignored until this number goes up. Bumped
    // to 2 for the UX-6 global key binding, which silently did not register until this changed.
    // Bump it again whenever Menus.vsct changes.
    [ProvideMenuResource("Menus.ctmenu", 3)]
    [Guid(GuidList.guidClaudeCodePackageString)]
    public sealed class ClaudeCodePackage : AsyncPackage
    {
        /// <summary>Per-VS-instance singleton, used by the tool window to reach package services (e.g. the Options page).</summary>
        internal static ClaudeCodePackage? Instance { get; private set; }

        /// <summary>Full paths of every file under the current workspace, mirrored here by
        /// <see cref="ClaudeCodeChatControl.IndexProjectFilesAsync"/> so MarkdownRenderer - a static
        /// class with no control instance to reach - can auto-link a bare filename Claude's own
        /// prose mentions, the same index the composer's "@" picker already builds.</summary>
        internal string[] IndexedProjectFiles { get; set; } = [];

        private IdeCompanionServer? _ideServer;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Instance = this;

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await ClaudeCodeCommand.InitializeAsync(this);

            // Fire-and-forget: never block VS startup on a network call (RESUPPLY).
            _ = JoinableTaskFactory.RunAsync(() => ExtensionUpdateCheck.CheckAsync());
        }

        internal ClaudeCodeOptionsPage GetOptions() => (ClaudeCodeOptionsPage)GetDialogPage(typeof(ClaudeCodeOptionsPage));

        internal void ShowOptions() => ShowOptionPage(typeof(ClaudeCodeOptionsPage));

        private IClassificationTypeRegistryService? _classificationTypeRegistry;
        private IClassificationFormatMapService? _classificationFormatMapService;
        private IEditorFormatMapService? _editorFormatMapService;

        /// <summary>
        /// The real Visual Studio editor's own color for a token kind (keyword/string/comment/...),
        /// same source GitHub Copilot Chat's code blocks use - not a hand-picked palette, so it
        /// tracks the user's actual color theme AND any Fonts and Colors customization
        /// automatically. Category "text" is the shared, view-independent format map (mirrors the
        /// "Text Editor" category in Tools > Options > Fonts and Colors) - no live ITextView is
        /// needed for it. Returns null (caller falls back to the plain theme text color) if the
        /// classification services aren't available, e.g. under the xUnit tests' fake package-less
        /// environment.
        /// </summary>
        internal Brush? GetClassificationForeground(string classificationTypeName)
        {
            try
            {
                if (_classificationTypeRegistry == null || _classificationFormatMapService == null)
                {
                    if (GetService(typeof(SComponentModel)) is not IComponentModel componentModel)
                        return null;

                    _classificationTypeRegistry = componentModel.GetService<IClassificationTypeRegistryService>();
                    _classificationFormatMapService = componentModel.GetService<IClassificationFormatMapService>();
                }

                var classificationType = _classificationTypeRegistry.GetClassificationType(classificationTypeName);
                if (classificationType == null) return null;

                var formatMap = _classificationFormatMapService.GetClassificationFormatMap(category: "text");
                return formatMap.GetTextProperties(classificationType).ForegroundBrush;
            }
            catch
            {
                // A missing/renamed classification type, or no editor host at all, must never cost
                // the user the code block itself - the caller's plain-text fallback still reads fine.
                return null;
            }
        }

        /// <summary>
        /// The real Visual Studio code editor's own default background - what a fenced code
        /// block's header+body chrome is painted with, instead of a hand-picked or accent-tinted
        /// color, so it reads as an actual editor surface (dark stays dark, light stays light) on
        /// any theme, matching GitHub Copilot Chat's own code blocks.
        /// <para>
        /// Deliberately NOT <c>IClassificationFormatMap.DefaultTextProperties</c> (what the first
        /// version of this method used, and <see cref="GetClassificationForeground"/> still uses
        /// for per-token FOREGROUND colors) - found live 2026-09-06 rendering solid white
        /// regardless of the active VS theme. That API models per-TOKEN highlight overlays for
        /// syntax coloring; "Plain Text"'s own background there is commonly left at its literal
        /// default (white) in Fonts and Colors, since the editor's actual visible surface is
        /// painted separately by the view itself, not by that classification's background. The
        /// editor's real, currently-themed surface color is
        /// <see cref="IEditorFormatMapService"/>'s "Plain Text" -> "Background" entry instead -
        /// the same source Fonts and Colors' own "Plain Text" preview swatch reads from.
        /// </para>
        /// </summary>
        internal Brush? GetEditorBackground()
        {
            try
            {
                if (_editorFormatMapService == null)
                {
                    if (GetService(typeof(SComponentModel)) is not IComponentModel componentModel)
                        return null;

                    _editorFormatMapService = componentModel.GetService<IEditorFormatMapService>();
                }

                var formatMap = _editorFormatMapService.GetEditorFormatMap("text");
                var properties = formatMap.GetProperties("Plain Text");
                object? background = properties["Background"];
                return background switch
                {
                    Brush brush => brush,
                    Color color => new SolidColorBrush(color),
                    _ => null,
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Lazily starts (or stops, if the setting was just turned off) the shared IDE companion
        /// server - one per VS instance, shared across every chat session/tool window, matching
        /// how the CLI subprocess is meant to discover exactly one IDE per environment. Call from
        /// the UI thread (matches every other VS SDK call this server's handlers make).
        /// </summary>
        /// <summary>
        /// Set by <see cref="GetOrStartIdeServer"/> on every call - null on success, otherwise the
        /// reason no server is available (option disabled, or the exception <see cref="IdeCompanionServer.Start"/>
        /// threw). Exists because two live F5 passes (2026-08-26) both showed the CLI never
        /// connecting (empty `mcp_servers`) with no visible cause - this makes the actual outcome
        /// observable from the chat's Raw CLI Output panel instead of failing silently again.
        /// </summary>
        internal string? LastIdeServerDiagnostic { get; private set; }

        internal IdeCompanionServer? GetOrStartIdeServer()
        {
            if (!GetOptions().EnableIdeCompanionServer)
            {
                _ideServer?.Stop();
                LastIdeServerDiagnostic = "disabled via EnableIdeCompanionServer option";
                return null;
            }

            try
            {
                _ideServer ??= new IdeCompanionServer(new VsIdeToolHandlers(), GetWorkspaceFoldersSync);

                if (!_ideServer.IsRunning)
                    _ideServer.Start();
                else
                    _ideServer.RefreshWorkspaceFolders();

                LastIdeServerDiagnostic = $"running, port={_ideServer.Port}";
                return _ideServer;
            }
            catch (Exception ex)
            {
                LastIdeServerDiagnostic = $"GetOrStartIdeServer threw {ex.GetType().Name}: {ex.Message}";
                return null;
            }
        }

        // Called synchronously from IdeCompanionServer.WriteLockFile - blocking-join is
        // intentional here (matches the fire-and-forget-elsewhere-but-synchronous-here need of
        // writing the lockfile before Start() returns), same as other callers of this method
        // that are already on the UI thread when they call it.
        private static System.Collections.Generic.IReadOnlyList<string> GetWorkspaceFoldersSync()
        {
            string dir = ThreadHelper.JoinableTaskFactory.Run(VsIdeToolHandlers.GetWorkingDirectoryAsync);
            return [dir];
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _ideServer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
