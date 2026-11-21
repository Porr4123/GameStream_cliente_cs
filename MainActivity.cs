using Android.Content;
using Android.Text;
using Android.Views;
using Android.Widget;
using GameStream.Client.Models;
using GameStream.Client.Net;

namespace GameStream.Client;

[Activity(Label = "GameStream", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    private EditText _host = null!, _port = null!, _pin = null!;
    private TextView _status = null!;
    private ListView _list = null!;
    private Button _fetch = null!;
    private List<GameInfo> _games = new();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var prefs = GetSharedPreferences("gamestream", FileCreationMode.Private)!;
        int pad = Dp(20);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(pad, pad, pad, pad);

        _host = Field(root, "IP do servidor (ex.: 192.168.0.10)", InputTypes.ClassText | InputTypes.TextVariationUri,
            prefs.GetString("host", "") ?? "");
        _port = Field(root, "Porta", InputTypes.ClassNumber, prefs.GetString("port", "5055") ?? "5055");
        _pin = Field(root, "PIN mostrado no console do servidor", InputTypes.ClassNumber | InputTypes.NumberVariationPassword,
            prefs.GetString("pin", "") ?? "");

        _fetch = new Button(this) { Text = "Buscar jogos" };
        _fetch.Click += OnFetchClick;
        root.AddView(_fetch);

        _status = new TextView(this) { Text = "Informe o servidor e toque em Buscar jogos." };
        _status.SetPadding(0, Dp(8), 0, Dp(8));
        root.AddView(_status);

        _list = new ListView(this);
        _list.ItemClick += (_, args) => Launch(_games[args.Position]);
        root.AddView(_list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        SetContentView(root);
    }

    private EditText Field(LinearLayout parent, string hint, InputTypes type, string value)
    {
        var edit = new EditText(this) { Hint = hint, Text = value, InputType = type };
        edit.SetSingleLine(true);
        parent.AddView(edit);
        return edit;
    }

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);

    private async void OnFetchClick(object? sender, EventArgs e)
    {
        string host = _host.Text!.Trim();
        string pin = _pin.Text!.Trim();
        if (host.Length == 0 || !int.TryParse(_port.Text, out int port) || pin.Length == 0)
        {
            _status.Text = "Preencha IP, porta e PIN.";
            return;
        }

        GetSharedPreferences("gamestream", FileCreationMode.Private)!.Edit()!
            .PutString("host", host)!.PutString("port", port.ToString())!.PutString("pin", pin)!.Apply();

        _fetch.Enabled = false;
        _status.Text = "Buscando...";
        try
        {
            _games = await ServerApi.GetGamesAsync(host, port, pin);
            _list.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1,
                _games.Select(g => g.Name.Length > 0 ? g.Name : g.Id).ToList());
            _status.Text = _games.Count == 0 ? "Nenhum jogo na pasta do servidor." : "Toque em um jogo para jogar:";
        }
        catch (InvalidOperationException ex)
        {
            _status.Text = ex.Message;
        }
        catch (Exception)
        {
            _status.Text = "Não consegui falar com o servidor. Confira IP, porta, PIN e o firewall do Windows.";
        }
        finally
        {
            _fetch.Enabled = true;
        }
    }

    private void Launch(GameInfo game)
    {
        var intent = new Intent(this, typeof(StreamActivity));
        intent.PutExtra("host", _host.Text!.Trim());
        intent.PutExtra("port", int.Parse(_port.Text!));
        intent.PutExtra("pin", _pin.Text!.Trim());
        intent.PutExtra("game", game.Id);
        intent.PutExtra("layout", System.Text.Json.JsonSerializer.Serialize(game.Layout, ClientJson.Default.ListButtonLayout));
        StartActivity(intent);
    }
}
