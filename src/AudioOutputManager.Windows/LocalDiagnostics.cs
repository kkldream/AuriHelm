namespace AudioOutputManager.Windows;

public sealed class LocalDiagnostics(string directory)
{
    private readonly object gate=new();
    private readonly Dictionary<string,DateTimeOffset> recent=[];
    public void Record(string code)
    {
        lock(gate)
        {
            var now=DateTimeOffset.UtcNow;
            if(recent.TryGetValue(code,out var last)&&now-last<TimeSpan.FromSeconds(30))return;
            recent[code]=now;
            Directory.CreateDirectory(directory);
            foreach(string file in Directory.EnumerateFiles(directory,"events*.log")) if(File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddDays(-7))File.Delete(file);
            string current=Path.Combine(directory,"events.log");
            if(File.Exists(current)&&new FileInfo(current).Length>=2*1024*1024)
            {
                string oldest=Path.Combine(directory,"events.4.log");if(File.Exists(oldest))File.Delete(oldest);
                for(int i=3;i>=1;i--) {string path=Path.Combine(directory,$"events.{i}.log");if(File.Exists(path))File.Move(path,Path.Combine(directory,$"events.{i+1}.log"),true);}
                File.Move(current,Path.Combine(directory,"events.1.log"),true);
            }
            // Callers supply fixed event codes only, never paths, names, IDs or raw exception text.
            File.AppendAllText(current,DateTimeOffset.UtcNow.ToString("O")+" "+code+Environment.NewLine);
        }
    }
    public string Preview()
    {
        lock(gate)
        {
            string file=Path.Combine(directory,"events.log");
            return File.Exists(file)?string.Join(Environment.NewLine,File.ReadLines(file).TakeLast(200)):"沒有本機事件紀錄。 / No local events.";
        }
    }
    public void Clear()
    {lock(gate) {if(Directory.Exists(directory))foreach(string file in Directory.EnumerateFiles(directory,"events*.log"))File.Delete(file);}}
}
