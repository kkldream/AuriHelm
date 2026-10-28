namespace AudioOutputManager.Core;

/// <summary>Reversible 180 ms reveal. The caller supplies elapsed monotonic milliseconds.</summary>
public sealed class RevealTransition(bool open)
{
    private double from=open?1:0,to=open?1:0,started;
    public bool Open {get;private set;}=open;
    public bool Running(double now)=>from!=to&&now-started<180;
    public double Progress(double now)
    {
        double t=Math.Clamp((now-started)/180,0,1);
        // cubic-bezier(.2,0,0,1): solve x, then evaluate y.
        double lo=0,hi=1;
        for(int i=0;i<16;i++){double m=(lo+hi)/2,x=.6*m*(1-m)*(1-m)+m*m*m;if(x<t)lo=m;else hi=m;}
        double u=(lo+hi)/2,eased=t>=1?1:t<=0?0:3*(1-u)*u*u+u*u*u;
        return from+(to-from)*eased;
    }
    public void Set(bool open,double now,bool animate)
    {
        from=animate?Progress(now):open?1:0;to=open?1:0;started=now;Open=open;
    }
}
