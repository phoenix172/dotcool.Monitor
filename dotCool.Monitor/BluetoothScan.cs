namespace dotCool.Monitor;

public class BluetoothScan : IAsyncDisposable
{
    private readonly Func<Task> _cancellationAction;
    private readonly Task _scanTask;

    public BluetoothScan(Task scanTask, Func<Task> cancellationAction)
    {
        _cancellationAction = cancellationAction;
        _scanTask = scanTask;
    }

    public async Task Await()
    {
        await _scanTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellationAction();
    }
}