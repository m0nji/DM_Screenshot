namespace DMShot.Update;

/// <summary>Keep the dispatcher alive during history drain and observe installer errors.
/// A failed handoff must restore an application that had prepared to quit.</summary>
public static class RestartHandoff
{
    public static async Task<bool> ExecuteAsync(Func<Task<bool>> prepare, Action restart, Action<Exception> failed)
    {
        try
        {
            if (!await prepare()) return false;
            restart();
            return true;
        }
        catch (Exception ex) { failed(ex); return false; }
    }
}
