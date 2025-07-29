using System.Collections.Concurrent;
using System.Diagnostics;
using Linux.Bluetooth;
using Tmds.DBus;

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

public class LinuxBleClient : IBleClient, IDisposable
{
    private readonly ILogger<LinuxBleClient> _logger;
    private Adapter? _adapter;
    private Task _scanTask = Task.CompletedTask;

    public LinuxBleClient(ILogger<LinuxBleClient> logger)
    {
        _logger = logger;
    }

    private async Task<Adapter> GetAdapter()
    {
        if (_adapter != null)
            return _adapter;

        try
        {
            await _resetSemaphore.WaitAsync();

            var adapter = (await BlueZManager.GetAdaptersAsync()).FirstOrDefault();
            if (adapter == null)
                throw new InvalidOperationException("No Bluetooth adapter found");
            return adapter;
        }
        finally
        {
            _resetSemaphore.Release();
        }
    }

    public async Task<BluetoothScan> StartPassiveScanAsync()
    {
        CancellationTokenSource cts = new();
        await ResetAdapter(cts.Token);
        _scanTask = Task.Run(async () => await ScanAndHandleErrors(cts.Token), cts.Token);
        var result = new BluetoothScan(_scanTask, cts.CancelAsync);
        return result;
    }

    private async Task ScanAndHandleErrors(CancellationToken ct)
    {
        int retries = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ScanLoop(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "({Retries}) Error during scan on adapter {AdapterName}", retries,
                    _adapter?.Name ?? "<Adapter missing>");
                if (retries++ >= 3)
                {
                    _logger.LogError("Failed to scan after 3 retries");
                    throw;
                }

                await ResetAdapter(ct);
            }
        }
    }

    private readonly SemaphoreSlim _resetSemaphore = new(1, 1);

    private async Task ResetAdapter(CancellationToken token)
    {
        try
        {
            await _resetSemaphore.WaitAsync(token);
            _adapter?.Dispose();
            _adapter = null;
            await Process.Start("bash", "-c \"modprobe -r btusb\"").WaitForExitAsync(token);
            await Process.Start("bash", "-c \"modprobe btusb\"").WaitForExitAsync(token);
            await Task.Delay(1000, token);
            await Process.Start("bash", "-c \"bluetoothctl power off\"").WaitForExitAsync(token);
            await Process.Start("bash", "-c \"bluetoothctl power on\"").WaitForExitAsync(token);

            await Task.Delay(3000, token);

            _logger.LogInformation("Restarted Bluetooth scan on adapter");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset Bluetooth adapter");
            throw;
        }
        finally
        {
            _resetSemaphore.Release();
        }
    }

    private async Task ScanLoop(CancellationToken ct)
    {
        _adapter = await GetAdapter();
        _logger.LogInformation("Started passive scan on adapter {AdapterName}", _adapter.Name);
        var filter = new Dictionary<string, object>
        {
            ["Transport"] = "le",
            ["DuplicateData"] = true
        };
        await _adapter.SetDiscoveryFilterAsync(filter);
        while (ct.IsCancellationRequested == false)
        {
            try
            {
                _logger.LogInformation("Starting discovery");
                await _adapter.StartDiscoveryAsync();
                await Task.Delay(30000, ct);
            }
            finally
            {
                _logger.LogInformation("Stopping discovery");
                await _adapter.StopDiscoveryAsync();
                await Task.Delay(500, ct);
            }
        }
    }

    public async Task<IAsyncDisposable> SubscribeToAdvertisement(Func<BluetoothLeAdvertisement, Task> action,
        params string[] deviceIds)
    {
        _adapter = await GetAdapter();

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

    private void PublishServiceData(Func<BluetoothLeAdvertisement, Task> action,
        IDictionary<string, object> serviceData, string deviceAddress)
    {
        _logger.LogDebug("[{Now}] Trying to publish service data for device {DeviceAddress} : {ServiceData}",
            DateTime.Now, deviceAddress, serviceData);
        bool success = TryToByteDictionary(serviceData.Where(x => x.Key == "ServiceData").ToDictionary(),
            out var serviceDataDictionary);
        _logger.LogDebug("[{Now}] Trying to publish service data for device {DeviceAddress} ({Success}): {ServiceData}",
            DateTime.Now, deviceAddress, success, serviceDataDictionary);
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
            if (keyValuePair.Value is IDictionary<string, object> dataDictionary)
                foreach (var dataPair in dataDictionary)
                {
                    if (dataPair.Value is byte[] dataBytes)
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