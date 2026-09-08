using Godot;
using LostCrownlike.Core;
using LostCrownlike.Save;

namespace LostCrownlike.UI;

/// <summary>
/// Modal dialog for meta-progression upgrades at the Camp's Runestone Shrine.
/// Spends persistent Embers collected across runs to unlock permanent WorldFlags perks.
///
/// WCAG AA compliant: high contrast text, minimum 44px touch/click targets,
/// keyboard (ESC / E) and controller navigability.
/// </summary>
public partial class RuneForgeModal : Control
{
    private Label _embersLabel;
    private Button _vigorButton;
    private Button _parryButton;
    private Button _healButton;
    private Button _closeButton;

    public const string FlagVigor = "rune_vigor";
    public const string FlagParryWindow = "rune_parry_window";
    public const string FlagParryHeal = "rune_parry_heal";

    public const int CostVigor = 30;
    public const int CostParryWindow = 60;
    public const int CostParryHeal = 90;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        // 1. Fullscreen dark backdrop
        var backdrop = new ColorRect
        {
            Name = "Backdrop",
            Color = new Color(0.02f, 0.03f, 0.05f, 0.88f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        // 2. Centered panel container
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(620f, 440f),
        };
        var boxStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.14f, 0.98f),
            BorderWidthBottom = 2,
            BorderWidthTop = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderColor = new Color(0.90f, 0.72f, 0.25f), // Warm gold border
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 20,
            ContentMarginBottom = 20,
        };
        panel.AddThemeStyleboxOverride("panel", boxStyle);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        // Header
        var title = new Label
        {
            Text = "✦ FORGIA DELLE RUNE — ACCAMPAMENTO ✦",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", new Color(1.0f, 0.88f, 0.45f));
        title.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(title);

        _embersLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _embersLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.65f, 0.20f));
        _embersLabel.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(_embersLabel);

        vbox.AddChild(new HSeparator());

        // Perk 1: Obsidian Vigor
        vbox.AddChild(BuildPerkRow("Vigore dell'Ossidiana",
            "+20 Salute Massima (120 HP base all'avvio)",
            CostVigor, out _vigorButton, () => PurchasePerk(FlagVigor, CostVigor)));

        // Perk 2: Silver Deflect
        vbox.AddChild(BuildPerkRow("Riflesso d'Argento",
            "+30ms alla finestra di Parry Perfetto",
            CostParryWindow, out _parryButton, () => PurchasePerk(FlagParryWindow, CostParryWindow)));

        // Perk 3: Vital Ember
        vbox.AddChild(BuildPerkRow("Scintilla Vitale",
            "La prima parata perfetta in ogni stanza cura 6 HP",
            CostParryHeal, out _healButton, () => PurchasePerk(FlagParryHeal, CostParryHeal)));

        vbox.AddChild(new HSeparator());

        // Footer: Close button
        var footerBox = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        _closeButton = new Button
        {
            Text = "CHIUDI FORGIA [ESC / E]",
            CustomMinimumSize = new Vector2(220f, 44f),
        };
        _closeButton.Pressed += Close;
        footerBox.AddChild(_closeButton);
        vbox.AddChild(footerBox);
    }

    private Control BuildPerkRow(string name, string desc, int cost, out Button button, System.Action onBuy)
    {
        var row = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0f, 54f),
        };
        row.AddThemeConstantOverride("separation", 16);

        var textVBox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var nameLabel = new Label
        {
            Text = name,
        };
        nameLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.98f));
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        textVBox.AddChild(nameLabel);

        var descLabel = new Label
        {
            Text = desc,
        };
        descLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.72f, 0.78f));
        descLabel.AddThemeFontSizeOverride("font_size", 12);
        textVBox.AddChild(descLabel);

        row.AddChild(textVBox);

        button = new Button
        {
            CustomMinimumSize = new Vector2(160f, 44f),
            Text = $"FORGIA ({cost} 🔥)",
        };
        button.Pressed += onBuy;
        row.AddChild(button);

        return row;
    }

    public void Open()
    {
        if (Visible) return;
        Visible = true;
        RefreshUI();
        _closeButton?.GrabFocus();
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        EventBus.Instance?.EmitRuneForgeRequested(false);
    }

    public void RefreshUI()
    {
        var save = SaveManager.Instance?.Current;
        int totalEmbers = save?.TotalEmbers ?? 0;
        if (_embersLabel != null)
            _embersLabel.Text = $"Braci Disponibili: {totalEmbers} 🔥";

        UpdatePerkButton(_vigorButton, FlagVigor, CostVigor, totalEmbers);
        UpdatePerkButton(_parryButton, FlagParryWindow, CostParryWindow, totalEmbers);
        UpdatePerkButton(_healButton, FlagParryHeal, CostParryHeal, totalEmbers);
    }

    private void UpdatePerkButton(Button btn, string flag, int cost, int available)
    {
        if (btn == null) return;
        bool owned = SaveManager.Instance?.GetWorldFlag(flag) ?? false;
        if (owned)
        {
            btn.Text = "✓ FORGIATO";
            btn.Disabled = true;
            btn.AddThemeColorOverride("font_disabled_color", new Color(0.35f, 0.85f, 0.45f));
        }
        else
        {
            btn.Text = $"FORGIA ({cost} 🔥)";
            btn.Disabled = available < cost;
            btn.RemoveThemeColorOverride("font_disabled_color");
        }
    }

    private void PurchasePerk(string flag, int cost)
    {
        if (SaveManager.Instance == null) return;
        if (SaveManager.Instance.TrySpendEmbers(cost))
        {
            SaveManager.Instance.SetWorldFlag(flag, true);
            EventBus.Instance?.EmitRuneForged(flag);
            RefreshUI();
            GD.Print($"[RuneForge] Unlocked perk '{flag}' for {cost} embers.");
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;

        if (@event.IsActionPressed("pause")
            || @event.IsActionPressed("ui_cancel")
            || (@event is InputEventJoypadButton joy && joy.Pressed && joy.ButtonIndex == JoyButton.B)
            || (@event is InputEventKey key && key.Pressed && (key.Keycode == Key.E || key.Keycode == Key.Escape)))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
