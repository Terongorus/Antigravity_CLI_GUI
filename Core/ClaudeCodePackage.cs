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
