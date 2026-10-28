using AudioOutputManager.Windows;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class TrayMenuLifetimeTests
{
    [Fact]
    public async Task ClosedMenuCanReopenBeforeTheOldAwaitContinuationRuns()
    {
        var lifetime=new TrayMenuLifetime();var first=lifetime.TryBegin()!;
        lifetime.Complete(first,TrayMenuCommand.None);
        // Closed and the next shell callback can precede an awaited UI continuation.
        var second=lifetime.TryBegin();
        Assert.NotNull(second);
        Assert.Equal(TrayMenuCommand.None,await first.Result);
        Assert.True(lifetime.IsCurrent(second));
    }

    [Fact]
    public void LeftClickDoesNotInheritDeferredOwnerCleanupFromTheRightMenu()
    {
        var lifetime=new TrayMenuLifetime();var first=lifetime.TryBegin()!;
        var calls=new List<string>();
        lifetime.Complete(first,TrayMenuCommand.None);
        calls.Add("quick-activate");
        lifetime.Release(first,()=>calls.Add("old-owner-hide"));
        Assert.Equal(new[]{"quick-activate"},calls);
    }

    [Fact]
    public void StaleClosedOrFinallyCannotDismissTheNewMenu()
    {
        var lifetime=new TrayMenuLifetime();var first=lifetime.TryBegin()!;
        lifetime.Complete(first,TrayMenuCommand.None);
        var second=lifetime.TryBegin();Assert.NotNull(second);
        lifetime.Complete(first,TrayMenuCommand.Exit);
        lifetime.Release(first,()=>throw new InvalidOperationException("stale owner cleanup"));
        Assert.True(lifetime.IsCurrent(second));Assert.False(second.Result.IsCompleted);
    }

    [Fact]
    public async Task ClosingBeforeAnchorLoadsCancelsThePendingRequestAndAllowsAnother()
    {
        var lifetime=new TrayMenuLifetime();var first=lifetime.TryBegin()!;
        var loaded=new TaskCompletionSource();
        lifetime.Complete(first,TrayMenuCommand.None);
        Assert.Same(first.Result,await Task.WhenAny(loaded.Task,first.Result));
        Assert.NotNull(lifetime.TryBegin());
    }

    [Fact]
    public async Task CleanupFailureDoesNotStrandTheCommandOrLockTheNextRequest()
    {
        var lifetime=new TrayMenuLifetime();var request=lifetime.TryBegin()!;
        Assert.Throws<InvalidOperationException>(()=>lifetime.Complete(request,TrayMenuCommand.None,()=>throw new InvalidOperationException("cleanup")));
        Assert.Equal(TrayMenuCommand.None,await request.Result);
        Assert.NotNull(lifetime.TryBegin());
    }

    [Fact]
    public async Task ReentrantCleanupCannotReleaseOrCancelANewerRequest()
    {
        var lifetime=new TrayMenuLifetime();var first=lifetime.TryBegin()!;
        TrayMenuLifetime.Request? second=null;
        lifetime.Complete(first,TrayMenuCommand.OpenPanel,()=>second=lifetime.TryBegin());
        Assert.NotNull(second);
        lifetime.Release(first,()=>throw new InvalidOperationException("stale cleanup"));
        lifetime.Complete(first,TrayMenuCommand.None);
        Assert.True(lifetime.IsCurrent(second));Assert.False(second.Result.IsCompleted);
        Assert.Equal(TrayMenuCommand.OpenPanel,await first.Result);
    }
}
