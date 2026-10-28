using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;
public sealed class SwitchTests
{
    private sealed class Audio : IRoleController
    {
        public Dictionary<AudioRole,string?> Roles=Enum.GetValues<AudioRole>().ToDictionary(r=>r,_=>(string?)"before");
        public List<(AudioRole,string)> Writes=[];
        public Action<AudioRole,string>? Intercept;
        public Action? BeforeNativeExecution;
        public bool Available=true;
        public Task<IReadOnlyDictionary<AudioRole,string?>> ReadRolesAsync()=>Task.FromResult<IReadOnlyDictionary<AudioRole,string?>>(Roles.ToDictionary());
        public Task<bool> IsAvailableAsync(string id)=>Task.FromResult(Available);
        public Task SetRoleAsync(AudioRole role,string id,Func<bool> stillCurrent) { BeforeNativeExecution?.Invoke();if(!stillCurrent())throw new OperationCanceledException();Writes.Add((role,id)); Intercept?.Invoke(role,id); Roles[role]=id; return Task.CompletedTask; }
    }
    [Fact] public async Task AllRolesMustReadBackBeforeSuccess()
    {
        var audio=new Audio();var switches=new SwitchCoordinator(audio);
        var result=await switches.SwitchAsync("target");
        Assert.True(result.Success);Assert.Equal(3,audio.Writes.Count);Assert.All(result.Actual.Values,x=>Assert.Equal("target",x));
    }
    [Fact] public async Task PartialFailureRetriesOnceThenRestoresOnlyChangedRoles()
    {
        var audio=new Audio { Intercept=(r,id)=>{if(r==AudioRole.Communications&&id=="target") throw new IOException("failed");} };
        var result=await new SwitchCoordinator(audio).SwitchAsync("target");
        Assert.False(result.Success); Assert.True(result.Restored);
        Assert.Equal(2,audio.Writes.Count(w=>w==(AudioRole.Communications,"target")));
        Assert.Equal(2,audio.Writes.Count(w=>w.Item2=="before"));
        Assert.All(result.Actual.Values,x=>Assert.Equal("before",x));
    }
    [Fact] public async Task ExternalChoiceStopsQueuedOldWritesWithoutRestoringOverIt()
    {
        var audio=new Audio();var switches=new SwitchCoordinator(audio);
        audio.Intercept=(r,id)=>switches.Invalidate();
        var result=await switches.SwitchAsync("target");
        Assert.True(result.Interrupted);Assert.Single(audio.Writes);
    }
    [Fact] public async Task MissingDeviceIsNotAnOperationFailure()
    {
        var audio=new Audio {Available=false};var result=await new SwitchCoordinator(audio).SwitchAsync("target");
        Assert.True(result.TargetUnavailable);Assert.Empty(audio.Writes);
    }
    [Fact] public async Task SupersededQueuedNativeWorkNeverWrites()
    {
        var audio=new Audio();var switches=new SwitchCoordinator(audio);audio.BeforeNativeExecution=switches.Invalidate;
        var result=await switches.SwitchAsync("target");Assert.True(result.Interrupted);Assert.Empty(audio.Writes);
    }
    [Fact] public async Task ExternalEventBetweenDecisionAndEnqueueInvalidatesThatDecision()
    {
        var audio=new Audio();var switches=new SwitchCoordinator(audio);
        long decision=switches.Generation;
        switches.Invalidate();
        var result=await switches.SwitchAsync("target",decisionGeneration:decision);
        Assert.True(result.Interrupted);Assert.Empty(audio.Writes);
    }
    [Fact] public async Task CoupledRoleNotificationBeforeItsWriteIsOurOwnChange()
    {
        var audio=new Audio();var switches=new SwitchCoordinator(audio);
        audio.Intercept=(role,id)=>
        {
            // Windows can couple Console and Multimedia before the second native call.
            if(role==AudioRole.Console && !switches.AcceptNotification(AudioRole.Multimedia,id))switches.Invalidate();
        };
        var result=await switches.SwitchAsync("target");
        Assert.True(result.Success);Assert.Equal(3,audio.Writes.Count);
    }
    [Fact] public async Task CoupledRoleNotificationDuringRollbackDoesNotCancelRestoration()
    {
        var audio=new Audio();var switches=new SwitchCoordinator(audio);
        audio.Intercept=(role,id)=>
        {
            if(role==AudioRole.Communications&&id=="target")throw new IOException("failed");
            if(role==AudioRole.Console&&id=="before"&&!switches.AcceptNotification(AudioRole.Multimedia,id))switches.Invalidate();
        };
        var result=await switches.SwitchAsync("target");
        Assert.False(result.Success);Assert.True(result.Restored);Assert.False(result.Interrupted);
    }
}
