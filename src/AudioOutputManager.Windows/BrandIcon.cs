using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AudioOutputManager.Windows;

/// <summary>Owned HICONs. Recolors the alpha mask to the taskbar/system foreground without a tile.</summary>
public sealed class BrandIcon : SafeHandleZeroOrMinusOneIsInvalid
{
    private BrandIcon(nint value):base(true)=>SetHandle(value);
    protected override bool ReleaseHandle()=>DestroyIcon(handle);

    public static BrandIcon Load(string path,int size,uint rgb)
    {
        using var source=new BrandIcon(LoadImage(0,path,1,size,size,0x10));
        if(source.IsInvalid)throw new Win32Exception();
        if(!GetIconInfo(source.handle,out var info))throw new Win32Exception();
        nint dc=GetDC(0),bitmap=0;
        try
        {
            var header=new BitmapInfo{Size=40,Width=size,Height=-size,Planes=1,BitCount=32,SizeImage=(uint)(size*size*4)};
            var pixels=new byte[size*size*4];
            if(GetDIBits(dc,info.Color,0,(uint)size,pixels,ref header,0)!=(uint)size)throw new Win32Exception();
            for(int i=0;i<pixels.Length;i+=4)
            {
                int alpha=pixels[i+3];pixels[i]=(byte)((rgb&255)*alpha/255);pixels[i+1]=(byte)(((rgb>>8)&255)*alpha/255);pixels[i+2]=(byte)(((rgb>>16)&255)*alpha/255);
            }
            bitmap=CreateDIBSection(dc,ref header,0,out nint bits,0,0);
            if(bitmap==0)throw new Win32Exception();
            Marshal.Copy(pixels,0,bits,pixels.Length);
            var recolored=info;recolored.Color=bitmap;
            var result=new BrandIcon(CreateIconIndirect(ref recolored));
            if(result.IsInvalid){result.Dispose();throw new Win32Exception();}
            return result;
        }
        finally
        {
            if(bitmap!=0)DeleteObject(bitmap);
            if(info.Color!=0)DeleteObject(info.Color);if(info.Mask!=0)DeleteObject(info.Mask);
            if(dc!=0)ReleaseDC(0,dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]private struct IconInfo{[MarshalAs(UnmanagedType.Bool)]public bool IsIcon;public uint X,Y;public nint Mask,Color;}
    [StructLayout(LayoutKind.Sequential)]private struct BitmapInfo{public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,SizeImage;public int XPels,YPels;public uint Used,Important;public uint Color;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint LoadImage(nint instance,string path,uint type,int cx,int cy,uint flags);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetIconInfo(nint icon,out IconInfo info);
    [DllImport("user32.dll",SetLastError=true)]private static extern nint CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")]private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")]private static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern uint GetDIBits(nint dc,nint bitmap,uint start,uint lines,[Out]byte[] bits,ref BitmapInfo info,uint usage);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint bits,nint section,uint offset);
    [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DeleteObject(nint obj);
}
