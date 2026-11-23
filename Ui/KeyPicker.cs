using Android.App;
using Android.Widget;
using GameStream.Client.Models;

namespace GameStream.Client.Ui;

/// <summary>Janela para escolher a tecla de um botão: números, letras, Ctrl, Shift e Esc.</summary>
public static class KeyPicker
{
    /// <param name="allowed">Teclas que o jogo escolhido aceita; as outras aparecem apagadas. null = sem aviso.</param>
    public static void Show(Activity activity, string title, string? current, HashSet<string>? allowed, Action<string> onPicked)
    {
        AlertDialog? dialog = null;
        int pad = UiKit.Dp(activity, 16);
        int gap = UiKit.Dp(activity, 3);

        var content = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        content.SetPadding(pad, UiKit.Dp(activity, 4), pad, pad);

        if (allowed != null)
        {
            var hint = UiKit.Label(activity, "Teclas apagadas não são aceitas pelo jogo escolhido no servidor.", 12, UiKit.TextMuted);
            hint.SetPadding(0, 0, 0, UiKit.Dp(activity, 6));
            content.AddView(hint);
        }

        void Section(string heading, string[] keys, int perRow)
        {
            var h = UiKit.Label(activity, heading, 13, UiKit.TextMuted, bold: true);
            h.SetPadding(0, UiKit.Dp(activity, 8), 0, UiKit.Dp(activity, 4));
            content.AddView(h);

            for (int start = 0; start < keys.Length; start += perRow)
            {
                var row = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
                for (int j = 0; j < perRow; j++)
                {
                    var lp = new LinearLayout.LayoutParams(0, UiKit.Dp(activity, 44), 1f);
                    lp.SetMargins(gap, gap, gap, gap);

                    int idx = start + j;
                    if (idx >= keys.Length)
                    {
                        row.AddView(new Android.Views.View(activity), lp); // espaço vazio para alinhar a última linha
                        continue;
                    }

                    string key = keys[idx];
                    bool isCurrent = string.Equals(key, current, StringComparison.OrdinalIgnoreCase);
                    bool isAllowed = allowed == null || allowed.Contains(key);

                    var btn = UiKit.MakeButton(activity, KeyCatalog.Display(key), isCurrent ? BtnStyle.ChipSelected : BtnStyle.Secondary);
                    btn.SetPadding(0, 0, 0, 0);
                    if (!isAllowed) btn.SetTextColor(UiKit.TextMuted);
                    btn.Click += (_, _) =>
                    {
                        dialog?.Dismiss();
                        onPicked(key);
                    };
                    row.AddView(btn, lp);
                }
                content.AddView(row);
            }
        }

        Section("Números", KeyCatalog.Numbers, 5);
        Section("Letras", KeyCatalog.Letters, 7);
        Section("Especiais", KeyCatalog.Special, 3);

        var scroll = new ScrollView(activity);
        scroll.AddView(content);

        dialog = new AlertDialog.Builder(activity)!
            .SetTitle(title)!
            .SetView(scroll)!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .Create();
        dialog!.Show();
    }
}
