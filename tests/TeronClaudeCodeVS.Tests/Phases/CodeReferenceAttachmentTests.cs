using TeronClaudeCodeVS.ViewModels;
using Xunit;

namespace TeronClaudeCodeVS.Tests.Phases
{
    /// <summary>
    /// Phase 25: Active File / Selection is now a real attachment chip (a
    /// <see cref="PendingCodeReferenceAttachment"/>, thumbnail-style like an image/file attachment)
    /// instead of raw "@path#Lstart-Lend" text inserted into the composer - see
    /// ClaudeCodeChatControl.InsertContextReference. SendMessageAsync itself isn't covered here
    /// (it starts a real CLI session, which this suite avoids everywhere else too) - just the
    /// staging data these tests can exercise without one.
    /// </summary>
    public sealed class CodeReferenceAttachmentTests
    {
        [Fact]
        public void A_whole_file_reference_has_no_line_suffix()
        {
            PendingCodeReferenceAttachment attachment = new("src/Foo.cs", @"D:\Repo\src\Foo.cs", null, null);

            Assert.Equal("Foo.cs", attachment.DisplayTitle);
            Assert.Equal("@src/Foo.cs", attachment.ReferenceText);
        }

        [Fact]
        public void A_single_line_selection_shows_one_line_number()
        {
            PendingCodeReferenceAttachment attachment = new("src/Foo.cs", @"D:\Repo\src\Foo.cs", 42, 42);

            Assert.Equal("Foo.cs:42", attachment.DisplayTitle);
            Assert.Equal("@src/Foo.cs#L42", attachment.ReferenceText);
        }

        [Fact]
        public void A_multi_line_selection_shows_a_line_range()
        {
            PendingCodeReferenceAttachment attachment = new("src/Foo.cs", @"D:\Repo\src\Foo.cs", 10, 20);

            Assert.Equal("Foo.cs:10-20", attachment.DisplayTitle);
            Assert.Equal("@src/Foo.cs#L10-L20", attachment.ReferenceText);
        }

        [Fact]
        public void Staging_and_removing_a_code_reference_toggles_HasPendingCodeReferences()
        {
            ChatSessionViewModel vm = new();
            Assert.False(vm.HasPendingCodeReferences);

            vm.AddPendingCodeReference("src/Foo.cs", @"D:\Repo\src\Foo.cs", null, null);
            Assert.True(vm.HasPendingCodeReferences);
            PendingCodeReferenceAttachment staged = Assert.Single(vm.PendingCodeReferences);
            Assert.Equal("Foo.cs", staged.FileName);

            vm.RemovePendingCodeReference(staged);
            Assert.False(vm.HasPendingCodeReferences);
        }

        [Fact]
        public void Multiple_code_references_can_be_staged_at_once()
        {
            ChatSessionViewModel vm = new();

            vm.AddPendingCodeReference("src/Foo.cs", @"D:\Repo\src\Foo.cs", null, null);
            vm.AddPendingCodeReference("src/Bar.cs", @"D:\Repo\src\Bar.cs", 5, 8);

            Assert.Equal(2, vm.PendingCodeReferences.Count);
        }
    }
}
