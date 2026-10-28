using AudioOutputManager.Windows;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

if(args.Length==0 || args.Any(a=>a is not ("--read-only" or "--probe-headset")))
{
    Console.WriteLine("Usage: AudioOutputManager.Diagnostics --read-only [--probe-headset]\nLists render endpoints and session counts; never changes output or volume. Headset probing sends only the documented status query.");
    return 1;
}
using var audio=new CoreAudioService();
await audio.InitializeAsync();
var snapshot=await audio.ReadAsync();
string? Mask(string? id)=>id is null?null:Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..8];
object? headset=null;
if(args.Contains("--probe-headset"))
{
    var reading=await new ProX2Headset().QueryAsync(CancellationToken.None);
    headset=new { State=reading.State.ToString(),reading.Battery,reading.Error };
}
Console.WriteLine(JsonSerializer.Serialize(new { OS=Environment.OSVersion.VersionString,Outputs=snapshot.Outputs.Select(d=>new { Device=Mask(d.Id),d.Name,d.Available,d.Volume,d.Muted,Sessions=d.Sessions.Count }),Roles=snapshot.Defaults.ToDictionary(x=>x.Key.ToString(),x=>Mask(x.Value)),snapshot.RolesConsistent,snapshot.Error,Headset=headset },new JsonSerializerOptions { WriteIndented=true }));
return 0;
