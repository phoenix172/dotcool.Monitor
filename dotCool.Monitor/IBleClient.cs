namespace dotCool.Monitor;

public interface IBleClient
{
    Task<BluetoothScan> StartPassiveScanAsync();

    Task<IAsyncDisposable> SubscribeToAdvertisement(Func<BluetoothLeAdvertisement, Task> action, params string[] deviceIds);
}