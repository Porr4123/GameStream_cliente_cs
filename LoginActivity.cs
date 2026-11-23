using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using GameStream.Client.Models;
using GameStream.Client.Net;
using GameStream.Client.Ui;

namespace GameStream.Client;

/// <summary>Tela 1: login no servidor (IP, porta e PIN). Ao conectar, abre a tela de perfis.</summary>
[Activity(
    Label = "GameStream",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar",
    WindowSoftInputMode = SoftInput.AdjustResize)]
public class LoginActivity : Activity
{
    private EditText _host = null!, _port = null!, _pin = null!;
    private TextView _status = null!;
    private ProgressBar _progress = null!;
    private Button _connect = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window!.SetStatusBarColor(UiKit.Background);
        Window!.SetNavigationBarColor(UiKit.Background);

        var prefs = GetSharedPreferences("gamestream", FileCreationMode.Private)!;
        int pad = UiKit.Dp(this, 24);

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.SetPadding(pad, pad, pad, pad);
        column.SetGravity(GravityFlags.CenterVertical);

        var title = UiKit.Label(this, "GameStream", 34, Color.White, bold: true);
        title.Gravity = GravityFlags.Center;
        column.AddView(title);

        var subtitle = UiKit.Label(this, "Entre no servidor do seu PC", 15, UiKit.TextMuted);
        subtitle.Gravity = GravityFlags.Center;
        subtitle.SetPadding(0, UiKit.Dp(this, 4), 0, UiKit.Dp(this, 24));
        column.AddView(subtitle);

        var card = UiKit.Card(this);

        _host = AddField(card, "IP do servidor", "Ex.: 192.168.0.10",
            InputTypes.ClassText | InputTypes.TextVariationUri, prefs.GetString("host", "") ?? "");
        _port = AddField(card, "Porta", "5055", InputTypes.ClassNumber, prefs.GetString("port", "5055") ?? "5055");
        _pin = AddField(card, "PIN", "Mostrado no console do servidor",
            InputTypes.ClassNumber | InputTypes.NumberVariationPassword, prefs.GetString("pin", "") ?? "");
        _pin.ImeOptions = ImeAction.Done;
        _pin.EditorAction += (_, e) =>
        {
            if (e.ActionId == ImeAction.Done) OnConnectClick(null, EventArgs.Empty);
        };

        _connect = UiKit.MakeButton(this, "Conectar", BtnStyle.Primary);
        _connect.Click += OnConnectClick;
        var connectParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        connectParams.TopMargin = UiKit.Dp(this, 20);
        card.AddView(_connect, connectParams);

        _progress = new ProgressBar(this) { Indeterminate = true, Visibility = ViewStates.Gone };
        card.AddView(_progress, new LinearLayout.LayoutParams(UiKit.Dp(this, 28), UiKit.Dp(this, 28))
        {
            Gravity = GravityFlags.CenterHorizontal,
            TopMargin = UiKit.Dp(this, 12)
        });

        _status = UiKit.Label(this, "", 14, UiKit.Danger);
        _status.Gravity = GravityFlags.Center;
        _status.SetPadding(0, UiKit.Dp(this, 12), 0, 0);
        _status.Visibility = ViewStates.Gone;
        card.AddView(_status);

        column.AddView(card);

        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.SetBackgroundColor(UiKit.Background);
        scroll.AddView(column);
        SetContentView(scroll);
    }

    private EditText AddField(LinearLayout parent, string caption, string hint, InputTypes type, string value)
    {
        var label = UiKit.Label(this, caption, 13, UiKit.TextMuted, bold: true);
        label.SetPadding(0, UiKit.Dp(this, 10), 0, UiKit.Dp(this, 4));
        parent.AddView(label);

        var edit = UiKit.Input(this, hint, value, type);
        parent.AddView(edit, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return edit;
    }

    private void ShowMessage(string text)
    {
        _status.Text = text;
        _status.Visibility = text.Length == 0 ? ViewStates.Gone : ViewStates.Visible;
    }

    private async void OnConnectClick(object? sender, EventArgs e)
    {
        if (!_connect.Enabled) return;

        string host = _host.Text!.Trim();
        string pin = _pin.Text!.Trim();
        if (host.Length == 0 || !int.TryParse(_port.Text, out int port) || pin.Length == 0)
        {
            ShowMessage("Preencha o IP, a porta e o PIN.");
            return;
        }

        (GetSystemService(InputMethodService) as InputMethodManager)?.HideSoftInputFromWindow(_pin.WindowToken, HideSoftInputFlags.None);

        _connect.Enabled = false;
        _progress.Visibility = ViewStates.Visible;
        ShowMessage("");
        try
        {
            var games = await ServerApi.GetGamesAsync(host, port, pin);

            GetSharedPreferences("gamestream", FileCreationMode.Private)!.Edit()!
                .PutString("host", host)!.PutString("port", port.ToString())!.PutString("pin", pin)!.Apply();

            var intent = new Intent(this, typeof(ProfilesActivity));
            intent.PutExtra("host", host);
            intent.PutExtra("port", port);
            intent.PutExtra("pin", pin);
            intent.PutExtra("games", System.Text.Json.JsonSerializer.Serialize(games, ClientJson.Default.ListGameInfo));
            StartActivity(intent);
        }
        catch (InvalidOperationException ex)
        {
            ShowMessage(ex.Message); // ex.: PIN incorreto
        }
        catch (Exception)
        {
            ShowMessage("Não consegui falar com o servidor. Confira o IP, a porta, o PIN e o firewall do Windows.");
        }
        finally
        {
            _connect.Enabled = true;
            _progress.Visibility = ViewStates.Gone;
        }
    }
}
