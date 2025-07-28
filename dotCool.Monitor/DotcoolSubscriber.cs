namespace dotCool.Monitor;

public class DotcoolSubscriber : BackgroundService
{
    private readonly AdvertisementHandler _handler;
    private readonly SensorBinding[] _sensors;
    private readonly HttpClient _client;
    private readonly ILogger<DotcoolSubscriber> _logger;

    public DotcoolSubscriber(AdvertisementHandler handler, SensorBinding[] sensors, HttpClient client,
        ILogger<DotcoolSubscriber> logger)
    {
        _handler = handler;
        _sensors = sensors;
        _client = client;
        _logger = logger;
    }

    private async Task OnAdvertisementReceived(BluetoothLeAdvertisement advertisement)
    {
        _logger.LogDebug("Advertisement: {@Advertisement}", advertisement);
        var device = _sensors.FirstOrDefault(x =>
            x.BluetoothMacAddress.Equals(advertisement.DeviceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
            throw new ArgumentException($"No sensor found for device {advertisement.DeviceId}", nameof(advertisement));

        _logger.LogDebug("Value: {Value}", Convert.ToHexString(advertisement.Data));
        if (advertisement.Data.Length >= 9)
        {
            var hex = Convert.ToHexString(advertisement.Data);
            var degrees = Convert.ToInt32(hex.Substring(10, 4), 16) / 256m;
            try
            {
                await _client.SendAsync(new HttpRequestMessage(HttpMethod.Parse(device.HttpMethod), device.Webhook)
                {
                    Content = new StringContent($"{{\"{device.JsonFieldName}\": {degrees} }}",
                        System.Text.Encoding.UTF8, "application/json")
                });
                _logger.LogInformation("dotcool {DeviceId} Service Data: {ServiceId} = {Degrees} Hex {Hex}",
                    advertisement.DeviceId, advertisement.ServiceId, degrees, hex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send data for device {DeviceId} with service {ServiceId}",
                    advertisement.DeviceId, advertisement.ServiceId);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _handler.AwaitReady();
        _logger.LogInformation("Done waiting for bluetooth. Subscribing...");
        var disposable =
            await _handler.Subscribe(OnAdvertisementReceived, _sensors.Select(x => x.BluetoothMacAddress).ToArray());
        _logger.LogInformation("Subscribed to advertisements for devices: {Devices}", string.Join(", ", _sensors.Select(x => x.BluetoothMacAddress)));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        finally
        {
            await disposable.DisposeAsync();
        }
    }
}