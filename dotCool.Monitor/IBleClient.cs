namespace dotCool.Monitor;

public interface IBleClient
{
    Task<IAsyncDisposable> StartPassiveScanAsync();

    Task<IAsyncDisposable> SubscribeToAdvertisement(Func<BluetoothLeAdvertisement, Task> action, params string[] deviceIds);
}