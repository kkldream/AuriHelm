namespace AudioOutputManager.Core;

/// <summary>SemVer precedence; numeric identifiers are compared without an integer-size limit.</summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly string[] numbers,prerelease;
    private readonly string text;
    public bool IsPrerelease=>prerelease.Length!=0;
    private ReleaseVersion(string text,string[] numbers,string[] prerelease)
        =>(this.text,this.numbers,this.prerelease)=(text,numbers,prerelease);

    public static bool TryParse(string? value,out ReleaseVersion? version)
    {
        version=null;
        if(string.IsNullOrEmpty(value)||value.Length>256)return false;
        string core=value;
        int plus=core.IndexOf('+');
        if(plus>=0)
        {
            if(!Identifiers(core[(plus+1)..],false))return false;
            core=core[..plus];
        }
        string[] pre=[];
        int dash=core.IndexOf('-');
        if(dash>=0)
        {
            string suffix=core[(dash+1)..];
            if(!Identifiers(suffix,true))return false;
            pre=suffix.Split('.');core=core[..dash];
        }
        string[] parts=core.Split('.');
        if(parts.Length!=3||parts.Any(p=>!Numeric(p)||(p.Length>1&&p[0]=='0')))return false;
        version=new(value,parts,pre);return true;
    }
    private static bool Numeric(string value)=>value.Length>0&&value.All(c=>c is >= '0' and <= '9');
    private static bool Identifiers(string value,bool noLeadingZero)
        =>value.Split('.').All(p=>p.Length>0&&p.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '-')
            &&(!noLeadingZero||!Numeric(p)||p.Length==1||p[0]!='0'));
    private static int Number(string a,string b)=>a.Length!=b.Length?a.Length.CompareTo(b.Length):string.CompareOrdinal(a,b);
    public int CompareTo(ReleaseVersion? other)
    {
        if(other is null)return 1;
        for(int i=0;i<3;i++){int c=Number(numbers[i],other.numbers[i]);if(c!=0)return c;}
        if(!IsPrerelease||!other.IsPrerelease)return IsPrerelease==other.IsPrerelease?0:IsPrerelease?-1:1;
        for(int i=0;i<Math.Min(prerelease.Length,other.prerelease.Length);i++)
        {
            string a=prerelease[i],b=other.prerelease[i];bool an=Numeric(a),bn=Numeric(b);
            int c=an&&bn?Number(a,b):an!=bn?(an?-1:1):string.CompareOrdinal(a,b);
            if(c!=0)return c;
        }
        return prerelease.Length.CompareTo(other.prerelease.Length);
    }
    public override string ToString()=>text;
}
