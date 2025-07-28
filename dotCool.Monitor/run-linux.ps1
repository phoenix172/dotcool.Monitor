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
sh "cd ./dotcool && rm -f dotCool/*"
scp ./bin/Release/net9.0/linux-x64/publish/* "$address:dotcool/"
sh "cd ./dotcool && dotnet dotCool.Monitor.dll"