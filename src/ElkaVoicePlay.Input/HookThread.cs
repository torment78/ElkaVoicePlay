using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ElkaVoicePlay.Input;

/// <summary>A sleeping native message pump. Never runs UI, process inspection or recognition.</summary>
internal sealed class HookThread : IDisposable
{
    private const uint WorkMessage = 0x8001;
    private readonly ConcurrentQueue<(Action Work, TaskCompletionSource Completion)> queue = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private readonly Action cleanup;
    private readonly object lifecycle = new();
    private uint threadId;
    private int disposed;
    internal int ManagedThreadId => thread.ManagedThreadId;
    internal HookThread(Action cleanup)
    {
        this.cleanup = cleanup;
        thread = new(Run) { IsBackground = true, Name = "VoicePlay input" };
        thread.Start();
        ready.Task.GetAwaiter().GetResult();
    }
    internal Task InvokeAsync(Action action)
    {
        lock (lifecycle)
        {
            if (Volatile.Read(ref disposed) != 0) return Task.FromException(new ObjectDisposedException(nameof(HookThread)));
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue((action, completion));
            if (!NativeMethods.PostThreadMessage(threadId, WorkMessage, 0, 0))
                completion.TrySetException(new Win32Exception(Marshal.GetLastWin32Error(), "Could not update input controls."));
            return completion.Task;
        }
    }
    private void Run()
    {
        Exception? failure = null;
        try
        {
            threadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessage(out _, 0, 0, 0, 0); // Create the queue before callers can post.
            ready.TrySetResult();
            while (true)
            {
                int result = NativeMethods.GetMessage(out var message, 0, 0, 0);
                if (result == 0) break;
                if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Input message pump stopped.");
                if (message.Message != WorkMessage) continue;
                while (queue.TryDequeue(out var item))
                {
                    try { item.Work(); item.Completion.TrySetResult(); }
                    catch (Exception ex) { item.Completion.TrySetException(ex); }
                }
            }
        }
        catch (Exception ex) { failure = ex; ready.TrySetException(ex); }
        finally
        {
            lock (lifecycle) Interlocked.Exchange(ref disposed, 1);
            cleanup();
            while (queue.TryDequeue(out var item))
                item.Completion.TrySetException(failure ?? new ObjectDisposedException(nameof(HookThread)));
        }
    }
    public void Dispose()
    {
        lock (lifecycle)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            NativeMethods.PostThreadMessage(threadId, 0x12, 0, 0); // WM_QUIT; cleanup executes on the owning thread.
        }
        if (!thread.Join(TimeSpan.FromSeconds(2))) throw new TimeoutException("The input thread did not stop.");
    }
}
