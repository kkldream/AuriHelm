using System.Net;
using System.Text.Json;

namespace AudioOutputManager.Core;

public enum UpdateCheckState { Available,UpToDate,NoStableRelease,Offline,TimedOut,RateLimited,Unavailable,InvalidResponse,InvalidCurrentVersion,Cancelled }
public sealed record UpdateCheckResult(UpdateCheckState State,string? Version=null,Uri? ReleasePage=null,int? HttpStatus=null);

/// <summary>Only CheckAsync performs I/O. No credentials, diagnostics, installer downloads or timers.</summary>
public sealed class ReleaseUpdateClient : IDisposable
{
    public static Uri VersionsPage { get; }=new("https://github.com/kkldream/AuriHelm/releases");
    public static Uri Endpoint { get; }=new("https://api.github.com/repos/kkldream/AuriHelm/releases/latest");
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly TimeSpan timeout;
    private const int MaximumResponseBytes=1024*1024;
    public ReleaseUpdateClient(HttpClient? client=null,TimeSpan? timeout=null)
    {
        ownsClient=client is null;
        this.client=client??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false});
        this.timeout=timeout??TimeSpan.FromSeconds(10);
    }
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion,CancellationToken cancellation=default)
    {
        if(!ReleaseVersion.TryParse(currentVersion,out var current))return new(UpdateCheckState.InvalidCurrentVersion);
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,Endpoint);
            request.Headers.UserAgent.ParseAdd("AuriHelm");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version","2026-03-10");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,limit.Token).ConfigureAwait(false);
            if(response.StatusCode==HttpStatusCode.NotFound)return new(UpdateCheckState.NoStableRelease);
            bool limited=response.StatusCode==HttpStatusCode.TooManyRequests
                ||response.StatusCode==HttpStatusCode.Forbidden&&response.Headers.TryGetValues("X-RateLimit-Remaining",out var remaining)&&remaining.Any(v=>v.Trim()=="0");
            if(limited)return new(UpdateCheckState.RateLimited);
            if(response.StatusCode!=HttpStatusCode.OK)return new(UpdateCheckState.Unavailable,HttpStatus:(int)response.StatusCode);
            if(response.Content.Headers.ContentLength>MaximumResponseBytes)return new(UpdateCheckState.InvalidResponse);
            using var input=await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            using var body=new MemoryStream();var buffer=new byte[16384];
            int count;
            while((count=await input.ReadAsync(buffer.AsMemory(),limit.Token).ConfigureAwait(false))>0)
            {
                if(body.Length+count>MaximumResponseBytes)return new(UpdateCheckState.InvalidResponse);
                body.Write(buffer,0,count);
            }
            using var document=JsonDocument.Parse(body.GetBuffer().AsMemory(0,(int)body.Length),new JsonDocumentOptions{MaxDepth=32});
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object
                ||!root.TryGetProperty("draft",out var draft)||draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                ||!root.TryGetProperty("prerelease",out var prerelease)||prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                ||!root.TryGetProperty("tag_name",out var name)||name.ValueKind!=JsonValueKind.String)
                return new(UpdateCheckState.InvalidResponse);
            if(draft.GetBoolean()||prerelease.GetBoolean())return new(UpdateCheckState.NoStableRelease);
            string tag=name.GetString()!;
            string version=tag.StartsWith('v')?tag[1..]:tag;
            if(!ReleaseVersion.TryParse(version,out var latest))return new(UpdateCheckState.InvalidResponse);
            if(latest!.IsPrerelease)return new(UpdateCheckState.NoStableRelease);
            // Construct a fixed-repository link; never navigate a URL supplied in the response.
            var page=new Uri(VersionsPage.AbsoluteUri+"/tag/"+Uri.EscapeDataString(tag));
            return new(latest.CompareTo(current)>0?UpdateCheckState.Available:UpdateCheckState.UpToDate,version,page);
        }
        catch(OperationCanceledException) {return new(cancellation.IsCancellationRequested?UpdateCheckState.Cancelled:UpdateCheckState.TimedOut);}
        catch(HttpRequestException) {return new(UpdateCheckState.Offline);}
        catch(IOException) {return new(UpdateCheckState.Offline);}
        catch(JsonException) {return new(UpdateCheckState.InvalidResponse);}
    }
    public void Dispose(){if(ownsClient)client.Dispose();}
}
