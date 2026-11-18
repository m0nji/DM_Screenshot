namespace DMShot.Platform;

/// <summary>Transfer an initialized native resource to the caller, or dispose it on
/// any initialization/render failure before ownership can be transferred.</summary>
public static class OwnedResult
{
    public static T Create<T>(T result, Action<T> initialize) where T : IDisposable
    {
        try { initialize(result); return result; }
        catch { result.Dispose(); throw; }
    }
}
