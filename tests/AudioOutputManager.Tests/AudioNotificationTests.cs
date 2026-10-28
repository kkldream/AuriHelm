using AudioOutputManager.Windows;
using NAudio.CoreAudioApi;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class AudioNotificationTests
{
    [Fact]
    public void RecordedRoleSwitchDoesNotEnumerateUnrelatedDevicesOrSessions()
    {
        var calls=new List<string>();
        var events=new AudioNotifications(()=>calls.Add("topology"),id=>calls.Add("metadata:"+id),id=>calls.Add("read-roles:"+id),(role,id)=>calls.Add("notify:"+role+":"+id));
        // The real PRO X 2 / Realtek switch emitted these unrelated property keys.
        events.OnPropertyValueChanged("pro",new(new Guid("b7a0c3a5-e0f3-4995-bac8-0ad9aba9d0ed"),0));
        foreach(var role in Enum.GetValues<Role>())events.OnDefaultDeviceChanged(DataFlow.Render,role,"pro");
        events.OnPropertyValueChanged("other",new(new Guid("24dbb0fc-9311-4b3d-9cf0-18ff155639d4"),0));
        Assert.Equal(new[]{"read-roles:pro","notify:Console:pro","read-roles:pro","notify:Multimedia:pro","read-roles:pro","notify:Communications:pro"},calls);
    }
    [Fact]
    public void NameAndContainerChangesRefreshOnlyTheirDevice()
    {
        var calls=new List<string>();
        var events=new AudioNotifications(()=>calls.Add("topology"),id=>calls.Add(id),_=>calls.Add("roles"),(_,_)=>calls.Add("notify"));
        events.OnPropertyValueChanged("name",PropertyKeys.PKEY_Device_FriendlyName);
        events.OnPropertyValueChanged("interface",PropertyKeys.PKEY_DeviceInterface_FriendlyName);
        events.OnPropertyValueChanged("container",AudioNotifications.ContainerId);
        Assert.Equal(new[]{"name","interface","container"},calls);
    }
    [Fact]
    public void DeviceLifecycleStillRebuildsAndCaptureRolesAreIgnored()
    {
        int topology=0;
        var events=new AudioNotifications(()=>topology++,_=>Assert.Fail("metadata"),_=>Assert.Fail("roles"),(_,_)=>Assert.Fail("notify"));
        events.OnDeviceAdded("pro");events.OnDeviceRemoved("pro");events.OnDeviceStateChanged("pro",DeviceState.Unplugged);
        events.OnDefaultDeviceChanged(DataFlow.Capture,Role.Console,"microphone");
        Assert.Equal(3,topology);
    }
}
