using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using InTheHand.Bluetooth;

namespace dotCool.Monitor;

public record BluetoothLeAdvertisement(string DeviceId, Guid ServiceId, byte[] Data);

public class AdvertisementHandler : BackgroundService
{
    private readonly IBleClient _bluetooth;
    private readonly SensorBinding[] _sensors;
    private readonly ConcurrentDictionary<object, IAsyncDisposable> _subscriptions;
    private bool _ready;

    public AdvertisementHandler(IBleClient bluetooth, SensorBinding[] sensors)
    {
        _subscriptions = new();
        _bluetooth = bluetooth;
        _sensors = sensors;
        _ready = false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scan = await _bluetooth.StartPassiveScanAsync();
        _ready = true;
        try
        {
            while (stoppingToken.IsCancellationRequested == false)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        finally
        {
            await scan.DisposeAsync();
            foreach (var subscription in _subscriptions)
            {
                await subscription.Value.DisposeAsync();
            }
        }
    }

    public bool Ready => _ready;

    public async Task AwaitReady()
    {
        while (!_ready)
            await Task.Delay(100);
    }

    public async Task<IAsyncDisposable> Subscribe(Func<BluetoothLeAdvertisement, Task> action,
        params string[] deviceIds)
    {
        var disposable = await _bluetooth.SubscribeToAdvertisement(action, deviceIds);
        _subscriptions.TryAdd(disposable.GetHashCode(), disposable);
        return new ActionDisposable(async () =>
        {
            await disposable.DisposeAsync();
            _subscriptions.Remove(disposable.GetHashCode(), out _);
        });
    }
}