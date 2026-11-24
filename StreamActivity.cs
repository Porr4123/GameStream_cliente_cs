using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;
using GameStream.Client.Models;
using GameStream.Client.Net;
using GameStream.Client.Ui;
using GameStream.Client.Video;

namespace GameStream.Client;

[Activity(
    Label = "GameStream",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen")]
public class StreamActivity : Activity, ISurfaceHolderCallback
{
    private FrameLayout _root = null!;
    private AspectSurfaceView _surface = null!;
    private ControlsView _controls = null!;
    private TextView _status = null!;

    private CancellationTokenSource? _cts;
    private StreamClient? _client;
    private volatile H264Decoder? _decoder;
    private bool _started;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window!.AddFlags(WindowManagerFlags.KeepScreenOn);

        // Em celulares com recorte (notch), ocupa também a faixa do recorte em vez de deixar uma barra preta ali.
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            var attrs = Window.Attributes!;
            attrs.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            Window.Attributes = attrs;
        }

        var root = _root = new FrameLayout(this);
        root.SetBackgroundColor(Color.Black);

        _surface = new AspectSurfaceView(this);
        _surface.Holder!.AddCallback(this);
        root.AddView(_surface, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));

        _controls = new ControlsView(this);
        _controls.KeyChanged = (key, down) => _client?.SendKey(key, down);
        var layoutJson = Intent!.GetStringExtra("layout") ?? "[]";
        _controls.SetLayout(System.Text.Json.JsonSerializer.Deserialize(layoutJson, ClientJson.Default.ListButtonLayout) ?? new List<ButtonLayout>());
        root.AddView(_controls, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        _status = new TextView(this) { Text = "Conectando (o jogo está abrindo)..." };
        _status.SetTextColor(Color.White);
        _status.TextSize = 18f;
        root.AddView(_status, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center));

        SetContentView(root);
        HideSystemBars();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) HideSystemBars();
    }

#pragma warning disable CA1422 // SystemUiVisibility está obsoleto, mas funciona em todas as versões suportadas (API 24+)
    private void HideSystemBars()
    {
        Window!.DecorView.SystemUiVisibility = (StatusBarVisibility)(
            SystemUiFlags.ImmersiveSticky | SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen |
            SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen);
    }
#pragma warning restore CA1422

    // ---- Surface: só conecta depois que existe onde desenhar o vídeo ----

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        if (_started) return;
        _started = true;
        _cts = new CancellationTokenSource();
        var surface = holder.Surface!;
        var ct = _cts.Token;

        // Tela real em pixels: o servidor gera o quadro na proporção dela (no modo deitado, largura >= altura).
        int w = _root.Width, h = _root.Height;
        if (w <= 0 || h <= 0)
        {
            var metrics = Resources!.DisplayMetrics!;
            w = metrics.WidthPixels;
            h = metrics.HeightPixels;
        }
        int screenW = Math.Max(w, h), screenH = Math.Min(w, h);
        string fit = Intent!.GetStringExtra("fit") ?? "fit";

        _ = Task.Run(() => RunSessionAsync(surface, screenW, screenH, fit, ct));
    }

    public void SurfaceChanged(ISurfaceHolder holder, Android.Graphics.Format format, int width, int height)
    {
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        Stop();
    }

    private async Task RunSessionAsync(Surface surface, int screenW, int screenH, string fit, CancellationToken ct)
    {
        var intent = Intent!;
        string host = intent.GetStringExtra("host") ?? "";
        int port = intent.GetIntExtra("port", 5055);
        string pin = intent.GetStringExtra("pin") ?? "";
        string game = intent.GetStringExtra("game") ?? "";

        var client = new StreamClient();
        _client = client;

        client.Hello += (w, h, fps) =>
        {
            Log.Info("GameStream", $"hello: {w}x{h} @ {fps}fps");
            RunOnUiThread(() => _surface.SetAspect(w, h));

            var decoder = new H264Decoder(surface, w, h, fps);
            decoder.FirstFrame += () => RunOnUiThread(() => _status.Visibility = ViewStates.Gone);
            decoder.Error += msg => ShowStatus(msg);
            _decoder = decoder;

            RunOnUiThread(() => _status.Text = "Aguardando a imagem do jogo...");
            _ = ShowDecoderStatsAsync(decoder, ct);
        };

        client.Video += (buffer, offset, length, keyFrame) => _decoder?.Feed(buffer, offset, length, keyFrame);

        client.ServerError += msg => ShowStatus("Erro do servidor: " + msg);

        try
        {
            await client.RunAsync(host, port, game, pin, ct, screenW, screenH, fit);
            if (!ct.IsCancellationRequested) ShowStatus("Desconectado do servidor.");
        }
        catch (OperationCanceledException)
        {
            // saiu da tela
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Warn("GameStream", $"sessão terminou com erro: {ex}");
            ShowStatus($"A conexão caiu ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>Enquanto a imagem não aparece, mostra quantos quadros chegaram/foram exibidos (ajuda a achar o problema).</summary>
    private async Task ShowDecoderStatsAsync(H264Decoder decoder, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !decoder.HasRendered && !decoder.Failed)
            {
                await Task.Delay(1000, ct);
                if (decoder.HasRendered || decoder.Failed) break;
                RunOnUiThread(() =>
                {
                    if (_status.Visibility == ViewStates.Visible && !decoder.HasRendered && !decoder.Failed)
                        _status.Text = "Aguardando a imagem do jogo...\n" + decoder.StatusLine;
                });
            }
        }
        catch (OperationCanceledException)
        {
            // saiu da tela
        }
    }

    private void ShowStatus(string text)
    {
        RunOnUiThread(() =>
        {
            _status.Text = text;
            _status.Visibility = ViewStates.Visible;
        });
    }

    private void Stop()
    {
        _cts?.Cancel();
        _controls?.ReleaseAll();

        _client?.Dispose();
        _client = null;

        _decoder?.Dispose();
        _decoder = null;

        _started = false;
    }

    protected override void OnStop()
    {
        base.OnStop();
        // A sessão no servidor termina junto (o jogo é fechado), então voltar ao app leva à tela inicial.
        Stop();
        Finish();
    }

    protected override void OnDestroy()
    {
        Stop();
        base.OnDestroy();
    }
}
