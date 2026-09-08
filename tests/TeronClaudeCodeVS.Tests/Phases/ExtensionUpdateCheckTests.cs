using System;
using TeronClaudeCodeVS.Core;
using TeronClaudeCodeVS.Tests.Infrastructure;
using Xunit;

namespace TeronClaudeCodeVS.Tests.Phases
{
    /// <summary>
    /// Regression coverage for a real bug found live 2026-09-09: both the automatic update check
    /// and the manual "Check for Updates" command always concluded "nothing newer" because
    /// <c>ExtensionUpdateCheck</c> compared the GitHub release tag against
    /// <c>typeof(ExtensionUpdateCheck).Assembly.GetName().Version</c> - which is permanently
    /// 1.0.0.0 since this csproj sets no &lt;Version&gt;, regardless of the real shipped release
    /// (0.7.2 at the time). Fixed to read the real installed version out of
    /// extension.vsixmanifest (VSSDK's renamed copy of source.extension.vsixmanifest, shipped
    /// next to the DLL) instead.
    /// </summary>
    public sealed class ExtensionUpdateCheckTests
    {
        [Fact]
        public void Parses_the_version_out_of_the_real_shipped_manifest()
        {
            string manifestPath = Fixtures.ProjectFile("source.extension.vsixmanifest");

            Version? version = ExtensionUpdateCheck.ParseVersionFromManifest(manifestPath);

            Assert.NotNull(version);
            Assert.True(version >= new Version(0, 7, 2), $"expected at least 0.7.2, got {version}");
        }
    }
}
