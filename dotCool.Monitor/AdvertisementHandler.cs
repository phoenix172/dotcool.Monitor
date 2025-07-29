using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using InTheHand.Bluetooth;

namespace dotCool.Monitor;

public record BluetoothLeAdvertisement(string DeviceId, Guid ServiceId, byte[] Data);

public class AdvertisementHandler
{
    private readonly IBleClient _bluetooth;
    private readonly SensorBinding[] _sensors;
    private readonly ConcurrentDictionary<object, IAsyncDisposable> _subscriptions;

    public AdvertisementHandler(IBleClient bluetooth, SensorBinding[] sensors)
    {
        _subscriptions = new();
        _bluetooth = bluetooth;
        _sensors = sensors;
    }

    public async Task ScanAsync(CancellationToken stoppingToken)
    {
        BluetoothScan? scan = null;
        try
        {
            scan = await _bluetooth.StartPassiveScanAsync();
            stoppingToken.Register(async () => await scan.DisposeAsync());
            await scan.Await();
        }
        finally
        {
            if(scan is not null)
                await scan.DisposeAsync();
            foreach (var subscription in _subscriptions)
            {
                await subscription.Value.DisposeAsync();
            }
        }
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