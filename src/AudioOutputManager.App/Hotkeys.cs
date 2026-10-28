namespace AudioOutputManager.App;

internal sealed class Hotkeys(NativeShell shell) : IDisposable
{
    private readonly NativeShell native=shell;
    private Dictionary<string,(int Id,string Binding)> active=[];
    private int nextId=100;
    public event Action<string>? Invoked;
    public void Dispatch(int id)
    {
        foreach(var item in active)if(item.Value.Id==id)Invoked?.Invoke(item.Key);
    }
    public Plan Prepare(IReadOnlyDictionary<string,string> requested)
    {
        var next=new Dictionary<string,(int Id,string Binding)>();var added=new List<int>();
        try
        {
            foreach(string action in new[]{"menu","next","automatic"})
            {
                string binding=requested.GetValueOrDefault(action,"").Trim();if(binding.Length==0)continue;
                if(active.TryGetValue(action,out var previous)&&string.Equals(previous.Binding,binding,StringComparison.OrdinalIgnoreCase)){next[action]=previous;continue;}
                var (modifiers,key)=Parse(binding);int id=++nextId;
                if(!NativeShell.RegisterHotKey(native.Handle,id,modifiers|0x4000,key))throw new InvalidOperationException("快捷鍵衝突或無法註冊 / Hotkey unavailable: "+binding);
                added.Add(id);next[action]=(id,binding);
            }
            return new(this,next,added);
        }
        catch {foreach(int id in added)NativeShell.UnregisterHotKey(native.Handle,id);throw;}
    }
    private static (uint Modifiers,uint Key) Parse(string text)
    {
        var parts=text.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);if(parts.Length<2)throw new InvalidDataException("快捷鍵需使用 Ctrl／Alt／Win 加按鍵。");
        uint modifiers=0;
        foreach(string part in parts[..^1]) modifiers|=part.ToUpperInvariant() switch {"ALT"=>1u,"CTRL"=>2u,"SHIFT"=>4u,"WIN"=>8u,_=>throw new InvalidDataException("無效修飾鍵 / Invalid modifier")};
        if((modifiers&11)==0)throw new InvalidDataException("需包含 Ctrl、Alt 或 Win。");
        string last=parts[^1];uint key;
        if(last.Length==1&&char.IsAsciiLetterOrDigit(last[0]))key=char.ToUpperInvariant(last[0]);
        else if(Enum.TryParse<global::Windows.System.VirtualKey>(last,true,out var vk))key=(uint)vk;
        else throw new InvalidDataException("無效按鍵 / Invalid key");
        return(modifiers,key);
    }
    public sealed class Plan(Hotkeys owner,Dictionary<string,(int Id,string Binding)> next,List<int> added) : IDisposable
    {
        private bool committed;
        public void Commit()
        {
            foreach(var old in owner.active.Values)if(!next.Values.Any(n=>n.Id==old.Id))NativeShell.UnregisterHotKey(owner.native.Handle,old.Id);
            owner.active=next;committed=true;
        }
        public void Dispose(){if(!committed)foreach(int id in added)NativeShell.UnregisterHotKey(owner.native.Handle,id);}
    }
    public void Dispose(){foreach(var item in active.Values)NativeShell.UnregisterHotKey(native.Handle,item.Id);active.Clear();}
}
