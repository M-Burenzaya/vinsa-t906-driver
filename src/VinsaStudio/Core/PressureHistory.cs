namespace VinsaStudio.Core;

/// <summary>Pen-event history and a separate, fixed-rate ten-second timeline.</summary>
public sealed class PressureHistory
{
    public const int Capacity=200;
    public const double IntervalSeconds=0.05;
    private readonly Queue<double> events=new();
    private readonly Queue<double> timeline=new(Enumerable.Repeat(0.0,Capacity));
    private long lastTick;
    private double current;
    public IReadOnlyList<double> Events => events.ToArray();
    public IReadOnlyList<double> Timeline => timeline.ToArray();

    public void Record(double pressure,double seconds)
    {
        if(!double.IsFinite(pressure)) throw new ArgumentOutOfRangeException(nameof(pressure));
        Advance(seconds);
        current=Math.Clamp(pressure,0,1);
        events.Enqueue(current);
        while(events.Count>Capacity) events.Dequeue();
    }

    public void Advance(double seconds)
    {
        if(!double.IsFinite(seconds) || seconds<0) throw new ArgumentOutOfRangeException(nameof(seconds));
        long tick=(long)Math.Floor(seconds/IntervalSeconds+1e-9);
        if(tick<=lastTick) return;
        long steps=Math.Min(tick-lastTick,Capacity);
        for(long i=0;i<steps;i++) {timeline.Enqueue(current);timeline.Dequeue();}
        lastTick=tick;
    }

    public void Clear(double seconds)
    {
        events.Clear();timeline.Clear();
        for(int i=0;i<Capacity;i++) timeline.Enqueue(0);
        current=0;lastTick=(long)Math.Floor(seconds/IntervalSeconds+1e-9);
    }
}
