using System.Runtime.InteropServices;
using ElkaVoicePlay.Input;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class EditorInsertionTests
{
    private static readonly WindowTarget Target = new(100, 200, 300, "notepad", 1, 400);

    [Theory]
    [InlineData("Now we're talking about other stuff so that is so.")]
    [InlineData("Þú ert á Íslandi — æ ö ü. 😀")]
    public async Task WholeUnicodeMessageIsDeliveredOnceWithoutCharacterSimulation(string text)
    {
        var delivered = new List<(nint Editor, string Text)>();
        var insertion = new EditorTextInserter(_ => true, (hwnd, value) => delivered.Add((hwnd, value)));
        await insertion.InsertAsync(Target, text, CancellationToken.None);
        Assert.Equal((Target.EditorHandle, text), Assert.Single(delivered));
    }

    [Fact] public async Task ChangedEditorOrFocusStopsBeforeWriting()
    {
        int writes = 0;
        var insertion = new EditorTextInserter(_ => false, (_, _) => writes++);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await insertion.InsertAsync(Target, "hello", CancellationToken.None));
        Assert.Equal(0, writes);
    }

    [Fact] public async Task CancelledInsertionNeverWrites()
    {
        int writes = 0;
        var insertion = new EditorTextInserter(_ => true, (_, _) => writes++);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await insertion.InsertAsync(Target, "hello", new(true)));
        Assert.Equal(0, writes);
    }

    [Fact] public async Task TimeoutNeverRetriesPotentiallyDeliveredMessage()
    {
        int writes = 0;
        var insertion = new EditorTextInserter(_ => true, (_, _) => { writes++; throw new TimeoutException(); });
        await Assert.ThrowsAsync<TimeoutException>(async () => await insertion.InsertAsync(Target, "hello", CancellationToken.None));
        Assert.Equal(1, writes);
    }

    [Theory] [InlineData("")] [InlineData(" \t")] [InlineData("hello\0there")]
    public async Task EmptyAndNativeTruncatingInputNeverWrites(string text)
    {
        int writes = 0;
        var insertion = new EditorTextInserter(_ => true, (_, _) => writes++);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await insertion.InsertAsync(Target, text, CancellationToken.None));
        Assert.Equal(0, writes);
    }

    [Fact] public void GuiThreadInfoMatchesWindowsX64Abi()
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(72, Marshal.SizeOf<NativeMethods.GuiThreadInfo>());
        Assert.Equal(16, Marshal.OffsetOf<NativeMethods.GuiThreadInfo>("Focus").ToInt32());
    }

    [Theory] [InlineData("RichEditD2DPT", true)] [InlineData("Edit", true)]
    [InlineData("Notepad", false)] [InlineData("Windows.UI.Composition.DesktopWindowContentBridge", false)]
    public void OnlyKnownDocumentEditorClassesAreAllowed(string name, bool expected) => Assert.Equal(expected, NotepadTextTarget.IsDocumentEditorClass(name));
}
