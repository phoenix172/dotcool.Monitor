namespace dotCool.Monitor;

class ActionDisposable : IAsyncDisposable
{
    private readonly Func<Task> _action;

    public ActionDisposable(Func<Task> action)
    {
        _action = action;
    }

    public async ValueTask DisposeAsync()
    {
        await _action();
    }
}