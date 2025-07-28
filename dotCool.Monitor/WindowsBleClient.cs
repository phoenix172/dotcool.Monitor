using InTheHand.Bluetooth;

namespace dotCool.Monitor;

public class WindowsBleClient : IBleClient
{
    public async Task<IAsyncDisposable> StartPassiveScanAsync()
    {
        if (!await Bluetooth.GetAvailabilityAsync())
            throw new Exception("Bluetooth is not available");

        var bleScanOptions = new BluetoothLEScanOptions()
        {
            AcceptAllAdvertisements = true
        };
        var scan = await Bluetooth.RequestLEScanAsync(bleScanOptions);
        return new ActionDisposable(() =>
        {
            scan.Stop();
            return Task.CompletedTask;
        });
    }

    public Task<IAsyncDisposable> SubscribeToAdvertisement(Func<BluetoothLeAdvertisement, Task> action, params string[] deviceIds)
    {
        var eventHandler = BluetoothOnAdvertisementReceived(action, deviceIds);
        Bluetooth.AdvertisementReceived += eventHandler;
        var result = new ActionDisposable(() =>
        {
            Bluetooth.AdvertisementReceived -= eventHandler;
            return Task.CompletedTask;
        });
        return Task.FromResult<IAsyncDisposable>(result);
    }

    private static EventHandler<BluetoothAdvertisingEvent> BluetoothOnAdvertisementReceived(
        Func<BluetoothLeAdvertisement, Task> action, string[] deviceIds) =>
        (_, e) =>
        {
            var deviceIdMappings = deviceIds.Select(x => (Mac: x, Id: x.Replace(":", "").ToUpperInvariant())).ToArray();
            (string Mac, string Id)? device = deviceIdMappings.SingleOrDefault(x => x.Id == e.Device.Id);
            if (device is null) return;
            
            var data = e.ServiceData.Select(x => new BluetoothLeAdvertisement(device.Value.Mac, x.Key.Value, x.Value));
            foreach (var d in data)
            {
                action(d);
            }
        };
}