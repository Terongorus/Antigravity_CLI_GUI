using System.Windows;
using TeronClaudeCodeVS.Controls;
using TeronClaudeCodeVS.ViewModels;
using Xunit;

namespace TeronClaudeCodeVS.Tests.Phases
{
    /// <summary>
    /// Real bug found live 2026-09-06: the CLI's own extended-thinking API can return a content
    /// block of type "thinking" with an empty string and only a cryptographic signature (confirmed
    /// directly in a real captured transcript: <c>"thinking":""</c>, non-empty <c>"signature"</c>) -
    /// a real round that simply didn't produce any visible reasoning, not a dropped delta. Every
    /// one of those still got its own "Thinking" Expander in the transcript that revealed nothing
    /// when opened - reported as "Thinking sections don't always actually contain any thinking".
    /// <see cref="ThinkingBlockViewModel.HasContent"/> and <see cref="ThinkingVisibleConverter"/>
    /// together hide the whole card once there is nothing to show, rather than leaving an empty
    /// Expander around to open.
    /// </summary>
    public sealed class ThinkingBlockTests
    {
        [Fact]
        public void A_thinking_block_with_no_text_has_no_content()
        {
            var block = new ThinkingBlockViewModel();
            Assert.False(block.HasContent);
        }

        [Fact]
        public void A_thinking_block_that_only_ever_received_whitespace_has_no_content()
        {
            // The real shape found live: an empty "thinking" string, not a missing one.
            var block = new ThinkingBlockViewModel();
            block.Append("");
            Assert.False(block.HasContent);

            block.Append("   \n  ");
            Assert.False(block.HasContent);
        }

        [Fact]
        public void A_thinking_block_gains_content_once_real_text_streams_in()
        {
            var block = new ThinkingBlockViewModel();
            Assert.False(block.HasContent);

            block.Append("Let me think about this...");
            Assert.True(block.HasContent);
        }

        [Theory]
        [InlineData(false, TranscriptViewMode.Normal, Visibility.Collapsed)]
        [InlineData(false, TranscriptViewMode.Verbose, Visibility.Collapsed)]
        [InlineData(false, TranscriptViewMode.Summary, Visibility.Collapsed)]
        [InlineData(true, TranscriptViewMode.Summary, Visibility.Collapsed)] // still hidden in Summary regardless of content
        [InlineData(true, TranscriptViewMode.Normal, Visibility.Visible)]
        [InlineData(true, TranscriptViewMode.Thinking, Visibility.Visible)]
        [InlineData(true, TranscriptViewMode.Verbose, Visibility.Visible)]
        public void ThinkingVisibleConverter_combines_content_and_transcript_mode(
            bool hasContent, TranscriptViewMode mode, Visibility expected)
        {
            var converter = new ThinkingVisibleConverter();
            object result = converter.Convert([hasContent, mode], typeof(Visibility), null!, null!);
            Assert.Equal(expected, result);
        }
    }
}
