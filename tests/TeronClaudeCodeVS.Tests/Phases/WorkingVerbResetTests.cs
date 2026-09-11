using System;
using System.IO;
using System.Reflection;
using TeronClaudeCodeVS.Protocol;
using TeronClaudeCodeVS.Tests.Infrastructure;
using TeronClaudeCodeVS.ViewModels;
using Xunit;

namespace TeronClaudeCodeVS.Tests.Phases
{
    /// <summary>
    /// Regression coverage for a real bug found live 2026-09-09: the whimsical "working" status
    /// line kept cycling forever after a session had gone fully idle (StatusText correctly read
    /// "Ready" the whole time).
    /// <para>
    /// Root cause: the working-verb line's cleanup (<c>ResetWorkingVerb()</c>) lived only inside
    /// <c>IsBusy</c>'s own true-&gt;false change-notification branch. <c>IsBusy</c> is set <c>true</c>
    /// in exactly one place (<c>SendMessageAsync</c>), but <c>SetWorkingStatus()</c> - which starts
    /// the verb line - is also called from <c>OnStatusChanged</c>'s "requesting" case, which can
    /// fire for a turn that never went through <c>SendMessageAsync</c> (e.g. a resumed session
    /// auto-continuing a turn left over from before this process started). When that turn later
    /// completes, <c>OnTurnCompleted</c>'s own <c>IsBusy = false</c> is a no-op (already false), so
    /// the change-notification branch - and the <c>ResetWorkingVerb()</c> living inside it - never
    /// fires, leaving the timer ticking with nothing left to ever stop it.
    /// </para>
    /// <para>
    /// Fixed by calling <c>ResetWorkingVerb()</c> directly and unconditionally at every point that
    /// represents "the session is now idle" (<c>OnTurnCompleted</c>, <c>OnSessionInitialized</c>,
    /// <c>OnProcessExited</c>) instead of relying solely on <c>IsBusy</c>'s change-detection.
    /// </para>
    /// </summary>
    public sealed class WorkingVerbResetTests
    {
        private static readonly MethodInfo OnStatusChangedMethod =
            typeof(ChatSessionViewModel).GetMethod("OnStatusChanged", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("ChatSessionViewModel.OnStatusChanged is gone.");

        private static readonly MethodInfo OnTurnCompletedMethod =
            typeof(ChatSessionViewModel).GetMethod("OnTurnCompleted", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("ChatSessionViewModel.OnTurnCompleted is gone.");

        private static readonly FieldInfo StorePathField =
            typeof(SessionHistoryStore).GetField("s_path", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException("SessionHistoryStore.s_path is gone; the sandbox redirect below depends on it.");

        [Fact]
        public void Verb_line_clears_when_a_turn_completes_without_IsBusy_ever_having_been_true()
        {
            Sta.Run(() =>
            {
                using var sandbox = new StoreSandbox();
                var vm = new ChatSessionViewModel();
                try
                {
                    Assert.False(vm.IsBusy);
                    Assert.Null(vm.WorkingVerbText);

                    // Simulates a "requesting" status arriving for a turn that never went through
                    // SendMessageAsync (the only place that sets IsBusy = true) - e.g. a resumed
                    // session auto-continuing. This alone starts the verb line while IsBusy stays
                    // false the whole time, exactly like the bug found live.
                    OnStatusChangedMethod.Invoke(vm, [new StatusMessage { Status = "requesting" }]);

                    Assert.False(vm.IsBusy, "IsBusy must stay false to reproduce the real bug shape");
                    // ResetWorkingVerb() sets WorkingVerbText to null; BeginTypingNewWorkingVerb()
                    // sets it to "" and lets the (unticked, in this synchronous test) timer fill it
                    // in - null vs. non-null is what distinguishes "reset" from "active" here, not
                    // the text's own length.
                    Assert.NotNull(vm.WorkingVerbText);

                    var result = new ResultMessage { SessionId = "verb-test-session", IsError = false, NumTurns = 1, QueuedTurnCount = 0 };
                    OnTurnCompletedMethod.Invoke(vm, [result]);

                    Assert.Null(vm.WorkingVerbText);
                }
                finally
                {
                    vm.Dispose();
                }
            });
        }

        /// <summary>Redirects <see cref="SessionHistoryStore"/> into a throwaway file so
        /// <c>OnTurnCompleted</c>'s real <c>SaveOrUpdateSession</c> call cannot touch the user's own
        /// history - same pattern as <see cref="ContextIndicatorTests"/>'s own sandbox.</summary>
        private sealed class StoreSandbox : IDisposable
        {
            private readonly string _realPath;
            private readonly string _directory;

            public StoreSandbox()
            {
                _realPath = (string)StorePathField.GetValue(null)!;
                _directory = Path.Combine(Path.GetTempPath(), "teron-working-verb-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_directory);
                StorePathField.SetValue(null, Path.Combine(_directory, "sessions.json"));
            }

            public void Dispose()
            {
                StorePathField.SetValue(null, _realPath);
                try { Directory.Delete(_directory, recursive: true); }
                catch (IOException) { /* a still-open handle is not a test failure */ }
            }
        }
    }
}
