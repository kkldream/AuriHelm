using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class MixerApplicationsTests
{
    private static AudioSession Session(string id, string identity, string name = "chrome", Guid? grouping = null) =>
        new(id, name, identity, grouping ?? Guid.NewGuid(), .7f, false, null);

    [Fact]
    public void SameApplicationWithDifferentWindowsGroupsHasOneMixerRow()
    {
        var first = Session("chrome-1", @"exe:C:\Apps\Chrome\chrome.exe");
        var second = Session("chrome-2", @"exe:C:\Apps\Chrome\chrome.exe");
        var game1 = Session("game-1", @"exe:C:\Games\Sandustry.exe", "Sandustry");
        var game2 = Session("game-2", @"exe:C:\Games\Sandustry.exe", "Sandustry");

        var rows = MixerApplications.Group([first, second, game1, game2]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["chrome-1", "chrome-2"], rows[0].Select(s => s.Id));
        Assert.Equal(["game-1", "game-2"], rows[1].Select(s => s.Id));
    }

    [Fact]
    public void NormalizedPathsAndPackagedAppIdentityDoNotProduceDuplicates()
    {
        var rows = MixerApplications.Group([
            Session("a", @"exe:C:\Apps\Chrome\chrome.exe"),
            Session("b", "exe:c:/apps/chrome/./chrome.exe"),
            Session("c", "aumid:Package!App"),
            Session("d", "aumid:package!app")]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(2, row.Length));
    }

    [Fact]
    public void SameDisplayNameFromDifferentSourcesAndUnknownSessionsStaySeparate()
    {
        var shared = Guid.NewGuid();
        var rows = MixerApplications.Group([
            Session("a", @"exe:C:\Apps\chrome.exe", grouping: shared),
            Session("b", @"exe:C:\Other\chrome.exe", grouping: shared),
            Session("c", "unresolved", grouping: shared),
            Session("d", "unresolved", grouping: shared)]);

        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.Single(row));
    }

    [Fact]
    public async Task OneApplicationControlChangesEveryMemberAndPreservesMuteWhenChangingVolume()
    {
        var sessions = new[] {
            Session("b", @"exe:C:\Apps\Chrome\chrome.exe") with { Volume = .8f, Muted = true },
            Session("a", @"exe:C:\Apps\Chrome\chrome.exe") with { Volume = .3f, Muted = false }
        };
        var members = Assert.Single(MixerApplications.Group(sessions));
        var ids = members.Select(s => s.Id).ToHashSet();
        var control = new VolumeControl(MixerApplications.Value(members),
            volume => { sessions = sessions.Select(s => ids.Contains(s.Id) ? s with { Volume = volume } : s).ToArray(); return Task.CompletedTask; },
            muted => { sessions = sessions.Select(s => ids.Contains(s.Id) ? s with { Muted = muted } : s).ToArray(); return Task.CompletedTask; },
            () => Task.FromResult(MixerApplications.Value(sessions.Where(s => ids.Contains(s.Id)))));

        Assert.Equal(new VolumeValue(.3f, false), control.Value);
        await control.SetVolumeAsync(.4f);
        Assert.All(sessions, s => Assert.Equal(.4f, s.Volume));
        Assert.True(sessions.Single(s => s.Id == "b").Muted);
        Assert.False(sessions.Single(s => s.Id == "a").Muted);
        await control.ToggleMuteAsync();
        Assert.All(sessions, s => Assert.True(s.Muted));
        await control.ToggleMuteAsync();
        Assert.All(sessions, s => Assert.False(s.Muted));
    }

    [Fact]
    public void NewSessionsKeepTheirValuesAndRepresentativeDoesNotFollowPeakOrEnumerationOrder()
    {
        var first = Session("a", @"exe:C:\Apps\Chrome\chrome.exe") with { Volume = .3f, Peak = 0 };
        var later = Session("b", first.AppIdentity) with { Volume = .9f, Peak = 1, Muted = true };

        var members = Assert.Single(MixerApplications.Group([later, first]));

        Assert.Equal(["a", "b"], members.Select(s => s.Id));
        Assert.Equal(new VolumeValue(.3f, false), MixerApplications.Value(members));
        Assert.Equal(.9f, later.Volume);
        Assert.True(later.Muted);
        Assert.Equal(MixerApplications.Value(members), MixerApplications.Value(members.Reverse()));
    }

    [Fact]
    public void ExclusionsHideEveryMemberAndOutputsRemainIndependent()
    {
        const string identity = @"exe:C:\Apps\Chrome\chrome.exe";
        var firstOutput = new[] { Session("a", identity), Session("b", identity), Session("c", AppExclusions.SystemSounds, "系統音效") };
        var secondOutput = new[] { Session("d", identity) };
        var exclusions = new Dictionary<string, string> { [AppExclusions.Key(identity)!] = "chrome" };

        var visible = MixerApplications.Group(AppExclusions.Visible(firstOutput, exclusions));
        Assert.Equal(AppExclusions.SystemSounds, Assert.Single(Assert.Single(visible)).AppIdentity);
        Assert.Empty(MixerApplications.Group(AppExclusions.Visible(secondOutput, exclusions)));
        Assert.Equal(["a", "b"], MixerApplications.Group(firstOutput)[0].Select(s => s.Id));
        Assert.Equal("d", Assert.Single(Assert.Single(MixerApplications.Group(secondOutput))).Id);
    }
}
