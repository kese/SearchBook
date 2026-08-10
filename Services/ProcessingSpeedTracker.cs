namespace SearchBook.Services;

public sealed class ProcessingSpeedTracker(int capacity = 36, int smoothingWindow = 5)
{
    private readonly Queue<double> _samples = new();
    private readonly int _capacity = Math.Max(2, capacity);
    private readonly int _smoothingWindow = Math.Max(1, smoothingWindow);
    private int _previousCompleted;
    private TimeSpan _previousElapsed;

    public IReadOnlyList<double> Samples => _samples.ToArray();
    public double CurrentItemsPerMinute { get; private set; }

    public double AddSample(int completed, TimeSpan elapsed)
    {
        var itemDelta = completed - _previousCompleted;
        var secondDelta = (elapsed - _previousElapsed).TotalSeconds;
        _previousCompleted = completed;
        _previousElapsed = elapsed;

        if (itemDelta <= 0 || secondDelta <= 0) return CurrentItemsPerMinute;

        var itemsPerMinute = itemDelta * 60d / secondDelta;
        if (!double.IsFinite(itemsPerMinute) || itemsPerMinute < 0) return CurrentItemsPerMinute;

        _samples.Enqueue(itemsPerMinute);
        while (_samples.Count > _capacity) _samples.Dequeue();

        CurrentItemsPerMinute = _samples.TakeLast(_smoothingWindow).Average();
        return CurrentItemsPerMinute;
    }

    public void Reset()
    {
        _samples.Clear();
        _previousCompleted = 0;
        _previousElapsed = TimeSpan.Zero;
        CurrentItemsPerMinute = 0;
    }
}
