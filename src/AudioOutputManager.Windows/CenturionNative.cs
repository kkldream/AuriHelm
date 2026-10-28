// Portions copyright (c) 2026 Ayerdi. MIT; see THIRD-PARTY-NOTICES.md.
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AudioOutputManager.Windows.Hardware
{
    public sealed class CenturionStateResult
    {
        public string State = "Unknown";
        public int BatteryPercent = -1;
        public bool OfflineSignatureSeen = false;
        public string Error = "";
        public string Frames = "";
    }

    internal static partial class CenturionNative
    {
        private const ushort VendorId = 0x046D;
        private const ushort ProductId = 0x0AF7;
        private const ushort TargetUsagePage = 0xFFA0;
        private const byte ReportId = 0x51;

        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public uint Size;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HiddAttributes
        {
            public int Size;
            public ushort VendorId;
            public ushort ProductId;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidpCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;

            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        internal sealed class Endpoint
        {
            public string Path;
            public int InputLength;
            public int OutputLength;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetAttributes(IntPtr device, ref HiddAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps caps);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_SetOutputReport(IntPtr device, byte[] buffer, uint length);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr parentWindow,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr set,
            IntPtr deviceInfoData,
            ref Guid interfaceGuid,
            uint memberIndex,
            ref SpDeviceInterfaceData data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr set,
            ref SpDeviceInterfaceData data,
            IntPtr detail,
            uint detailSize,
            ref uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(
            IntPtr file,
            byte[] buffer,
            uint bytesToWrite,
            out uint bytesWritten,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(
            IntPtr file,
            byte[] buffer,
            uint bytesToRead,
            out uint bytesRead,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CancelIoEx(IntPtr file, IntPtr overlapped);

        internal static Endpoint FindEndpoint()
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr set = SetupDiGetClassDevs(
                ref hidGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                DigcfPresent | DigcfDeviceInterface);

            if (set == InvalidHandle)
                return null;

            try
            {
                for (uint index = 0; ; index++)
                {
                    SpDeviceInterfaceData data = new SpDeviceInterfaceData();
                    data.Size = (uint)Marshal.SizeOf(typeof(SpDeviceInterfaceData));

                    if (!SetupDiEnumDeviceInterfaces(
                        set,
                        IntPtr.Zero,
                        ref hidGuid,
                        index,
                        ref data))
                        break;

                    uint required = 0;
                    SetupDiGetDeviceInterfaceDetail(
                        set,
                        ref data,
                        IntPtr.Zero,
                        0,
                        ref required,
                        IntPtr.Zero);

                    if (required == 0)
                        continue;

                    IntPtr detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                        if (!SetupDiGetDeviceInterfaceDetail(
                            set,
                            ref data,
                            detail,
                            required,
                            ref required,
                            IntPtr.Zero))
                            continue;

                        string path = Marshal.PtrToStringUni(
                            new IntPtr(detail.ToInt64() + 4));

                        if (String.IsNullOrEmpty(path))
                            continue;

                        IntPtr handle = CreateFile(
                            path,
                            GenericRead | GenericWrite,
                            FileShareRead | FileShareWrite,
                            IntPtr.Zero,
                            OpenExisting,
                            0,
                            IntPtr.Zero);

                        if (handle == InvalidHandle)
                            continue;

                        try
                        {
                            HiddAttributes attributes = new HiddAttributes();
                            attributes.Size = Marshal.SizeOf(typeof(HiddAttributes));

                            if (!HidD_GetAttributes(handle, ref attributes) ||
                                attributes.VendorId != VendorId ||
                                attributes.ProductId != ProductId)
                                continue;

                            IntPtr preparsedData;
                            if (!HidD_GetPreparsedData(handle, out preparsedData))
                                continue;

                            try
                            {
                                HidpCaps caps;
                                int status = HidP_GetCaps(preparsedData, out caps);
                                if (status == 0x00110000 &&
                                    caps.UsagePage == TargetUsagePage &&
                                    caps.Usage == 0x0001)
                                {
                                    Endpoint endpoint = new Endpoint();
                                    endpoint.Path = path;
                                    endpoint.InputLength = caps.InputReportByteLength > 0
                                        ? caps.InputReportByteLength
                                        : 64;
                                    endpoint.OutputLength = caps.OutputReportByteLength > 0
                                        ? caps.OutputReportByteLength
                                        : 64;
                                    return endpoint;
                                }
                            }
                            finally
                            {
                                HidD_FreePreparsedData(preparsedData);
                            }
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }

            return null;
        }


    }
}
