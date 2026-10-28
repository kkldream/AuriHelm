using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using AudioOutputManager.Core;
using AudioOutputManager.Windows.Hardware;

namespace AudioOutputManager.Windows;

public sealed class ProX2Headset : IHeadsetProvider
{
    private readonly SemaphoreSlim gate=new(1,1);
    private string? cachedPath;
    private Guid? cachedContainer;
    public async Task<HeadsetReading> QueryAsync(CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(1));
            var endpoint=CenturionNative.FindEndpoint();
            if(endpoint is null) return new(HeadsetState.Unknown,DateTimeOffset.UtcNow,Error:"找不到相符的 PRO X 2 HID 介面。");
            if(cachedPath!=endpoint.Path||cachedContainer is null)
            {
                var information=await global::Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(endpoint.Path,new[]{"System.Devices.ContainerId"},global::Windows.Devices.Enumeration.DeviceInformationKind.DeviceInterface).AsTask(deadline.Token);
                cachedContainer=information?.Properties.GetValueOrDefault("System.Devices.ContainerId") is Guid container?container:null;
                cachedPath=endpoint.Path;
            }
            using var handle=Open(endpoint.Path,0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
            if(handle.IsInvalid) return new(HeadsetState.Unknown,DateTimeOffset.UtcNow,Error:"無法開啟 HID："+Marshal.GetLastWin32Error());
            if(endpoint.InputLength<13 || endpoint.OutputLength<10 || !FlushQueue(handle)) return new(HeadsetState.Unknown,DateTimeOffset.UtcNow,Error:"HID report 格式或佇列狀態不符。");
            await using var stream=new FileStream(handle,FileAccess.ReadWrite,endpoint.InputLength,true);
            byte[] request=new byte[endpoint.OutputLength];
            byte[] header=[0x51,8,0,3,0x1A,0,3,0,4,0x0A]; header.CopyTo(request,0);
            await stream.WriteAsync(request,deadline.Token);
            bool off=false;
            byte[] buffer=new byte[endpoint.InputLength];
            for(int i=0;i<4;i++)
            {
                int length;
                try { length=await stream.ReadAsync(buffer,deadline.Token); }
                catch(OperationCanceledException) when(!cancellation.IsCancellationRequested && off) { break; }
                var state=CenturionPacket.Classify(buffer.AsSpan(0,length));
                if(state==HeadsetState.On) return new(state,DateTimeOffset.UtcNow,buffer[10],ContainerId:cachedContainer);
                off|=state==HeadsetState.Off;
            }
            return new(off?HeadsetState.Off:HeadsetState.Unknown,DateTimeOffset.UtcNow,Error:off?null:"沒有有效狀態回應。",ContainerId:cachedContainer);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or OperationCanceledException or COMException)
        { cachedPath=null;cachedContainer=null;return new(HeadsetState.Unknown,DateTimeOffset.UtcNow,Error:ex is OperationCanceledException?"HID 查詢取消或逾時。":"HID 查詢失敗："+ex.HResult.ToString("X8")); }
        finally { gate.Release(); }
    }
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle Open(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll",EntryPoint="HidD_FlushQueue",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.U1)] private static extern bool FlushQueue(SafeFileHandle handle);
}
