# kissproxy / kissproxylib

kissproxy is a serial-to-TCP proxy for serial KISS modems, including MQTT tracing support. That is, a program which connects to a serial KISS modem, makes it available on a TCP port so it can continue to be used as normal by node software, and optionally also outputs all the traffic between the node software and the modem to an MQTT host of your choosing, with various options.

For a tool which can subscribe to these topics and produce Wireshark-readable output, see [Ax25Mqtt2pcap](https://github.com/M0LTE/Ax25Mqtt2pcap).

kissproxylib is a .NET library version of the above, which can be used to integrate a KISS proxy with optional MQTT support into some other application.

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/Y8Y8KFHA0)

## kissproxy

### Usage

```
$ ./kissproxy
Description:
  
Usage:
  kissproxy [options]

Options:
  -c, --comport <comport> (REQUIRED)  The COM port the modem is connected to, e.g. /dev/ttyACM0
  -b, --baud <baud>                   The baud rate of the modem [default: 57600]
  -p, --tcpport <tcpport>             The TCP port to listen on [default: 8910]
  -a, --anyhost                       Whether to accept connections from any host, instead of just localhost [default: False]
  -m, --mqtt-server <mqtt-server>     MQTT server to forward KISS frames to
  -mu, --mqtt-user <mqtt-user>        MQTT username
  -mp, --mqtt-pass <mqtt-pass>        MQTT password
  --base64                            Publish base64 strings rather than raw bytes
  --version                           Show version information
  -?, -h, --help                      Show help and usage information
```

### Topics

This program outputs to a topic named as follows:

```
kissproxy/$hostname/$comport/$direction
```

`$direction` can be `toModem` or `fromModem`.

Under that topic are further sub-topics:

`/framed` - the payload is the raw traffic between the node and the modem, with its KISS framing intact.

`/unframed/$kissport/$kisscommand` - for these topics, the MQTT payload is the unpacked/unescaped frame, normally but not necessarily AX.25, separated by TNC port and KISS command id.

The KISS command represents the command byte unpacked from the KISS framing.

`/decoded/$kissport` - the payload is a human-readable representation. Requires ax2txt (https://github.com/Online-Amateur-Radio-Club-M0OUK/ax2txt) binary to be present at /opt/ax2txt/ax2txt.

The KISS port will always be port0 for single-port TNCs.

### Windows usage

Should just be a case of 

```
git clone https://github.com/M0LTE/kissproxy.git
cd kissproxy\src\kissproxy
dotnet run
```

assuming .NET 8 is installed. Then point LinBPQ at it as below. Awaiting feedback / demonstrated need, since it's not too common to run a packet node on Windows these days, let alone debug one.

### Linux Build / Install

#### Prerequisites
.NET 8 SDK:
```
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel LTS
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$HOME/.dotnet' >> ~/.bashrc
source ~/.bashrc
dotnet --version
```

#### Build

Tested on 32 bit Raspberry Pi OS Lite v11 on Raspberry Pi 2B rev 1.1. Adjust publish profile for other platforms as required.

```
git clone https://github.com/M0LTE/kissproxy.git
cd kissproxy
dotnet publish src/kissproxy/kissproxy.csproj --configuration Release -p:PublishProfile=src/kissproxy/Properties/PublishProfiles/Linux-arm32.pubxml
```

#### Install as systemd service

First stop LinBPQ to free up the modem port.

Note the modem COM port and MQTT server specified below - adjust as required before running.

```
sudo mkdir /opt/kissproxy
sudo mv src/publish/* /opt/kissproxy/
sudo sh -c 'echo "[Unit]
After=network.target
[Service]
ExecStart=/opt/kissproxy/kissproxy --comport /dev/ttyACM0 --mqtt-server myhost
WorkingDirectory=/opt/kissproxy
Restart=always
[Install]
WantedBy=multi-user.target" > /etc/systemd/system/kissproxy.service'
sudo systemctl enable kissproxy
sudo systemctl start kissproxy
```

#### Configure linbpq (example)

In your `bpq32.cfg`, find your KISS port of interest.

Change from like this:

```
PORT
  PORTNUM=1
  ID=VHF
  TYPE=ASYNC
  PROTOCOL=KISS
  COMPORT=/dev/serial/by-path/platform-3f980000.usb-usb-0:1.2:1.0
  SPEED=57600
  ...
ENDPORT
```

to this:

```
PORT
  PORTNUM=1
  ID=VHF
  TYPE=ASYNC
  PROTOCOL=KISS
  IPADDR=127.0.0.1
  TCPPORT=8910
  ...
ENDPORT
```

This causes BPQ to connect to the modem via this program, rather than directly.

Start up LinBPQ and check syslog to ensure kissproxy reports it has received a node connection, has connected to the modem, and has connected to your MQTT server.

#### Upgrade

```
cd kissproxy
git pull
dotnet publish src/kissproxy/kissproxy.csproj --configuration Release -p:PublishProfile=src/kissproxy/Properties/PublishProfiles/Linux-arm32.pubxml
sudo systemctl stop kissproxy
sudo mv src/publish/* /opt/kissproxy/
sudo systemctl start kissproxy
```

#### Multi-port

If you want to run more than one TNC, don't specify any command line parameters. Instead, place `/etc/kissproxy.conf` like this:

```
[
  {
      "id": "2m",
      "comPort": "/dev/serial/by-path/platform-3f980000.usb-usb-0:1.2:1.0",
      "tcpPort": 8910,
      "mqttServer": "mqtt"
  }, {
      "id": "70cm",
      "comPort": "/dev/serial/by-path/platform-3f980000.usb-usb-0:1.3:1.0",
      "tcpPort": 8911,
      "mqttServer": "mqtt"
  }
]
```

## kissproxylib

A .NET library version of the above, which can be used to integrate a KISS proxy with optional MQTT support into some other application.

```
dotnet add package m0lte.kissproxylib
```

then simply:

```
var proxy = new KissProxy(new ConsoleLogger());
await proxy.Run("/dev/tnc-port");
```

That will spin up a KISS proxy for the modem at `/dev/tnc-port`, on TCP port 8910, with the default serial baud rate of 57600, and no MQTT output.

Optional parameters are available:

```
await proxy.Run("/dev/tnc-port", 
    modemBaud: 57600, 
    listenForNodeOnTcpPort: 8910, 
    allowTcpConnectFromOtherHosts: false, 
    mqttServer = "server", 
    mqttUser = "user", 
    mqttPassword = "password", 
    emitFramesToMqttAsBase64String: false);
```

## Licence

MIT. Fill your boots.
