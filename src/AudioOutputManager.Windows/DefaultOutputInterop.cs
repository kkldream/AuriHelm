using System.Runtime.InteropServices;
using AudioOutputManager.Core;

namespace AudioOutputManager.Windows;

// Undocumented compatibility ABI; isolated from supported Core Audio volume/session APIs.
// Portions copyright (c) 2026 Ayerdi. MIT; see THIRD-PARTY-NOTICES.md.
internal static class DefaultOutputInterop
{
    public static void Set(AudioRole role,string id)
    {
        object instance = new PolicyConfigClient();
        try { Marshal.ThrowExceptionForHR(((IPolicyConfig)instance).SetDefaultEndpoint(id,(int)role)); }
        finally { Marshal.ReleaseComObject(instance); }
    }
    [ComImport,Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }
    [ComImport,Guid("f8679f50-850a-41cf-9c72-430f290290c8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
        void GetPropertyValue(); void SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceName,int role);
        void SetEndpointVisibility();
    }
}
