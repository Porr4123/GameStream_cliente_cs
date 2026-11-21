# GameStream.Client

Cliente Android nativo (C#/.NET for Android, `net10.0-android`) do GameStream.Server.

## Compilar no Visual Studio
1. Instale a carga de trabalho **Desenvolvimento para dispositivos móveis com .NET** (inclui o workload `android`).
2. Abra `GameStream.sln`, defina `GameStream.Client` como projeto de inicialização e rode no aparelho ou emulador.
3. ABIs: `android-arm` (armeabi-v7a) e `android-arm64` (arm64-v8a), no mesmo APK (`RuntimeIdentifiers` no `.csproj`).
   APK de Release: `dotnet publish GameStream.Client -c Release`.

## Uso
Informe IP, porta (5055) e o PIN mostrado no console do servidor, toque em **Buscar jogos** e escolha um jogo.
O celular e o PC precisam estar na mesma rede, com a porta 5055 liberada no firewall do Windows.

## Estrutura
- `MainActivity.cs`: tela de conexão e lista de jogos (`GET /games`).
- `StreamActivity.cs`: vídeo (SurfaceView + MediaCodec) e botões na tela.
- `Net/`: `ServerApi` (HTTP) e `StreamClient` (WebSocket `/stream`).
- `Video/H264Decoder.cs`: decodificação H.264 Annex B por hardware (com decodificadores de software de reserva).
- `Video/AnnexB.cs`: leitura de SPS/PPS do fluxo H.264 (vai em csd-0/csd-1 do MediaCodec).
- `Ui/ControlsView.cs`: botões e dpad com multitoque, desenhados a partir do `layout` do servidor.

## Se a imagem não aparecer
Enquanto o vídeo não chega, a tela do jogo mostra uma linha com os contadores: quadros recebidos, keyframes,
exibidos e descartados, e o nome do decodificador em uso. Isso separa os casos:
- **recebidos = 0**: o servidor conectou mas não está mandando vídeo (olhe o console do servidor e o ffmpeg).
- **recebidos > 0 e keyframes = 0**: o fluxo chega sem keyframes (olhe a saída do ffmpeg no console do servidor).
- **recebidos e keyframes > 0, exibidos = 0**: o decodificador do aparelho não está aceitando o vídeo.
  Rode `adb logcat -s GameStream` para ver qual decodificador foi aberto ou por que cada um falhou.

O decodificador é aberto só no primeiro keyframe, com SPS/PPS no `MediaFormat`; se o de hardware recusar,
o app tenta os de software do Android antes de desistir.
