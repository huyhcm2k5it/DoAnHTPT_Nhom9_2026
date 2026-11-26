namespace De6.Client.Simulation;

/// <summary>Client-side fault switches for the demo. Thread-safe.</summary>
public sealed class SimulatedFaults
{
    private int _networkDown;
    private int _dropNextResponse;
    private int _cutAfterNextApply;

    public bool NetworkDown { get => Volatile.Read(ref _networkDown) == 1; set => Volatile.Write(ref _networkDown, value ? 1 : 0); }
    /// <summary>The next mutation is applied by the cluster but its response is lost.</summary>
    public bool DropNextResponse { get => Volatile.Read(ref _dropNextResponse) == 1; set => Volatile.Write(ref _dropNextResponse, value ? 1 : 0); }
    /// <summary>The next mutation is applied, its response is lost and the network goes down (leaves a real UNKNOWN).</summary>
    public bool CutAfterNextApply { get => Volatile.Read(ref _cutAfterNextApply) == 1; set => Volatile.Write(ref _cutAfterNextApply, value ? 1 : 0); }

    internal bool TakeDrop() => Interlocked.Exchange(ref _dropNextResponse, 0) == 1;
    internal bool TakeCut() => Interlocked.Exchange(ref _cutAfterNextApply, 0) == 1;
}
