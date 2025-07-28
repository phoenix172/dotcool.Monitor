**Features**
- Receives Bluetooth LE advertisements from [dotcool.mini](https://shopus.dotcool.co/products/dotcool-mini) temperature sensors
- Can work with multiple sensors at the same time
- Sends data to a webhook in simple json format
- Works for different kinds of integrations (for example Home Assistant)

**Compiling**
- Install .Net 9 SDK: https://dotnet.microsoft.com/en-us/download/dotnet/9.0
- Linux `dotnet publish -r linux-x64 -c Release -o ./bin/Release/net9.0/linux-x64/publish --self-contained /p:PublishSingleFile=true`
- Windows `dotnet publish -r win-x64 -c Release -o ./bin/Release/net9.0/win-x64/publish --self-contained /p:PublishSingleFile=true`

**Installation**
- You need appsettings.json and the compiled binary
- Configure appsettings.json with one or more device MAC addresses and a webhook to send the data
- Run `dotcool.Monitor`

**Requirements**
- Needs Bluetooth LE. Generally, if other bluetooth applications on the device work, then this should also work.
- Windows
  - Usually just works
- Linux
  - Other running applications cannot use the Bluetooth LE adapter, while this application is running
  - Make sure that you can run a scan with `bluetoothctl` and then `scan le`
  - While scan is running, in another terminal session, make sure the scan finds your device and displays ServiceData `btmon | grep -C 10 "your-device-mac-address"`
  - In case of ungraceful termination, a bluetooth scan might remain running. If that happens, you can reset the bluetooth adapter (works for USB Bluetooth Adapters)
    1. `modprobe -r btusb && modprobe btusb && systemctl restart bluetooth`
    2. `bluetoothctl power off && bluetoothctl power on`
