using Android.App;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

// O servidor fala HTTP/WebSocket sem TLS (só rede local), então o Android precisa liberar tráfego em texto puro.
[assembly: Application(Label = "GameStream", UsesCleartextTraffic = true)]
