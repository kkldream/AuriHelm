using AudioOutputManager.Windows;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class AudioQueueTests
{
    [Fact]
    public async Task ControlAndRoleOrderPrecedeQueuedMeterOnSameMta()
    {
        using var worker=new MtaWorker();using var release=new ManualResetEventSlim();
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked=worker.Invoke(()=>{started.SetResult();release.Wait();});await started.Task;
        var order=new List<int>();var threads=new List<int>();
        var meter=worker.InvokeMeter(()=>{order.Add(4);threads.Add(Environment.CurrentManagedThreadId);return true;});
        var controls=Enumerable.Range(1,3).Select(i=>worker.Invoke(()=>{order.Add(i);threads.Add(Environment.CurrentManagedThreadId);Assert.Equal(ApartmentState.MTA,Thread.CurrentThread.GetApartmentState());})).ToArray();
        release.Set();await blocked;await Task.WhenAll(controls);await meter;
        Assert.Equal(new[]{1,2,3,4},order);Assert.Single(threads.Distinct());
    }
    [Fact]
    public async Task FailedDeviceOperationDoesNotBlockFollowingControl()
    {
        var worker=new MtaWorker();
        var failed=worker.Invoke(()=>throw new InvalidOperationException("removed"));
        var next=worker.Invoke(()=>42);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>failed);Assert.Equal(42,await next);
        worker.Dispose();await Assert.ThrowsAsync<ObjectDisposedException>(()=>worker.Invoke(()=>true));
    }
}
