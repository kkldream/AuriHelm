using AudioOutputManager.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioOutputManager.Windows;

internal sealed class AudioNotifications(Action topology,Action<string> metadata,Action<string?> defaults,Action<AudioRole,string?> changed) : IMMNotificationClient
{
    internal static readonly PropertyKey ContainerId=new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),2);
    public void OnDeviceStateChanged(string id,DeviceState state)=>topology();
    public void OnDeviceAdded(string id)=>topology();
    public void OnDeviceRemoved(string id)=>topology();
    public void OnPropertyValueChanged(string id,PropertyKey key)
    {
        if(key.Equals(PropertyKeys.PKEY_Device_FriendlyName)||key.Equals(PropertyKeys.PKEY_DeviceInterface_FriendlyName)||key.Equals(ContainerId))metadata(id);
    }
    public void OnDefaultDeviceChanged(DataFlow flow,Role role,string id)
    {
        if(flow==DataFlow.Render){defaults(id);changed((AudioRole)role,id);}
    }
}
