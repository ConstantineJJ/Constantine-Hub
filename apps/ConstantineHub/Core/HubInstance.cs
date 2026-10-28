namespace ConstantineHub.Core;

internal sealed class HubInstance : IDisposable
{
    private readonly Mutex _mutex = new(false, @"Local\ConstantineHub.Main", out _);
    private readonly EventWaitHandle _activate = new(false, EventResetMode.AutoReset, @"Local\ConstantineHub.Activate");
    private RegisteredWaitHandle? _wait;
    private readonly bool _owner;

    internal HubInstance()
    {
        try { _owner = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _owner = true; }
    }

    internal bool ActivateExisting()
    {
        if (_owner) return false;
        _activate.Set(); return true;
    }

    internal void Listen(MainForm form)
    {
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) =>
        {
            if (!form.IsDisposed && form.IsHandleCreated)
                try { form.BeginInvoke(new Action(form.RestoreFromTray)); } catch (InvalidOperationException) { }
        }, null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        _wait?.Unregister(null); _activate.Dispose();
        if (_owner) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
