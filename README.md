# GameStream.Client

Cliente Android nativo (C#/.NET for Android, `net10.0-android`) do GameStream.Server.

## Compilar no Visual Studio
1. Instale a carga de trabalho **Desenvolvimento para dispositivos móveis com .NET** (inclui o workload `android`).
2. Abra `GameStream.sln`, defina `GameStream.Client` como projeto de inicialização e rode no aparelho ou emulador.
3. ABIs: `android-arm` (armeabi-v7a) e `android-arm64` (arm64-v8a), no mesmo APK (`RuntimeIdentifiers` no `.csproj`).
   APK de Release: `dotnet publish GameStream.Client -c Release`.

## Uso
O app tem duas telas:

1. **Login**: informe IP, porta (5055) e o PIN mostrado no console do servidor e toque em **Conectar**.
2. **Perfis**: escolha ou edite um perfil, escolha o jogo e toque em **Jogar**.

O celular e o PC precisam estar na mesma rede, com a porta 5055 liberada no firewall do Windows.

### Imagem na tela
Na tela de perfis, o cartão **Imagem na tela** define como o jogo ocupa o celular: **Ajustar** (jogo inteiro, pode sobrar
faixa preta), **Preencher** (ocupa tudo cortando as bordas) ou **Esticar** (ocupa tudo deformando).
O app informa ao servidor o tamanho da tela e o servidor gera o vídeo já na proporção dela, mantendo a altura do perfil do
jogo. Isso exige o servidor atualizado; com um servidor antigo o vídeo continua como antes (faixas pretas, sem erro).
O app também usa a área do recorte da câmera (notch) em vez de deixar uma barra preta.

### Perfis
- Até **5 perfis** por aparelho (aba "+ Novo"), cada um com nome e disposição própria dos controles.
  Um perfil novo começa com o layout padrão do jogo escolhido.
- Na prévia, **toque** em um controle para selecioná-lo e **arraste** para movê-lo.
- **+ Botão** e **+ Joystick** adicionam controles (até 24 por perfil). **Layout do jogo** substitui os controles
  pelo padrão do jogo escolhido.
- Para cada botão você escolhe a tecla que ele envia: **números (0-9), letras (A-Z), setas, Ctrl, Shift e Esc**; também dá
  para trocar o texto e o tamanho. No joystick você escolhe a tecla de cada direção (cima, esquerda, baixo, direita).
- O servidor só injeta letras, números, setas, Ctrl, Shift e Esc (a lista `safeKeys` que ele informa em `/games`).
  Se um botão usar outra tecla, ela aparece em amarelo e o app mostra um aviso: o servidor vai ignorar esse botão.
  Servidores antigos, sem `safeKeys`, usam a lista `allowedKeys` do jogo.
- **Ctrl e Esc não funcionam juntos**: o servidor bloqueia essa combinação (ela abriria o menu Iniciar do Windows) e o app
  avisa quando um perfil tem os dois.

## Estrutura
- `LoginActivity.cs`: tela 1, login no servidor (`GET /games` valida o PIN).
- `ProfilesActivity.cs`: tela 2, perfis, editor de controles, escolha do jogo.
- `StreamActivity.cs`: vídeo (SurfaceView + MediaCodec) e botões na tela.
- `Net/`: `ServerApi` (HTTP) e `StreamClient` (WebSocket `/stream`).
- `Video/H264Decoder.cs`: decodificação H.264 Annex B por hardware (com decodificadores de software de reserva).
- `Video/AnnexB.cs`: leitura de SPS/PPS do fluxo H.264 (vai em csd-0/csd-1 do MediaCodec).
- `Ui/ControlsView.cs`: botões e joystick com multitoque; no modo de edição vira o editor (selecionar e arrastar).
- `Ui/KeyPicker.cs`, `Ui/UiKit.cs`: janela de escolha de tecla e estilos da interface.
- `Data/ProfileStore.cs`: perfis salvos no aparelho (JSON em SharedPreferences).
- `Models/KeyCatalog.cs`: teclas que o usuário pode escolher.

## Se a imagem não aparecer
Enquanto o vídeo não chega, a tela do jogo mostra uma linha com os contadores: quadros recebidos, keyframes,
exibidos e descartados, e o nome do decodificador em uso. Isso separa os casos:
- **recebidos = 0**: o servidor conectou mas não está mandando vídeo (olhe o console do servidor e o ffmpeg).
- **recebidos > 0 e keyframes = 0**: o fluxo chega sem keyframes (olhe a saída do ffmpeg no console do servidor).
- **recebidos e keyframes > 0, exibidos = 0**: o decodificador do aparelho não está aceitando o vídeo.
  Rode `adb logcat -s GameStream` para ver qual decodificador foi aberto ou por que cada um falhou.

O decodificador é aberto só no primeiro keyframe, com SPS/PPS no `MediaFormat`; se o de hardware recusar,
o app tenta os de software do Android antes de desistir.

## Som

Quando o servidor tem som ligado, o `hello` traz `"audio":{"codec":"aac","rate":48000,"ch":2}` e o servidor envia mensagens binárias `[2][0][quadro AAC cru]`. O cliente decodifica com `MediaCodec` (`audio/mp4a-latm`, AudioSpecificConfig montado a partir da taxa e dos canais) e toca num `AudioTrack` de baixa latência (`Video/AacPlayer.cs`). Os botões de volume do aparelho controlam o som. Se o aparelho não conseguir abrir o som, o vídeo segue normalmente.
