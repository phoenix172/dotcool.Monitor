$address = "$(cat ~/.ssh/debian_host.host)"
$pass = "$(cat ~/.ssh/debian_host.pass)"
function sh([string]$command) {
    $escaped = $command -replace "'", "'\\''"
    ssh $address "echo $pass | sudo -S bash -c '$escaped'"
}
sh 'pkill -TERM -f dotCool'
sh 'pkill -9 -f dotnet'
sh "modprobe -r btusb && modprobe btusb && systemctl restart bluetooth"
sh "bluetoothctl power off && bluetoothctl power on"
rm -Force ./bin/Release/net9.0/linux-x64/publish/*
dotnet publish -r linux-x64 -c Debug -o ./bin/Release/net9.0/linux-x64/publish
sh "rm -f ./dotcool/*"
scp ./bin/Release/net9.0/linux-x64/publish/* "$address`:~/dotcool/"
sh "mv -f ./dotcool/appsettings.Development.json ./dotcool/appsettings.json"
sh "cd ./dotcool && sudo dotnet dotCool.Monitor.dll"