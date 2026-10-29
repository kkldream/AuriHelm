using System.Net;
using System.Text;
using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class ReleaseUpdateTests
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {Interlocked.Increment(ref Calls);return respond(request,cancellation);}
    }
    private static HttpResponseMessage Json(string content)=>new(HttpStatusCode.OK){Content=new StringContent(content,Encoding.UTF8,"application/json")};
    private sealed class UnknownLengthContent(byte[] data) : HttpContent
    {
        protected override bool TryComputeLength(out long length){length=0;return false;}
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>stream.WriteAsync(data).AsTask();
    }

    [Fact] public void SemanticPrecedenceHandlesCandidateNumbersAndIgnoresBuildMetadata()
    {
        string[] ordered=["1.0.0-alpha","1.0.0-alpha.1","1.0.0-alpha.beta","1.0.0-beta","1.0.0-beta.2","1.0.0-beta.11","1.0.0-rc.1","1.0.0"];
        for(int i=1;i<ordered.Length;i++)
        {Assert.True(ReleaseVersion.TryParse(ordered[i-1],out var a));Assert.True(ReleaseVersion.TryParse(ordered[i],out var b));Assert.True(a!.CompareTo(b)<0);}
        Assert.True(ReleaseVersion.TryParse("0.1.0-rc.10+build.01",out var ten));
        Assert.True(ReleaseVersion.TryParse("0.1.0-rc.9",out var nine));Assert.True(ten!.CompareTo(nine)>0);
        Assert.True(ReleaseVersion.TryParse("0.1.0-rc.10+other",out var same));Assert.Equal(0,ten.CompareTo(same));
        Assert.True(ReleaseVersion.TryParse("999999999999999999999.0.0",out var huge));Assert.True(huge!.CompareTo(ten)>0);
        Assert.True(ReleaseVersion.TryParse("1.10.0",out var minorTen));
        Assert.True(ReleaseVersion.TryParse("1.9.99",out var minorNine));Assert.True(minorTen!.CompareTo(minorNine)>0);
    }
    [Theory]
    [InlineData("1.0")][InlineData("1.0.0.0")][InlineData("01.0.0")][InlineData("1.0.0-rc.01")]
    [InlineData("1.0.0-")][InlineData("1.0.0+")][InlineData(" 1.0.0")][InlineData("1.0.0+foo+bar")]
    public void InvalidVersionsAreNotComparedAsCurrent(string input)=>Assert.False(ReleaseVersion.TryParse(input,out _));

    [Fact] public async Task ConstructionDoesNotContactNetworkAndExplicitRequestContainsNoCredentialsOrUserData()
    {
        using var handler=new Handler((request,_)=>
        {
            Assert.Equal(HttpMethod.Get,request.Method);Assert.Equal(ReleaseUpdateClient.Endpoint,request.RequestUri);
            Assert.Null(request.Headers.Authorization);Assert.Null(request.Content);Assert.Empty(request.RequestUri!.Query);
            Assert.Equal("AuriHelm",request.Headers.UserAgent.ToString());
            Assert.Equal(new[]{"Accept","User-Agent","X-GitHub-Api-Version"},request.Headers.Select(h=>h.Key).Order(StringComparer.Ordinal).ToArray());
            return Task.FromResult(Json("{\"tag_name\":\"v0.1.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://example.invalid/elsewhere\"}"));
        });
        using var http=new HttpClient(handler);using var client=new ReleaseUpdateClient(http);
        Assert.Equal(0,handler.Calls);
        var result=await client.CheckAsync("0.1.0-rc.6");
        Assert.Equal(UpdateCheckState.Available,result.State);Assert.Equal(1,handler.Calls);
        Assert.Equal("https://github.com/kkldream/AuriHelm/releases/tag/v0.1.0",result.ReleasePage!.AbsoluteUri);
    }
    [Theory]
    [InlineData("0.1.0")][InlineData("0.2.0-rc.1")][InlineData("1.0.0")]
    public async Task ValidOlderOrEqualStableReleaseDoesNotOfferADowngrade(string current)
    {
        using var handler=new Handler((_,_)=>Task.FromResult(Json("{\"tag_name\":\"v0.1.0\",\"draft\":false,\"prerelease\":false}")));
        using var http=new HttpClient(handler);using var client=new ReleaseUpdateClient(http);
        Assert.Equal(UpdateCheckState.UpToDate,(await client.CheckAsync(current)).State);
    }
    [Theory]
    [InlineData("{\"tag_name\":\"v9.0.0\",\"draft\":true,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v9.0.0\",\"draft\":false,\"prerelease\":true}")]
    [InlineData("{\"tag_name\":\"v9.0.0-rc.1\",\"draft\":false,\"prerelease\":false}")]
    public async Task DraftsAndCandidatesNeverBecomeStableUpdateOffers(string response)
    {
        using var handler=new Handler((_,_)=>Task.FromResult(Json(response)));
        using var http=new HttpClient(handler);using var client=new ReleaseUpdateClient(http);
        Assert.Equal(UpdateCheckState.NoStableRelease,(await client.CheckAsync("0.1.0-rc.6")).State);
    }
    [Theory]
    [InlineData(404,UpdateCheckState.NoStableRelease)][InlineData(429,UpdateCheckState.RateLimited)]
    [InlineData(403,UpdateCheckState.Unavailable)][InlineData(503,UpdateCheckState.Unavailable)]
    public async Task HttpFailuresAreNotReportedAsAlreadyCurrent(int status,UpdateCheckState expected)
    {
        using var handler=new Handler((_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var http=new HttpClient(handler);using var client=new ReleaseUpdateClient(http);
        Assert.Equal(expected,(await client.CheckAsync("0.1.0-rc.6")).State);
    }
    [Fact] public async Task GitHubRateLimitAndInvalidPayloadHaveDistinctResults()
    {
        using var handler=new Handler((_,_)=>{var response=new HttpResponseMessage(HttpStatusCode.Forbidden);response.Headers.Add("X-RateLimit-Remaining","0");return Task.FromResult(response);});
        using var http=new HttpClient(handler);using var client=new ReleaseUpdateClient(http);
        Assert.Equal(UpdateCheckState.RateLimited,(await client.CheckAsync("0.1.0-rc.6")).State);
    }
    [Theory]
    [InlineData("not-json")][InlineData("[]")][InlineData("null")][InlineData("{}")]
    [InlineData("{\"tag_name\":\"v1.0\",\"draft\":false,\"prerelease\":false}")]
    public async Task IncompleteOrMalformedResultsCannotClaimLatest(string response)
    {
        using var handler=new Handler((_,_)=>Task.FromResult(Json(response)));
        using var http=new HttpClient(handler);using var client=new ReleaseUpdateClient(http);
        Assert.Equal(UpdateCheckState.InvalidResponse,(await client.CheckAsync("0.1.0-rc.6")).State);
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task ResponseSizeLimitAppliesWithOrWithoutContentLength(bool knownLength)
    {
        byte[] oversized=new byte[1024*1024+1];
        using var handler=new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {Content=knownLength?new ByteArrayContent(oversized):new UnknownLengthContent(oversized)}));
        using var http=new HttpClient(handler);using var client=new ReleaseUpdateClient(http);
        Assert.Equal(UpdateCheckState.InvalidResponse,(await client.CheckAsync("0.1.0-rc.6")).State);
    }
    [Fact] public async Task RedirectResponseDoesNotBecomeAnUpdateLink()
    {
        using var handler=new Handler((_,_)=>
        {
            var response=new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location=new Uri("https://example.invalid/elsewhere");return Task.FromResult(response);
        });
        using var http=new HttpClient(handler);using var client=new ReleaseUpdateClient(http);
        var result=await client.CheckAsync("0.1.0-rc.6");
        Assert.Equal(UpdateCheckState.Unavailable,result.State);Assert.Null(result.ReleasePage);Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task OfflineTimeoutAndUserCancellationAreSeparateAndBadLocalVersionDoesNotSend()
    {
        using var offline=new HttpClient(new Handler((_,_)=>throw new HttpRequestException("offline")));
        using var client=new ReleaseUpdateClient(offline);
        Assert.Equal(UpdateCheckState.Offline,(await client.CheckAsync("0.1.0-rc.6")).State);
        using var waiting=new HttpClient(new Handler(async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return Json("{}");}));
        using var timeout=new ReleaseUpdateClient(waiting,TimeSpan.FromMilliseconds(30));
        Assert.Equal(UpdateCheckState.TimedOut,(await timeout.CheckAsync("0.1.0-rc.6")).State);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Equal(UpdateCheckState.Cancelled,(await timeout.CheckAsync("0.1.0-rc.6",cancelled.Token)).State);
        using var handler=new Handler((_,_)=>throw new Exception("must not send"));
        using var noSend=new HttpClient(handler);using var invalid=new ReleaseUpdateClient(noSend);
        Assert.Equal(UpdateCheckState.InvalidCurrentVersion,(await invalid.CheckAsync("invalid")).State);Assert.Equal(0,handler.Calls);
    }
}
