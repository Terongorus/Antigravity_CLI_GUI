using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TeronClaudeCodeVS.Core
{
    /// <summary>
    /// RESUPPLY: self-update via this extension's own GitHub Releases, never the VS Marketplace.
    /// Mirrors the pattern already established in Teron_DotNet_Studio's extensionUpdateCheck.ts -
    /// throttled check, silent failure, user-driven download+install rather than an automatic one.
    /// </summary>
    internal static class ExtensionUpdateCheck
    {
        private const string GitHubOwner = "Terongorus";
        private const string GitHubRepo = "Teron_ClaudeCode_VS";
        private static readonly TimeSpan ThrottleWindow = TimeSpan.FromHours(24);

        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("teron-claudecode-vs-extension");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public static async Task CheckAsync(bool force = false)
        {
            try
            {
                var options = ClaudeCodePackage.Instance?.GetOptions();
                if (options is null) { return; }

                if (!force && !string.IsNullOrEmpty(options.LastUpdateCheckUtc)
                    && DateTimeOffset.TryParse(options.LastUpdateCheckUtc, out var lastCheck)
                    && DateTimeOffset.UtcNow - lastCheck < ThrottleWindow)
                {
                    return;
                }

                options.LastUpdateCheckUtc = DateTimeOffset.UtcNow.ToString("o");
                options.SaveSettingsToStorage();

                var json = await Http.GetStringAsync(
                    $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest");
                JObject release = JObject.Parse(json);

                var tag = (string?)release["tag_name"];
                if (tag is null) { await ReportFailureIfForcedAsync(force); return; }
                var latestVersion = tag.TrimStart('v');

                var currentVersion = GetInstalledVersion();
                if (currentVersion is null || !Version.TryParse(latestVersion, out var latest))
                {
                    await ReportFailureIfForcedAsync(force);
                    return;
                }

                if (latest <= currentVersion)
                {
                    // The passive background check (force: false) never interrupts the user, per
                    // RESUPPLY - only the manual "Check for Updates" command confirms this state.
                    if (force)
                    {
                        await JoinableTaskFactory.SwitchToMainThreadAsync();
                        await ShowUpToDateInfoBarAsync(currentVersion);
                    }
                    return;
                }

                var asset = release["assets"]?
                    .FirstOrDefault(a => ((string?)a["name"])?.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase) == true);
                var downloadUrl = (string?)asset?["browser_download_url"];
                if (downloadUrl is null) { await ReportFailureIfForcedAsync(force); return; }

                await JoinableTaskFactory.SwitchToMainThreadAsync();
                await ShowUpdateInfoBarAsync(latestVersion, downloadUrl);
            }
            catch
            {
                // Passive check: silent, per RESUPPLY - a broken check should never interrupt
                // normal use. A manual click still deserves to know it didn't work.
                await ReportFailureIfForcedAsync(force);
            }
        }

        private static async Task ReportFailureIfForcedAsync(bool force)
        {
            if (!force) { return; }
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            await ShowInfoBarAsync("Couldn't check for updates. Check your internet connection and try again.");
        }

        /// <summary>
        /// The assembly's own AssemblyVersion is useless here - this project sets no
        /// &lt;Version&gt; in the csproj, so it's permanently the SDK default 1.0.0.0 regardless
        /// of the real release. VSSDK's build renames source.extension.vsixmanifest to
        /// extension.vsixmanifest and ships it next to the DLL, so read the real installed
        /// version from there instead.
        /// </summary>
        private static Version? GetInstalledVersion()
        {
            var assemblyDir = Path.GetDirectoryName(typeof(ExtensionUpdateCheck).Assembly.Location);
            if (assemblyDir is null) { return null; }

            var manifestPath = Path.Combine(assemblyDir, "extension.vsixmanifest");
            return File.Exists(manifestPath) ? ParseVersionFromManifest(manifestPath) : null;
        }

        internal static Version? ParseVersionFromManifest(string manifestPath)
        {
            var doc = XDocument.Load(manifestPath);
            XNamespace ns = "http://schemas.microsoft.com/developer/vsx-schema/2011";
            var version = doc.Descendants(ns + "Identity").FirstOrDefault()?.Attribute("Version")?.Value;
            return version is not null && Version.TryParse(version, out var parsed) ? parsed : null;
        }

        private static JoinableTaskFactory JoinableTaskFactory => ThreadHelper.JoinableTaskFactory;

        private static async Task ShowUpdateInfoBarAsync(string latestVersion, string downloadUrl)
        {
            InfoBarModel model = new(
                [
                    new InfoBarTextSpan($"A new version of Claude Code for Visual Studio is available (v{latestVersion})."),
                    new InfoBarHyperlink("Download and Install"),
                ],
                KnownMonikers.StatusInformation,
                isCloseButtonVisible: true);

            var infoBar = await VS.InfoBar.CreateAsync(model);
            if (infoBar is null) { return; }

            infoBar.ActionItemClicked += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                infoBar.Close();
                if (e.ActionItem.Text == "Download and Install")
                {
                    _ = JoinableTaskFactory.RunAsync(DownloadAndInstallAsync).Task;
                }

                async Task DownloadAndInstallAsync()
                {
                    try
                    {
                        var tempFile = Path.Combine(Path.GetTempPath(), $"TeronClaudeCodeVS-{Guid.NewGuid():N}.vsix");
                        var bytes = await Http.GetByteArrayAsync(downloadUrl);
                        File.WriteAllBytes(tempFile, bytes);

                        // VSIXInstaller.exe is the file association for .vsix - this is exactly
                        // what the README already tells users to do by hand (double-click the
                        // built .vsix).
                        Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
                    }
                    catch
                    {
                        // Silent, per RESUPPLY - the user can still update manually from the
                        // GitHub Releases page if the automatic download/install fails.
                    }
                }
            };

            await infoBar.TryShowInfoBarUIAsync();
        }

        private static async Task ShowUpToDateInfoBarAsync(Version currentVersion)
            => await ShowInfoBarAsync($"Claude Code for Visual Studio is up to date (v{currentVersion}).");

        private static async Task ShowInfoBarAsync(string message)
        {
            InfoBarModel model = new(
                [new InfoBarTextSpan(message)],
                KnownMonikers.StatusInformation,
                isCloseButtonVisible: true);

            var infoBar = await VS.InfoBar.CreateAsync(model);
            if (infoBar is null) { return; }

            await infoBar.TryShowInfoBarUIAsync();
        }
    }
}
