using AudioOutputManager.Core;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace AudioOutputManager.Windows;

public sealed record AppIconImage(byte[] Png,string Source);

/// <summary>Local icon I/O runs outside the audio MTA and UI. Only encoded, owned bytes cross back.</summary>
public sealed class AppIconReader
{
    private readonly IconCache<AppIconImage> cache=new();
    private readonly SemaphoreSlim readers=new(2);
    public async Task<IconResult<AppIconImage>> ReadAsync(AppIconRequest request)
    {
        if(request.SystemSounds)return new(null,false);
        string version=await Task.Run(()=>Version(request)).ConfigureAwait(false);
        return await cache.GetAsync(version,request.Revision,()=>ReadSourcesAsync(request)).ConfigureAwait(false);
    }
    private static string Version(AppIconRequest request)
    {
        var versions=new List<string>{request.Key,request.SourceRevision.ToString(CultureInfo.InvariantCulture)};
        foreach(var source in request.Sources)
        {
            try
            {
                if(source.Kind==AppIconSourceKind.Package)
                {
                    versions.Add(AppInfo.GetFromAppUserModelId(source.Value).Package.Id.FullName);continue;
                }
                if(ParseLocalResource(source.Value) is not {} resource)continue;
                var file=new FileInfo(resource.Path);versions.Add(file.Exists?$"{file.LastWriteTimeUtc.Ticks}:{file.Length}":"missing");
            }
            catch(Exception ex) when(Expected(ex)){versions.Add("unavailable");}
        }
        return string.Join("|",versions);
    }
    private async Task<AppIconImage?> ReadSourcesAsync(AppIconRequest request)
    {
        await readers.WaitAsync().ConfigureAwait(false);
        try
        {
            // Starting the whole source pipeline on the pool also isolates synchronous Shell extraction.
            return await Task.Run(async()=>
            {
                foreach(var source in request.Sources)
                {
                    try
                    {
                        byte[]? png=null;
                        if(source.Kind==AppIconSourceKind.Package)
                        {
                            var app=AppInfo.GetFromAppUserModelId(source.Value);
                            using var stream=await app.DisplayInfo.GetLogo(new(request.Size,request.Size)).OpenReadAsync();
                            png=await EncodeAsync(stream,request.Size);
                        }
                        else if(ParseLocalResource(source.Value) is {} resource&&File.Exists(resource.Path))
                        {
                            if(source.Kind==AppIconSourceKind.Resource&&new[]{".png",".bmp",".jpg",".jpeg",".webp"}.Contains(Path.GetExtension(resource.Path),StringComparer.OrdinalIgnoreCase))
                            {
                                var file=await StorageFile.GetFileFromPathAsync(resource.Path);using var stream=await file.OpenReadAsync();
                                png=await EncodeAsync(stream,request.Size);
                            }
                            else if(source.Kind==AppIconSourceKind.Thumbnail)
                            {
                                var file=await StorageFile.GetFileFromPathAsync(resource.Path);
                                using var thumbnail=await file.GetThumbnailAsync(ThumbnailMode.SingleItem,(uint)request.Size);
                                if(thumbnail is not null)png=await EncodeAsync(thumbnail,request.Size);
                            }
                            else
                            {
                                var pixels=NativeAppIcon.Extract(resource.Path,resource.Index,request.Size,source.Kind==AppIconSourceKind.Executable);
                                if(pixels is not null)png=await EncodePixelsAsync(pixels,request.Size);
                            }
                        }
                        if(png is {Length:>0})return new AppIconImage(png,source.Kind.ToString());
                    }
                    catch(Exception ex) when(Expected(ex)){}
                }
                return null;
            }).ConfigureAwait(false);
        }
        finally{readers.Release();}
    }
    internal sealed record LocalResource(string Path,int Index);
    internal static LocalResource? ParseLocalResource(string value)
    {
        string text=Environment.ExpandEnvironmentVariables(value.Trim());
        if(text.StartsWith('@'))text=text[1..];
        int index=0;int comma=text.LastIndexOf(',');
        if(comma>=0&&int.TryParse(text.AsSpan(comma+1),NumberStyles.Integer,CultureInfo.InvariantCulture,out int parsed)){index=parsed;text=text[..comma];}
        text=text.Trim().Trim('"');
        // Reject UNC, device paths, URLs, drive-relative paths, alternate streams and mapped shares.
        if(text.Length<4||!char.IsAsciiLetter(text[0])||text[1]!=':'||text[2]!='\\'||text[3..].Contains(':'))return null;
        try
        {
            string path=Path.GetFullPath(text);
            var drive=new DriveInfo(path[..3]);
            if(drive.DriveType is not (DriveType.Fixed or DriveType.Removable))return null;
            if(!new[]{".exe",".dll",".ico",".icl",".png",".bmp",".jpg",".jpeg",".webp"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))return null;
            string parent=path[..3];
            foreach(string part in path[3..].Split('\\',StringSplitOptions.RemoveEmptyEntries))
            {
                parent=Path.Combine(parent,part);
                try{if((File.GetAttributes(parent)&System.IO.FileAttributes.ReparsePoint)!=0)return null;}
                catch(Exception ex) when(ex is FileNotFoundException or DirectoryNotFoundException){break;}
            }
            return new(path,index);
        }
        catch(Exception ex) when(Expected(ex)){return null;}
    }
    private static bool Expected(Exception ex)=>ex is IOException or UnauthorizedAccessException or COMException or ArgumentException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception;
    private static async Task<byte[]> EncodeAsync(IRandomAccessStream stream,int size)
    {
        stream.Seek(0);var decoder=await BitmapDecoder.CreateAsync(stream);
        if(decoder.PixelWidth is 0 or >4096||decoder.PixelHeight is 0 or >4096)throw new InvalidDataException("圖示尺寸無效。");
        var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,new BitmapTransform{ScaledWidth=(uint)size,ScaledHeight=(uint)size},ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
        return await EncodePixelsAsync(pixels.DetachPixelData(),size);
    }
    private static async Task<byte[]> EncodePixelsAsync(byte[] pixels,int size)
    {
        if(pixels.Length!=size*size*4||!Enumerable.Range(0,size*size).Any(i=>pixels[i*4+3]>0))throw new InvalidDataException("圖示沒有可見像素。");
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)size,(uint)size,96,96,pixels);await encoder.FlushAsync();
        using var input=stream.GetInputStreamAt(0);using var reader=new DataReader(input);await reader.LoadAsync((uint)stream.Size);
        var bytes=new byte[stream.Size];reader.ReadBytes(bytes);return bytes;
    }
}

internal static class NativeAppIcon
{
    internal static byte[]? Extract(string path,int index,int size,bool shellFallback)
    {
        nint icon=0,small=0;
        try
        {
            if(SHDefExtractIcon(path,index,0,out icon,out small,(uint)(size|(size<<16)))!=0||icon==0)
            {
                if(!shellFallback)return null;
                if(icon!=0){DestroyIcon(icon);icon=0;}
                if(SHGetFileInfo(path,0,out var info,(uint)Marshal.SizeOf<ShellFileInfo>(),0x100)==0)return null;
                icon=info.Icon;if(icon==0)return null;
            }
            byte[] pixels=Draw(icon,size,3);
            if(!Enumerable.Range(0,size*size).Any(i=>pixels[i*4+3]!=0))
            {
                // Legacy icons have an AND mask rather than alpha; preserve transparent corners.
                byte[] mask=Draw(icon,size,1);
                for(int i=0;i<size*size;i++)pixels[i*4+3]=mask[i*4]==255?(byte)0:(byte)255;
            }
            return pixels;
        }
        finally{if(icon!=0)DestroyIcon(icon);if(small!=0&&small!=icon)DestroyIcon(small);}
    }
    private static byte[] Draw(nint icon,int size,uint flags)
    {
        nint dc=CreateCompatibleDC(0),bitmap=0,previous=0;
        if(dc==0)throw new System.ComponentModel.Win32Exception();
        try
        {
            var info=new BitmapInfo{Size=40,Width=size,Height=-size,Planes=1,BitCount=32};
            bitmap=CreateDIBSection(dc,ref info,0,out var bits,0,0);if(bitmap==0)throw new System.ComponentModel.Win32Exception();
            var pixels=new byte[size*size*4];Marshal.Copy(pixels,0,bits,pixels.Length);previous=SelectObject(dc,bitmap);
            if(!DrawIconEx(dc,0,0,icon,size,size,0,0,flags))throw new System.ComponentModel.Win32Exception();
            if(!GdiFlush())throw new System.ComponentModel.Win32Exception();
            Marshal.Copy(bits,pixels,0,pixels.Length);return pixels;
        }
        finally{if(previous!=0)SelectObject(dc,previous);if(bitmap!=0)DeleteObject(bitmap);DeleteDC(dc);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct BitmapInfo{public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,ImageSize;public int XPels,YPels;public uint ColorsUsed,ColorsImportant;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct ShellFileInfo{public nint Icon;public int IconIndex;public uint Attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string DisplayName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string TypeName;}
    [DllImport("shell32.dll",EntryPoint="SHDefExtractIconW",CharSet=CharSet.Unicode)]private static extern int SHDefExtractIcon(string path,int index,uint flags,out nint large,out nint small,uint size);
    [DllImport("shell32.dll",EntryPoint="SHGetFileInfoW",CharSet=CharSet.Unicode)]private static extern nint SHGetFileInfo(string path,uint attributes,out ShellFileInfo info,uint size,uint flags);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")]private static extern bool DrawIconEx(nint dc,int x,int y,nint icon,int width,int height,uint step,nint brush,uint flags);
    [DllImport("gdi32.dll")]private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]private static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint bits,nint section,uint offset);
    [DllImport("gdi32.dll")]private static extern nint SelectObject(nint dc,nint value);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")]private static extern bool GdiFlush();
}
