using System.Linq;
using TeronClaudeCodeVS.Tests.Infrastructure;
using TeronClaudeCodeVS.ViewModels;
using Xunit;

namespace TeronClaudeCodeVS.Tests.Phases
{
    /// <summary>
    /// Regression coverage for two real bugs found live 2026-09-08, both in
    /// <see cref="TranscriptReplay"/>'s resumed-session reader, against a real captured transcript
    /// (<c>transcript-replay-compact-and-local-command.jsonl</c> - a short session with two real
    /// `/compact` runs, captured verbatim, see fixtures/README.md).
    /// <para>
    /// 1. The CLI writes its `isCompactSummary` recap as a synthetic `type:"user"` line - already
    /// fixed and skipped.
    /// </para>
    /// <para>
    /// 2. Two more kinds of CLI bookkeeping were still slipping through as if a human had typed
    /// them: the raw `&lt;local-command-caveat&gt;`/`&lt;command-name&gt;`/`&lt;local-command-stdout&gt;`
    /// wrapper the CLI writes for a slash command run locally (e.g. the /compact invocation itself),
    /// and `isMeta:true` lines (the CLI's own auto-sent "Continue from where you left off." nudge
    /// sent when a session resumes with no new human input). Neither was ever typed by a human.
    /// </para>
    /// </summary>
    public sealed class TranscriptReplayTests
    {
        private static string FixturePath => Fixtures.Path_("transcript-replay-compact-and-local-command.jsonl");

        [Fact]
        public void Local_command_wrapper_and_meta_nudge_lines_never_become_chat_bubbles()
        {
            Assert.True(System.IO.File.Exists(FixturePath), "the captured transcript fixture is missing");

            var messages = TranscriptReplay.LoadFromPath(FixturePath);

            string[] allText = [.. messages
                .SelectMany(m => m.Blocks)
                .OfType<TextBlockViewModel>()
                .Select(b => b.Text)];

            Assert.DoesNotContain(allText, t => t.Contains("local-command-caveat"));
            Assert.DoesNotContain(allText, t => t.Contains("command-name"));
            Assert.DoesNotContain(allText, t => t.Contains("local-command-stdout"));
            Assert.DoesNotContain(allText, t => t.Contains("This session is being continued from a previous conversation"));
            Assert.DoesNotContain(allText, t => t == "Continue from where you left off.");

            // CONTROL: the fixture really does contain all of the above verbatim, so the asserts
            // above are a filter doing work and not an artefact of an already-clean fixture.
            string raw = System.IO.File.ReadAllText(FixturePath);
            Assert.Contains("local-command-caveat", raw);
            Assert.Contains("local-command-stdout", raw);
            Assert.Contains("This session is being continued from a previous conversation", raw);
            Assert.Contains("Continue from where you left off.", raw);

            // A real, human-typed prompt sent right after one of the skipped sequences must still
            // come through - this isn't a blanket "hide everything near a compact" filter.
            Assert.Contains(allText, t => t == "What?");
            Assert.Contains(allText, t => t.Contains("List the working directory's contents"));
        }
    }
}
