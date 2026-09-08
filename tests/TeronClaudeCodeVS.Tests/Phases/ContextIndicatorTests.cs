using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TeronClaudeCodeVS.Protocol;
using TeronClaudeCodeVS.Tests.Infrastructure;
using TeronClaudeCodeVS.ViewModels;
using Xunit;

namespace TeronClaudeCodeVS.Tests.Phases
{
    /// <summary>
    /// Regression coverage for a real bug found live 2026-09-07: with the "Show Threshold (%)"
    /// option set as low as 1%, the context-usage/compact button never appeared at all, for any
    /// session.
    /// <para>
    /// Root cause: <c>OnTurnCompleted</c> looked up <c>result.ModelUsage</c> (keyed by the CLI's
    /// full resolved model id, e.g. "claude-sonnet-...") using <c>SelectedModel.Value</c>, which is
    /// either null ("Default", the startup selection) or a short CLI alias ("sonnet"/"opus"/...).
    /// Neither ever matches a real key in that dictionary, so <c>_contextWindowSize</c> was never
    /// populated and <c>EffectiveContextWindow</c> stayed 0 forever - which gates
    /// <c>IsContextIndicatorVisible</c> to false regardless of threshold. Fixed by capturing the
    /// CLI's own resolved model id from <c>system:init</c> (<c>_resolvedModelId</c>) and keying the
    /// lookup off that instead. Only ever verified before this via a throwaway PowerShell reflection
    /// probe (see docs/Phase 23) that happened to use matching fake keys, so the mismatch shipped
    /// invisibly - this test drives the same two private handlers a real session drives, with
    /// deliberately mismatched-looking but real-shaped ids, so the shape of the bug cannot recur
    /// unnoticed.
    /// </para>
    /// </summary>
    public sealed class ContextIndicatorTests
    {
        private const string ModelId = "claude-sonnet-4-5-20250929";

        private static readonly FieldInfo StorePathField =
            typeof(SessionHistoryStore).GetField("s_path", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException("SessionHistoryStore.s_path is gone; the sandbox redirect below depends on it.");

        private static readonly MethodInfo OnSessionInitializedMethod =
            typeof(ChatSessionViewModel).GetMethod("OnSessionInitialized", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("ChatSessionViewModel.OnSessionInitialized is gone.");

        private static readonly MethodInfo OnTurnCompletedMethod =
            typeof(ChatSessionViewModel).GetMethod("OnTurnCompleted", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("ChatSessionViewModel.OnTurnCompleted is gone.");

        [Fact]
        public void The_compact_button_appears_at_a_1_percent_threshold_once_a_turn_completes()
        {
            Sta.Run(() =>
            {
                using var sandbox = new StoreSandbox();

                var vm = new ChatSessionViewModel { ContextIndicatorThresholdPercent = 1 };
                try
                {
                    Assert.False(vm.IsContextIndicatorVisible, "must stay hidden before any turn completes");

                    OnSessionInitializedMethod.Invoke(vm, [new InitMessage { SessionId = "ctx-test-session", Model = ModelId }]);

                    var result = new ResultMessage
                    {
                        SessionId = "ctx-test-session",
                        IsError = false,
                        NumTurns = 1,
                        ModelUsage = new Dictionary<string, ModelUsageInfo>
                        {
                            [ModelId] = new ModelUsageInfo { ContextWindow = 200_000, MaxOutputTokens = 8_192 }
                        }
                    };
                    OnTurnCompletedMethod.Invoke(vm, [result]);

                    // 1% of the ~178.8k effective window is ~1788 tokens - comfortably exceeded by
                    // ordinary system-prompt/tool overhead alone, which is exactly why real users hit
                    // this the very first turn.
                    FieldInfo currentTokensField =
                        typeof(ChatSessionViewModel).GetField("_currentContextTokens", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? throw new MissingFieldException("ChatSessionViewModel._currentContextTokens is gone.");
                    currentTokensField.SetValue(vm, 5_000);
                    typeof(ChatSessionViewModel).GetMethod("RaiseContextUsageChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(vm, null);

                    string? resolvedModelId = (string?)typeof(ChatSessionViewModel)
                        .GetField("_resolvedModelId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm);
                    int contextWindowSize = (int)typeof(ChatSessionViewModel)
                        .GetField("_contextWindowSize", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;
                    Assert.Equal(ModelId, resolvedModelId);
                    Assert.Equal(200_000, contextWindowSize);
                    Assert.True(vm.ContextPercentUsed >= 1, $"ContextPercentUsed was {vm.ContextPercentUsed}");

                    Assert.True(vm.IsContextIndicatorVisible,
                        "the button must appear once usage crosses a 1% threshold after a real turn - this is the bug that shipped");
                }
                finally
                {
                    vm.Dispose();
                }
            });
        }

        /// <summary>Redirects <see cref="SessionHistoryStore"/> into a throwaway file so
        /// <c>OnTurnCompleted</c>'s real <c>SaveOrUpdateSession</c> call cannot touch the user's own
        /// history - the exact contamination Phase 23's own verification hit and had to clean up by
        /// hand (see docs/Phase 23).</summary>
        private sealed class StoreSandbox : IDisposable
        {
            private readonly string _realPath;
            private readonly string _directory;

            public StoreSandbox()
            {
                _realPath = (string)StorePathField.GetValue(null)!;
                _directory = Path.Combine(Path.GetTempPath(), "teron-context-indicator-" + Guid.NewGuid().ToString("N"));
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
