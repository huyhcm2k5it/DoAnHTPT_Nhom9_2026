namespace De6.Cluster.Control;

public sealed class BootstrapState
{
    public string Role => "RECOVERING";
    // This immutable bootstrap has no activation path. Real promotion belongs to ClusterControl.
    public bool ServingEnabled => false;
    public long StableIndex => 0;
}
