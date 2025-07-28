using System.Collections.Concurrent;
using System.Diagnostics;
using Linux.Bluetooth;
using Tmds.DBus;

namespace dotCool.Monitor;

public class LinuxBleClient : IBleClient, IDisposable
{
    private readonly ILogger<LinuxBleClient> _logger;
    private Adapter? _adapter;
    private Task _scanTask = Task.CompletedTask;

    public LinuxBleClient(ILogger<LinuxBleClient> logger)
    {
        _logger = logger;
    }

    private static async Task<Adapter> GetAdapter()
    {
        var adapter = (await BlueZManager.GetAdaptersAsync()).FirstOrDefault();
        if (adapter == null)
            throw new InvalidOperationException("No Bluetooth adapter found");
        return adapter;
    }

    public async Task<IAsyncDisposable> StartPassiveScanAsync()
    {
        _adapter = await GetAdapter();
        try
        {
            await _adapter.StopDiscoveryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to stop discovery on adapter {AdapterName}", _adapter.Name);
        }

        var filter = new Dictionary<string, object>
        {
            ["Transport"] = "le",
            ["DuplicateData"] = true
        };
        await _adapter.SetDiscoveryFilterAsync(filter);
        CancellationTokenSource cts = new();
        _logger.LogInformation("Started passive scan on adapter {AdapterName}", _adapter.Name);
        _scanTask = Task.Run(async () =>
        {
            while (cts.IsCancellationRequested == false)
            {
                try
                {
                    _logger.LogInformation("Starting discovery");
                    await _adapter.StartDiscoveryAsync();
                    await Task.Delay(30000, CancellationToken.None);
                }
                finally
                {
                    await _adapter.StopDiscoveryAsync();
                    await Task.Delay(500, CancellationToken.None);
                }
            }
        }, cts.Token);
        return new ActionDisposable(async () => { await cts.CancelAsync(); });
    }

    public async Task<IAsyncDisposable> SubscribeToAdvertisement(Func<BluetoothLeAdvertisement, Task> action,
        params string[] deviceIds)
    {
        if (_adapter == null)
            throw new InvalidOperationException(
                "Bluetooth adapter is not initialized. Call StartPassiveScanAsync first.");

        var eventHandler = BluetoothOnAdvertisementReceived(action, deviceIds);
        _adapter.DeviceFound += eventHandler;
        _logger.LogInformation("Subscribed to advertisement on adapter for {@DeviceIds}", deviceIds);
        var result = new ActionDisposable(() =>
        {
            _adapter.DeviceFound -= eventHandler;
            return Task.CompletedTask;
        });
        return result;
    }

    private readonly ConcurrentDictionary<Device, IDisposable> _subscriptions = new();

    private DeviceChangeEventHandlerAsync BluetoothOnAdvertisementReceived(
        Func<BluetoothLeAdvertisement, Task> action, string[] deviceIds) =>
        async (_, args) =>
        {
            if (args.Device is not { } device)
                return;

            var deviceAddress = await device.GetAddressAsync();
            _logger.LogDebug("Received advertisement from device {DeviceAddress}", deviceAddress);

            if (deviceIds.Length > 0 && !deviceIds.Contains(deviceAddress))
                return;

            if (!_subscriptions.ContainsKey(args.Device))
            {
                var subscription = await device.WatchPropertiesAsync(props =>
                {
                    _logger.LogDebug("Props changed {DeviceAddress} {@Props}", deviceAddress, props.Changed);
                    PublishServiceData(action, props.Changed.ToDictionary(), deviceAddress);
                });
                if (_subscriptions.TryAdd(args.Device, subscription))
                    _logger.LogDebug("Subscribed to device {DeviceAddress} ({Total})", deviceAddress,
                        _subscriptions.Count);
            }

            var serviceData = await device.GetServiceDataAsync();
            
            PublishServiceData(action, serviceData, deviceAddress);
        };

    private void PublishServiceData(Func<BluetoothLeAdvertisement, Task> action, IDictionary<string, object> serviceData, string deviceAddress)
    {
        _logger.LogDebug("[{Now}] Trying to publish service data for device {DeviceAddress} : {ServiceData}", DateTime.Now, deviceAddress, serviceData);
        bool success = TryToByteDictionary(serviceData.Where(x => x.Key == "ServiceData").ToDictionary(), out var serviceDataDictionary);
        _logger.LogDebug("[{Now}] Trying to publish service data for device {DeviceAddress} ({Success}): {ServiceData}", DateTime.Now, deviceAddress, success, serviceDataDictionary);
        if (success && serviceDataDictionary.Count > 0)
        {
            _logger.LogDebug("[{Now}] Service data for device {DeviceAddress}: {@ServiceData}",
                DateTime.Now, deviceAddress, serviceDataDictionary);
            foreach (var data in serviceDataDictionary)
            {
                var advertisement =
                    new BluetoothLeAdvertisement(deviceAddress, Guid.Parse(data.Key), serviceDataDictionary[data.Key]);
                action(advertisement).ConfigureAwait(false);
                _logger.LogDebug("Processed advertisement for device {DeviceAddress}, service {ServiceId}",
                    deviceAddress, data.Key);
            }
        }
    }

    private static bool TryToByteDictionary(IDictionary<string, object> raw, out IDictionary<string, byte[]> dictionary)
    {
        dictionary = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyValuePair in raw)
        {
            if(keyValuePair.Value is IDictionary<string, object> dataDictionary)
                foreach (var dataPair in dataDictionary)
                {
                    if(dataPair.Value is byte[] dataBytes)
                        dictionary.Add(dataPair.Key, dataBytes);
                }
        }
        return true;
    }

    public void Dispose()
    {
        _adapter?.Dispose();
        foreach (var subscription in _subscriptions)
        {
            subscription.Value.Dispose();
        }
    }
}