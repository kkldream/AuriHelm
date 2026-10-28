using System.Text.Json;

namespace AudioOutputManager.Core;

public sealed record ViewRefreshPlan(bool Quick,bool Full,bool PreserveDraft);

/// <summary>Decides structural refreshes independently of WinUI controls and live volume updates.</summary>
public sealed class ViewRefreshTracker
{
    private string? signature;
    public ViewRefreshPlan Observe(AudioSnapshot snapshot,AppSettings settings,string? headsetId,string page,bool dirty)
    {
        string next=JsonSerializer.Serialize(new
        {
            Outputs=snapshot.Outputs.OrderBy(d=>d.Id,StringComparer.Ordinal).Select(d=>new
            {
                d.Id,d.Name,d.Available,d.ContainerId,d.IsProX2,
                Sessions=d.Sessions.OrderBy(s=>s.Id,StringComparer.Ordinal).Select(s=>new{s.Id,s.Grouping,s.Name,s.AppIdentity,s.IconFile,s.IconPath,s.IconRevision})
            }),
            Defaults=snapshot.Defaults.OrderBy(r=>r.Key),HeadsetId=headsetId,settings.Theme,settings.Language,
            Exclusions=settings.QuickMenuExclusions.OrderBy(p=>p.Key,StringComparer.Ordinal)
        });
        bool changed=next!=signature;signature=next;
        return new(changed,changed&&(page=="devices"||page=="auto"||page=="exclusions"),dirty);
    }
}
