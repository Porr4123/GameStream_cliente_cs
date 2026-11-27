using System.Text.Json;
using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Widget;
using GameStream.Client.Data;
using GameStream.Client.Models;
using GameStream.Client.Net;
using GameStream.Client.Ui;

namespace GameStream.Client;

/// <summary>
/// Tela 2: perfis. Até 5 perfis, cada um com sua disposição de botões e joystick e a tecla que cada um envia
/// (números, letras, Ctrl, Shift e Esc). Também escolhe o jogo e inicia a transmissão.
/// </summary>
[Activity(
    Label = "Perfis",
    Theme = "@android:style/Theme.Material.NoActionBar",
    WindowSoftInputMode = SoftInput.AdjustResize)]
public class ProfilesActivity : Activity
{
    private ISharedPreferences _prefs = null!;
    private string _host = "", _pin = "";
    private int _port;

    private List<GameInfo> _games = new();
    private List<Profile> _profiles = new();
    private int _current;
    private int _selected = -1;     // controle selecionado no perfil atual
    private bool _suppressEvents;

    private static readonly (string Mode, string Title, string Hint)[] FitModes =
    {
        ("fit", "Ajustar", "Mostra o jogo inteiro. Podem sobrar faixas pretas."),
        ("fill", "Preencher", "Ocupa a tela toda cortando as bordas do jogo."),
        ("stretch", "Esticar", "Ocupa a tela toda, mas deforma a imagem.")
    };
    private string _fit = "fit";

    private LinearLayout _chips = null!, _panel = null!, _fitChips = null!;
    private TextView _fitHint = null!;
    private TextView _counter = null!, _warning = null!;
    private EditText _name = null!;
    private Switch _mouseSwitch = null!;
    private ControlsView _preview = null!;
    private Spinner _gameSpinner = null!;
    private Button _delete = null!, _play = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window!.SetStatusBarColor(UiKit.Background);
        Window!.SetNavigationBarColor(UiKit.Background);

        _prefs = GetSharedPreferences("gamestream", FileCreationMode.Private)!;
        var intent = Intent!;
        _host = intent.GetStringExtra("host") ?? "";
        _port = intent.GetIntExtra("port", 5055);
        _pin = intent.GetStringExtra("pin") ?? "";
        _games = JsonSerializer.Deserialize(intent.GetStringExtra("games") ?? "[]", ClientJson.Default.ListGameInfo)
                 ?? new List<GameInfo>();

        _profiles = ProfileStore.Load(_prefs);

        BuildUi();
        FillGameSpinner(_prefs.GetString("selGame", null));

        _fit = _prefs.GetString("fit", "fit") ?? "fit";
        if (!FitModes.Any(f => f.Mode == _fit)) _fit = "fit";
        RebuildFitChips();

        if (_profiles.Count == 0)
        {
            _profiles.Add(NewProfile());
            SaveProfiles();
        }
        _current = Math.Clamp(_prefs.GetInt("selProfile", 0), 0, _profiles.Count - 1);
        LoadCurrentProfile();
    }

    // ---------------------------------------------------------------- montagem da tela

    private void BuildUi()
    {
        int pad = UiKit.Dp(this, 16);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(UiKit.Background);

        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(pad, pad, pad, pad);

        // --- cabeçalho
        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        var titles = new LinearLayout(this) { Orientation = Orientation.Vertical };
        titles.AddView(UiKit.Label(this, "Perfis", 26, Color.White, bold: true));
        titles.AddView(UiKit.Label(this, $"Servidor {_host}:{_port}", 13, UiKit.TextMuted));
        header.AddView(titles, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        var logout = UiKit.MakeButton(this, "Sair", BtnStyle.Secondary);
        logout.Click += (_, _) => Finish();
        header.AddView(logout);
        content.AddView(header);

        // --- abas dos perfis
        _counter = UiKit.Label(this, "", 13, UiKit.TextMuted);
        _counter.SetPadding(0, UiKit.Dp(this, 16), 0, UiKit.Dp(this, 6));
        content.AddView(_counter);

        var chipScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        _chips = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        chipScroll.AddView(_chips);
        content.AddView(chipScroll);

        // --- nome do perfil
        var nameCard = UiKit.Card(this);
        nameCard.AddView(UiKit.Label(this, "Nome do perfil", 13, UiKit.TextMuted, bold: true));
        var nameRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        nameRow.SetGravity(GravityFlags.CenterVertical);
        _name = UiKit.Input(this, "Nome", "", InputTypes.ClassText | InputTypes.TextFlagCapSentences);
        _name.TextChanged += (_, _) => OnNameChanged();
        nameRow.AddView(_name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        _delete = UiKit.MakeButton(this, "Excluir", BtnStyle.Danger);
        _delete.Click += (_, _) => ConfirmDeleteProfile();
        var deleteParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        deleteParams.LeftMargin = UiKit.Dp(this, 8);
        nameRow.AddView(_delete, deleteParams);
        var nameRowParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        nameRowParams.TopMargin = UiKit.Dp(this, 6);
        nameCard.AddView(nameRow, nameRowParams);

        // Mouse na tela: opção do perfil. O servidor também precisa ligar o mouse para o jogo.
        _mouseSwitch = new Switch(this) { Text = "Mouse na tela (toque, duplo toque, segurar)" };
        _mouseSwitch.SetTextColor(Color.White);
        _mouseSwitch.CheckedChange += (_, e) =>
        {
            if (_suppressEvents) return;
            CurrentProfile.MouseEnabled = e.IsChecked;
            SaveProfiles();
        };
        var mouseParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        mouseParams.TopMargin = UiKit.Dp(this, 12);
        nameCard.AddView(_mouseSwitch, mouseParams);
        nameCard.AddView(UiKit.Label(this,
            "Toque = clique esquerdo; dois toques rápidos = clique direito; segurar = arrastar. Os botões deste perfil continuam funcionando. Só vale se o servidor ligar o mouse para o jogo.",
            12, UiKit.TextMuted));
        content.AddView(nameCard, CardParams());

        // --- editor de controles
        var editorCard = UiKit.Card(this);
        editorCard.AddView(UiKit.Label(this, "Controles", 13, UiKit.TextMuted, bold: true));
        var hint = UiKit.Label(this, "Toque em um controle para editar e arraste para mover.", 12, UiKit.TextMuted);
        hint.SetPadding(0, UiKit.Dp(this, 2), 0, UiKit.Dp(this, 8));
        editorCard.AddView(hint);

        _preview = new ControlsView(this) { EditMode = true };
        _preview.SelectionChanged = index =>
        {
            _selected = index;
            RebuildPanel();
        };
        _preview.LayoutEdited = SaveAndRefresh;
        editorCard.AddView(_preview, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var addRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var addButton = UiKit.MakeButton(this, "+ Botão", BtnStyle.Secondary);
        addButton.Click += (_, _) => AddControl(joystick: false);
        var addJoystick = UiKit.MakeButton(this, "+ Joystick", BtnStyle.Secondary);
        addJoystick.Click += (_, _) => AddControl(joystick: true);
        var useGame = UiKit.MakeButton(this, "Layout do jogo", BtnStyle.Secondary);
        useGame.Click += (_, _) => ConfirmUseGameLayout();
        addRow.AddView(addButton, RowParams(left: 0));
        addRow.AddView(addJoystick, RowParams(left: 6));
        addRow.AddView(useGame, RowParams(left: 6));
        var addRowParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        addRowParams.TopMargin = UiKit.Dp(this, 10);
        editorCard.AddView(addRow, addRowParams);
        content.AddView(editorCard, CardParams());

        // --- controle selecionado
        _panel = UiKit.Card(this);
        content.AddView(_panel, CardParams());

        // --- jogo
        var gameCard = UiKit.Card(this);
        gameCard.AddView(UiKit.Label(this, "Jogo", 13, UiKit.TextMuted, bold: true));
        var gameRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        gameRow.SetGravity(GravityFlags.CenterVertical);
        _gameSpinner = new Spinner(this);
        _gameSpinner.ItemSelected += (_, e) => OnGameSelected(e.Position);
        gameRow.AddView(_gameSpinner, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        var refresh = UiKit.MakeButton(this, "Atualizar", BtnStyle.Secondary);
        refresh.Click += async (_, _) => await RefreshGamesAsync(refresh);
        var refreshParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        refreshParams.LeftMargin = UiKit.Dp(this, 8);
        gameRow.AddView(refresh, refreshParams);
        gameCard.AddView(gameRow);

        _warning = UiKit.Label(this, "", 13, UiKit.Warning);
        _warning.SetPadding(0, UiKit.Dp(this, 8), 0, 0);
        _warning.Visibility = ViewStates.Gone;
        gameCard.AddView(_warning);
        content.AddView(gameCard, CardParams());

        // --- como a imagem ocupa a tela
        var fitCard = UiKit.Card(this);
        fitCard.AddView(UiKit.Label(this, "Imagem na tela", 13, UiKit.TextMuted, bold: true));
        _fitChips = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var fitChipsParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        fitChipsParams.TopMargin = UiKit.Dp(this, 6);
        fitCard.AddView(_fitChips, fitChipsParams);
        _fitHint = UiKit.Label(this, "", 13, UiKit.TextMuted);
        _fitHint.SetPadding(0, UiKit.Dp(this, 8), 0, 0);
        fitCard.AddView(_fitHint);
        content.AddView(fitCard, CardParams());

        var scroll = new ScrollView(this);
        scroll.AddView(content);
        root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        // --- barra fixa embaixo com o botão Jogar
        var bottom = new LinearLayout(this) { Orientation = Orientation.Vertical };
        bottom.SetBackgroundColor(UiKit.Surface);
        bottom.SetPadding(pad, UiKit.Dp(this, 10), pad, UiKit.Dp(this, 10));
        _play = UiKit.MakeButton(this, "Jogar", BtnStyle.Primary);
        _play.TextSize = 18f;
        _play.Click += (_, _) => Play();
        bottom.AddView(_play, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, UiKit.Dp(this, 52)));
        root.AddView(bottom);

        SetContentView(root);
    }

    private LinearLayout.LayoutParams CardParams()
    {
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.TopMargin = UiKit.Dp(this, 12);
        return lp;
    }

    private LinearLayout.LayoutParams RowParams(int left)
    {
        var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        lp.LeftMargin = UiKit.Dp(this, left);
        return lp;
    }

    // ---------------------------------------------------------------- perfis

    private Profile CurrentProfile => _profiles[_current];

    /// <summary>Perfil novo: começa com a disposição padrão do jogo escolhido (ou uma básica).</summary>
    private Profile NewProfile()
    {
        var game = CurrentGame();
        var layout = game is { Layout.Count: > 0 }
            ? game.Layout.Select(c => c.Clone()).ToList()
            : ProfileStore.DefaultLayout();
        return new Profile { Name = $"Perfil {_profiles.Count + 1}", Layout = layout };
    }

    private void SaveProfiles() => ProfileStore.Save(_prefs, _profiles);

    private void LoadCurrentProfile()
    {
        _selected = -1;
        _preview.SetLayout(CurrentProfile.Layout);

        _suppressEvents = true;
        _name.Text = CurrentProfile.Name;
        _mouseSwitch.Checked = CurrentProfile.MouseEnabled;
        _suppressEvents = false;

        RebuildChips();
        RebuildPanel();
        UpdateWarning();
    }

    private void SelectProfile(int index)
    {
        _current = index;
        _prefs.Edit()!.PutInt("selProfile", index)!.Apply();
        LoadCurrentProfile();
    }

    private void RebuildChips()
    {
        _chips.RemoveAllViews();
        for (int i = 0; i < _profiles.Count; i++)
        {
            int index = i;
            var chip = UiKit.MakeButton(this, _profiles[i].Name, i == _current ? BtnStyle.ChipSelected : BtnStyle.Chip);
            chip.Click += (_, _) => SelectProfile(index);
            _chips.AddView(chip, ChipParams());
        }

        if (_profiles.Count < ProfileStore.MaxProfiles)
        {
            var add = UiKit.MakeButton(this, "+ Novo", BtnStyle.Chip);
            add.Click += (_, _) => CreateProfile();
            _chips.AddView(add, ChipParams());
        }

        _counter.Text = $"{_profiles.Count} de {ProfileStore.MaxProfiles} perfis";
        _delete.Enabled = _profiles.Count > 1;
        _delete.Alpha = _delete.Enabled ? 1f : 0.4f;
    }

    private LinearLayout.LayoutParams ChipParams()
    {
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        lp.RightMargin = UiKit.Dp(this, 8);
        return lp;
    }

    private void CreateProfile()
    {
        if (_profiles.Count >= ProfileStore.MaxProfiles)
        {
            Toast.MakeText(this, $"Limite de {ProfileStore.MaxProfiles} perfis.", ToastLength.Short)?.Show();
            return;
        }
        _profiles.Add(NewProfile());
        SaveProfiles();
        SelectProfile(_profiles.Count - 1);
    }

    private void OnNameChanged()
    {
        if (_suppressEvents) return;
        string name = _name.Text!.Trim();
        if (name.Length == 0) return; // enquanto o campo está vazio, mantém o nome anterior
        CurrentProfile.Name = name;
        SaveProfiles();
        RebuildChips();
    }

    private void ConfirmDeleteProfile()
    {
        if (_profiles.Count <= 1) return;
        new AlertDialog.Builder(this)!
            .SetTitle("Excluir perfil?")!
            .SetMessage($"O perfil \"{CurrentProfile.Name}\" e seus botões serão apagados.")!
            .SetPositiveButton("Excluir", (_, _) =>
            {
                _profiles.RemoveAt(_current);
                _current = Math.Min(_current, _profiles.Count - 1);
                SaveProfiles();
                SelectProfile(_current);
            })!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .Show();
    }

    private void ConfirmUseGameLayout()
    {
        var game = CurrentGame();
        if (game is null || game.Layout.Count == 0)
        {
            Toast.MakeText(this, "Este jogo não traz um layout padrão.", ToastLength.Short)?.Show();
            return;
        }
        new AlertDialog.Builder(this)!
            .SetTitle("Usar o layout do jogo?")!
            .SetMessage("Os botões deste perfil serão substituídos pelo layout padrão de " + (game.Name.Length > 0 ? game.Name : game.Id) + ".")!
            .SetPositiveButton("Substituir", (_, _) =>
            {
                CurrentProfile.Layout = game.Layout.Select(c => c.Clone()).ToList();
                SaveProfiles();
                LoadCurrentProfile();
            })!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .Show();
    }

    // ---------------------------------------------------------------- controles

    private void AddControl(bool joystick)
    {
        var layout = CurrentProfile.Layout;
        if (layout.Count >= ProfileStore.MaxControls)
        {
            Toast.MakeText(this, $"Limite de {ProfileStore.MaxControls} controles por perfil.", ToastLength.Short)?.Show();
            return;
        }

        layout.Add(joystick
            ? new ButtonLayout { Type = "dpad", Keys = new List<string> { "W", "A", "S", "D" }, X = 0.2, Y = 0.65, Size = 0.30 }
            : new ButtonLayout { Type = "button", Key = "Z", X = 0.5, Y = 0.5, Size = 0.12 });

        _selected = layout.Count - 1;
        _preview.Select(_selected);
        SaveAndRefresh();
        RebuildPanel();
    }

    private void DeleteSelected()
    {
        var layout = CurrentProfile.Layout;
        if (_selected < 0 || _selected >= layout.Count) return;
        layout.RemoveAt(_selected);
        _selected = -1;
        _preview.Select(-1);
        SaveAndRefresh();
        RebuildPanel();
    }

    /// <summary>Salva e redesenha a prévia, sem refazer o painel (para não tirar o foco de um campo de texto).</summary>
    private void SaveAndRefresh()
    {
        SaveProfiles();
        _preview.Refresh();
        UpdateWarning();
    }

    /// <summary>Painel do controle selecionado: tecla(s) que ele envia, texto, tamanho e excluir.</summary>
    private void RebuildPanel()
    {
        _panel.RemoveAllViews();
        var layout = CurrentProfile.Layout;

        if (_selected < 0 || _selected >= layout.Count)
        {
            _panel.AddView(UiKit.Label(this, "Nenhum controle selecionado.", 14, UiKit.TextMuted));
            return;
        }

        var c = layout[_selected];
        bool joystick = c.Type == "dpad";
        _panel.AddView(UiKit.Label(this, joystick ? "Joystick selecionado" : "Botão selecionado", 16, Color.White, bold: true));

        if (joystick)
        {
            c.Keys ??= new List<string>();
            string[] defaults = { "W", "A", "S", "D" };
            while (c.Keys.Count < 4) c.Keys.Add(defaults[c.Keys.Count]);

            string[] names = { "↑ Cima", "← Esquerda", "↓ Baixo", "→ Direita" };
            for (int i = 0; i < 4; i++)
            {
                int dir = i;
                _panel.AddView(KeyRow(names[dir], c.Keys[dir], key =>
                {
                    c.Keys[dir] = key;
                    AfterKeyChange();
                }));
            }
        }
        else
        {
            _panel.AddView(KeyRow("Envia a tecla", c.Key ?? "Z", key =>
            {
                string? old = c.Key;
                c.Key = key;
                // se o texto do botão era só o nome da tecla antiga, deixa o novo nome aparecer
                if (string.IsNullOrWhiteSpace(c.Label)
                    || string.Equals(c.Label, old, StringComparison.OrdinalIgnoreCase)
                    || c.Label == KeyCatalog.Display(old))
                    c.Label = null;
                AfterKeyChange();
            }));

            var text = UiKit.Input(this, "Texto no botão (opcional)", c.Label ?? "", InputTypes.ClassText);
            text.TextChanged += (_, _) =>
            {
                c.Label = string.IsNullOrWhiteSpace(text.Text) ? null : text.Text!.Trim();
                SaveAndRefresh();
            };
            var textParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            textParams.TopMargin = UiKit.Dp(this, 8);
            _panel.AddView(text, textParams);
        }

        var sizeLabel = UiKit.Label(this, "Tamanho", 13, UiKit.TextMuted, bold: true);
        sizeLabel.SetPadding(0, UiKit.Dp(this, 14), 0, 0);
        _panel.AddView(sizeLabel);

        const int minPercent = 6, maxPercent = 40;
        var seek = new SeekBar(this)
        {
            Max = maxPercent - minPercent,
            Progress = Math.Clamp((int)Math.Round(c.Size * 100) - minPercent, 0, maxPercent - minPercent)
        };
        seek.ProgressChanged += (_, e) =>
        {
            if (!e.FromUser) return;
            c.Size = (minPercent + e.Progress) / 100.0;
            SaveAndRefresh();
        };
        _panel.AddView(seek);

        var remove = UiKit.MakeButton(this, "Excluir este controle", BtnStyle.Danger);
        remove.Click += (_, _) => DeleteSelected();
        var removeParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        removeParams.TopMargin = UiKit.Dp(this, 12);
        _panel.AddView(remove, removeParams);
    }

    private void AfterKeyChange()
    {
        SaveAndRefresh();
        RebuildPanel(); // atualiza o texto dos botões de tecla
    }

    /// <summary>Linha "legenda ........ [tecla]"; tocar na tecla abre a janela de escolha.</summary>
    private View KeyRow(string caption, string key, Action<string> onPick)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(0, UiKit.Dp(this, 8), 0, 0);

        row.AddView(UiKit.Label(this, caption, 15, Color.White),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var allowed = UsableKeys();
        bool notAllowed = allowed != null && !allowed.Contains(key);
        var button = UiKit.MakeButton(this, KeyCatalog.Display(key), BtnStyle.Secondary);
        button.SetMinimumWidth(UiKit.Dp(this, 96));
        if (notAllowed) button.SetTextColor(UiKit.Warning);
        button.Click += (_, _) => KeyPicker.Show(this, caption, key, UsableKeys(), onPick);
        row.AddView(button);
        return row;
    }

    // ---------------------------------------------------------------- jogo

    private GameInfo? CurrentGame()
    {
        int i = _gameSpinner?.SelectedItemPosition ?? -1;
        return i >= 0 && i < _games.Count ? _games[i] : null;
    }

    /// <summary>Teclas que o servidor aceita para o jogo escolhido (null = nenhum jogo escolhido).</summary>
    private HashSet<string>? UsableKeys() => CurrentGame()?.UsableKeys();

    private void FillGameSpinner(string? preferredId)
    {
        var names = _games.Select(g => g.Name.Length > 0 ? g.Name : g.Id).ToList();
        _gameSpinner.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, names);

        int index = preferredId is null ? 0 : _games.FindIndex(g => g.Id == preferredId);
        if (index >= 0 && index < _games.Count) _gameSpinner.SetSelection(index);

        _play.Enabled = _games.Count > 0;
        _play.Alpha = _play.Enabled ? 1f : 0.4f;
    }

    private void OnGameSelected(int position)
    {
        if (position >= 0 && position < _games.Count)
            _prefs.Edit()!.PutString("selGame", _games[position].Id)!.Apply();
        if (_profiles.Count > 0)
        {
            RebuildPanel(); // as teclas fora da lista do jogo mudam de cor
            UpdateWarning();
        }
    }

    private async Task RefreshGamesAsync(Button button)
    {
        button.Enabled = false;
        try
        {
            string? keep = CurrentGame()?.Id;
            _games = await ServerApi.GetGamesAsync(_host, _port, _pin);
            FillGameSpinner(keep);
            Toast.MakeText(this, "Lista de jogos atualizada.", ToastLength.Short)?.Show();
        }
        catch (Exception)
        {
            Toast.MakeText(this, "Não consegui atualizar. O servidor está ligado?", ToastLength.Long)?.Show();
        }
        finally
        {
            button.Enabled = true;
        }
    }

    /// <summary>
    /// Avisa quais teclas do perfil o servidor não aceita (ele ignora essas teclas) e se o perfil tem Ctrl e Esc, que o
    /// servidor não deixa funcionar juntos.
    /// </summary>
    private void UpdateWarning()
    {
        var game = CurrentGame();
        if (game is null || _profiles.Count == 0)
        {
            _warning.Visibility = ViewStates.Gone;
            return;
        }

        var lines = new List<string>();

        var usable = game.UsableKeys();
        var blocked = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var k in KeyCatalog.KeysOf(CurrentProfile.Layout))
            if (!usable.Contains(k)) blocked.Add(KeyCatalog.Display(k));

        if (blocked.Count > 0)
        {
            lines.Add("⚠ O servidor não aceita estas teclas" + (game.HasSafeKeys ? "" : " neste jogo") + ": " +
                      string.Join(", ", blocked) + ". Os botões que as usam não vão funcionar.");
        }

        if (KeyCatalog.HasBlockedCombo(CurrentProfile.Layout))
        {
            lines.Add("⚠ Ctrl e Esc não funcionam juntos: o servidor bloqueia essa combinação (ela abriria o menu Iniciar do Windows).");
        }

        _warning.Text = string.Join("\n", lines);
        _warning.Visibility = lines.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
    }

    private void RebuildFitChips()
    {
        _fitChips.RemoveAllViews();
        foreach (var (mode, title, hint) in FitModes)
        {
            bool selected = mode == _fit;
            string chosen = mode;
            var chip = UiKit.MakeButton(this, title, selected ? BtnStyle.ChipSelected : BtnStyle.Chip);
            chip.Click += (_, _) =>
            {
                _fit = chosen;
                _prefs.Edit()!.PutString("fit", chosen)!.Apply();
                RebuildFitChips();
            };
            _fitChips.AddView(chip, ChipParams());
            if (selected) _fitHint.Text = hint;
        }
    }

    private void Play()
    {
        var game = CurrentGame();
        if (game is null)
        {
            Toast.MakeText(this, "Nenhum jogo disponível no servidor.", ToastLength.Short)?.Show();
            return;
        }

        var intent = new Intent(this, typeof(StreamActivity));
        intent.PutExtra("host", _host);
        intent.PutExtra("port", _port);
        intent.PutExtra("pin", _pin);
        intent.PutExtra("game", game.Id);
        intent.PutExtra("fit", _fit);
        intent.PutExtra("mouse", CurrentProfile.MouseEnabled);
        intent.PutExtra("layout", JsonSerializer.Serialize(CurrentProfile.Layout, ClientJson.Default.ListButtonLayout));
        StartActivity(intent);
    }
}
