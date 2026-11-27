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

### Mouse na tela
O servidor aceita mouse isolado: ele posta os cliques direto na janela do jogo, sem mexer no mouse do PC. O app tem duas
camadas de toque sobre o vídeo: embaixo a **camada de mouse**, em cima os **botões e o joystick** do perfil.

- **Toque** (sem andar): clique esquerdo.
- **Dois toques rápidos** (o 2º até `doubleTapMs` depois do 1º, perto dele): clique direito. O 1º toque já manda o clique
  esquerdo na hora (sem esperar, latência zero), então no duplo toque o servidor recebe esquerdo e depois direito.
- **Segurar** o dedo parado por `holdMs`: botão esquerdo apertado (vibra curto). Andar arrasta; levantar solta.
- **Andar antes de `holdMs`**: só move o cursor, sem clicar.

Quem manda no dedo é decidido **quando ele desce** e vale até ele levantar: o dedo que começa num botão (ou na folga em volta
dele, ou numa caixa de exclusão do perfil) é dos botões, mesmo se arrastar; o que começa fora é do mouse, mesmo se passar por
cima de um botão. Cada dedo é decidido separadamente, então um dedo no botão e outro no mouse funcionam juntos. Toque na
faixa preta (fora do vídeo) não vira mouse. Só um dedo comanda o cursor por vez.

O servidor liga o mouse no `hello` (`"mouse":{"enabled":true,"exclude":[...],"buttonMargin":0.02,"holdMs":350,"doubleTapMs":300}`).
Sem esse campo (servidor antigo) ou com `enabled` falso, o app fica como antes. No app, cada perfil tem a opção
**Mouse na tela** (padrão: ligada); ela só vale se o servidor também ligar. Mensagens enviadas:
`{"t":"mouse","a":"move|down|up|click","b":"left|right","x":0..1,"y":0..1}`, com x e y relativos ao quadro de vídeo inteiro
(o servidor descarta o que cai nas faixas pretas). Todo `down` é seguido de um `up`: ao levantar, cancelar o toque, ir para segundo
plano ou perder a conexão, nenhum botão do mouse fica preso. O app avisa no log do servidor (`[cliente] mouse: ...`) se a
camada ficou ligada. O editor de botões (tela de perfis) não usa a camada de mouse.

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
- `Ui/TouchRouter.cs`, `Ui/PointerOwnership.cs`: decidem de quem é cada dedo (botões ou mouse) quando ele desce.
- `Ui/MouseLayerView.cs`, `Ui/MouseGestureEngine.cs`: a camada de mouse e os gestos (toque, duplo toque, segurar).
- `Ui/ButtonHitArea.cs`: área de toque dos botões e do joystick (a mesma usada para apertar teclas).
- `Models/MouseSettings.cs`, `Net/MouseMessage.cs`: leitura de `hello.mouse` e a mensagem `{"t":"mouse",...}`.
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

## Se o som não tocar
O app relata o estado do som ao servidor, que imprime no log como `[cliente] ...`:
- `app com diagnóstico de som; aparelho ...; hello ...; som anunciado pelo servidor`: aparece uma vez, ao conectar. **Se esta
  linha não aparece, o APK instalado é antigo**: reinstale a versão nova. "som NÃO anunciado" significa que o servidor não
  mandou `audio` no `hello` (som desligado ou captura que não abriu).
- `som: decodificador AAC aberto (48000 Hz, 2 canais)`: o decodificador do aparelho abriu. Se vier `FALHOU ao abrir`, o motivo vem junto.
- `som: Som: N quadros recebidos, N tocados, N descartados; decodificador=...; saída=...; pico=.../32768; trilha=...;
  volume de mídia do aparelho: x/y`, aos 2 s e a cada 5 s por 30 s:
  - **recebidos = 0**: os quadros de som não estão chegando ao app (rede ou servidor).
  - **recebidos > 0 e tocados = 0**: o decodificador não está entregando som; veja `decodificador` e `saída`.
  - **tocados > 0 e pico = 0**: o decodificador entrega silêncio.
  - **pico > 0 e volume de mídia = 0**: o som chega ao aparelho, mas o volume de mídia está no mínimo (use os botões de volume).
  - **pico > 0, volume > 0 e sem som**: problema na saída do aparelho (Bluetooth, fones, modo silencioso do fabricante).
