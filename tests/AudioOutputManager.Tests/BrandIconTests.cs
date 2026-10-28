using System.Runtime.InteropServices;
using AudioOutputManager.Windows;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class BrandIconTests
{
    [Theory]
    [InlineData(16,0x202B36u)]
    [InlineData(20,0xEFF6FFu)]
    [InlineData(24,0xFFFF00u)]
    [InlineData(32,0xFFFFFFu)]
    public void NativeTrayIconRetainsAlphaAndUsesRequestedForegroundAndSize(int size,uint rgb)
    {
        using var icon=BrandIcon.Load(Path.Combine(AppContext.BaseDirectory,"Assets","AuriHelm.Tray.ico"),size,rgb);
        Assert.False(icon.IsInvalid);Assert.True(GetIconInfo(icon.DangerousGetHandle(),out var info));
        nint dc=GetDC(0);
        try
        {
            Assert.NotEqual(0,GetObject(info.Color,Marshal.SizeOf<Bitmap>(),out var bitmap));
            Assert.Equal(size,bitmap.Width);Assert.Equal(size,bitmap.Height);
            var header=new BitmapInfo{Size=40,Width=size,Height=-size,Planes=1,BitCount=32};var data=new byte[size*size*4];
            Assert.Equal((uint)size,GetDIBits(dc,info.Color,0,(uint)size,data,ref header,0));
            Assert.Equal(0,data[3]); // corners are transparent, not the app tile
            var opaque=Enumerable.Range(0,size*size).Where(i=>data[i*4+3]==255).ToArray();Assert.NotEmpty(opaque);
            Assert.All(opaque,i=>{Assert.Equal((byte)(rgb&255),data[i*4]);Assert.Equal((byte)((rgb>>8)&255),data[i*4+1]);Assert.Equal((byte)(rgb>>16),data[i*4+2]);});
        }
        finally{ReleaseDC(0,dc);DeleteObject(info.Color);DeleteObject(info.Mask);}
        icon.Dispose();Assert.True(icon.IsClosed);
    }
    [StructLayout(LayoutKind.Sequential)]private struct IconInfo{[MarshalAs(UnmanagedType.Bool)]public bool IsIcon;public uint X,Y;public nint Mask,Color;}
    [StructLayout(LayoutKind.Sequential)]private struct BitmapInfo{public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,SizeImage;public int XPels,YPels;public uint Used,Important;public uint Color;}
    [StructLayout(LayoutKind.Sequential)]private struct Bitmap{public int Type,Width,Height,WidthBytes;public ushort Planes,BitsPixel;public nint Bits;}
    [DllImport("gdi32.dll",EntryPoint="GetObjectW")]private static extern int GetObject(nint obj,int size,out Bitmap bitmap);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetIconInfo(nint icon,out IconInfo info);
    [DllImport("user32.dll")]private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")]private static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("gdi32.dll")]private static extern uint GetDIBits(nint dc,nint bitmap,uint start,uint lines,[Out]byte[] bits,ref BitmapInfo info,uint usage);
    [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DeleteObject(nint bitmap);
}
