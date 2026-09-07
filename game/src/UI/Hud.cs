using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.UI;

/// <summary>
/// Player HUD: health bar, boss bar, transient checkpoint prompt.
///
/// The ability icons are gone with the crystals. Four slots that light up one
/// by one say "you are collecting things"; four slots lit from the first frame
/// say nothing at all, and cost screen.
/// Pure consumer of LostCrownlike.Core.EventBus — never references PlayerController
/// or any other subsystem's types (see ARCHITECTURE.md "### UI").
/// </summary>
public partial class Hud : Control
{
    [Export] public int MaxHealth = 100;

    private int _currentHealth;

    private ProgressBar _healthBar;
    private Label _healthLabel;
    private Label _checkpointPrompt;
    private Timer _checkpointTimer;
    private Label _runCompleteBanner;
    private ProgressBar _bossBar;
    private Label _bossLabel;
    private Label _roomLabel;
    private ColorRect _parryFlash;

    public override void _Ready()
    {
        _healthBar = GetNode<ProgressBar>("%HealthBar");
        _healthLabel = GetNode<Label>("%HealthLabel");
        _checkpointPrompt = GetNode<Label>("%CheckpointPrompt");
        _checkpointTimer = GetNode<Timer>("%CheckpointTimer");

        _currentHealth = MaxHealth;
        _healthBar.MaxValue = MaxHealth;
        _healthBar.Value = _currentHealth;
        UpdateHealthLabel();
        _checkpointPrompt.Visible = false;

        _checkpointTimer.OneShot = true;
        _checkpointTimer.Timeout += OnCheckpointTimerTimeout;

        // Built here rather than in Hud.tscn. The HUD reaches its nodes by
        // unique name, so a scene-authored banner would be one more binding to
        // keep in sync with a hand-edited .tscn; the round-1 camera bug in this
        // project was exactly that class of failure. Nothing else needs to
        // address this node.
        _runCompleteBanner = new Label
        {
            Name = "RunCompleteBanner",
            Text = "RUN COMPLETE",
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _runCompleteBanner.SetAnchorsPreset(LayoutPreset.FullRect);
        _runCompleteBanner.AddThemeFontSizeOverride("font_size", 48);
        // The banner lands wherever the player finished, which is a lit wall
        // as often as a dark one, so white-on-anything needs an outline to stay
        // legible rather than a colour chosen for one screenshot.
        _runCompleteBanner.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _runCompleteBanner.AddThemeConstantOverride("outline_size", 10);
        AddChild(_runCompleteBanner);

        BuildBossBar();
        BuildRoomLabel();
        BuildParryFlash();

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PlayerDamaged += OnPlayerDamaged;
            EventBus.Instance.PlayerHealthChanged += OnPlayerHealthChanged;
            EventBus.Instance.PlayerDied += OnPlayerDied;
            EventBus.Instance.CheckpointReached += OnCheckpointReached;
            EventBus.Instance.RunCompleted += OnRunCompleted;
            EventBus.Instance.BossStateChanged += OnBossStateChanged;
            EventBus.Instance.RoomEntered += OnRoomEntered;
            EventBus.Instance.Parried += OnParried;
            EventBus.Instance.LevelTransitionRequested += (_, _) =>
            {
                if (_runCompleteBanner != null) _runCompleteBanner.Visible = false;
            };
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.PlayerDamaged -= OnPlayerDamaged;
            EventBus.Instance.PlayerHealthChanged -= OnPlayerHealthChanged;
            EventBus.Instance.PlayerDied -= OnPlayerDied;
            EventBus.Instance.CheckpointReached -= OnCheckpointReached;
            EventBus.Instance.BossStateChanged -= OnBossStateChanged;
            EventBus.Instance.RoomEntered -= OnRoomEntered;
            EventBus.Instance.Parried -= OnParried;
        }

        if (_checkpointTimer != null)
        {
            _checkpointTimer.Timeout -= OnCheckpointTimerTimeout;
        }
    }

    private void OnPlayerDamaged(int amount, Vector3 sourcePosition, Vector3 knockback, bool isCritical)
    {
        // Intentionally does not change the bar: health is rendered from
        // PlayerHealthChanged, the authoritative broadcast. Kept as the hook
        // for damage-only feedback (flash, shake) that must not fire on heals.
    }

    private void OnPlayerHealthChanged(int current, int max)
    {
        MaxHealth = max;
        _currentHealth = Mathf.Clamp(current, 0, max);
        _healthBar.MaxValue = max;
        _healthBar.Value = _currentHealth;
        UpdateHealthLabel();
    }

    private void OnPlayerDied()
    {
        // Deliberately does NOT write the bar. PlayerHealthChanged is the only
        // writer; this handler used to zero it and, because respawn happens
        // inside the PlayerDied emission (SaveManager subscribes first, as an
        // autoload, and its handler calls Respawn which broadcasts 100/100),
        // this ran LAST and stamped the bar back to 0 — leaving a fully healed
        // player showing 0/100 for the rest of the run.
    }

    
    private void OnCheckpointReached(string checkpointId)
    {
        _checkpointPrompt.Text = $"Checkpoint Reached: {checkpointId}";
        _checkpointPrompt.Visible = true;
        _checkpointTimer.Start();
    }

    private void OnRunCompleted(int roomsCleared)
    {
        if (_runCompleteBanner == null) return;
        // The banner has to say what to DO. Showing only "RUN COMPLETE" left the
        // player standing in a finished room with no visible way forward.
        _runCompleteBanner.Text = "RUN COMPLETE — " + roomsCleared + " rooms" + System.Environment.NewLine + "press JUMP for a new run";
        _runCompleteBanner.Visible = true;
    }

    private void OnCheckpointTimerTimeout()
    {
        _checkpointPrompt.Visible = false;
    }

    /// Built in code for the same reason as the banner: the HUD addresses its
    /// scene nodes by unique name, and one more hand-edited .tscn binding is
    /// one more thing that can silently stop resolving.
    private void BuildBossBar()
    {
        _bossLabel = new Label
        {
            Name = "BossLabel",
            Text = "THE WARDEN",
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _bossLabel.SetAnchorsPreset(LayoutPreset.CenterTop);
        _bossLabel.AnchorLeft = 0.25f;
        _bossLabel.AnchorRight = 0.75f;
        _bossLabel.OffsetTop = 18f;
        _bossLabel.OffsetBottom = 42f;
        _bossLabel.OffsetLeft = 0f;
        _bossLabel.OffsetRight = 0f;
        _bossLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _bossLabel.AddThemeConstantOverride("outline_size", 6);
        AddChild(_bossLabel);

        _bossBar = new ProgressBar
        {
            Name = "BossBar",
            Visible = false,
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            ShowPercentage = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _bossBar.SetAnchorsPreset(LayoutPreset.CenterTop);
        _bossBar.AnchorLeft = 0.25f;
        _bossBar.AnchorRight = 0.75f;
        _bossBar.OffsetTop = 44f;
        _bossBar.OffsetBottom = 58f;
        _bossBar.OffsetLeft = 0f;
        _bossBar.OffsetRight = 0f;
        AddChild(_bossBar);
    }

    /// The armour is the fight's whole rule, so the bar says which state the
    /// boss is in rather than only how much is left: grey while armoured, lit
    /// while the parry window is open. Without that the player sees a bar that
    /// barely moves and concludes the game is broken, which is the correct
    /// conclusion from the information on screen.
    private void OnBossStateChanged(bool alive, float healthFraction, bool armoured, bool enraged)
    {
        if (_bossBar == null) return;

        _bossBar.Visible = alive;
        _bossLabel.Visible = alive;
        if (!alive) return;

        _bossBar.Value = healthFraction * 100f;
        _bossLabel.Text = enraged ? "THE WARDEN  —  ENRAGED" : "THE WARDEN";

        var fill = new StyleBoxFlat
        {
            BgColor = armoured
                ? new Color(0.42f, 0.40f, 0.38f)     // absorbing: the bar will barely move
                : new Color(0.95f, 0.78f, 0.30f),    // open: spend the window
        };
        _bossBar.AddThemeStyleboxOverride("fill", fill);
    }

    /// Where you are in the run. Worth the pixels only since the run became
    /// ten rooms of 200 metres: at two minutes you keep count yourself.
    private void BuildRoomLabel()
    {
        _roomLabel = new Label
        {
            Name = "RoomLabel",
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _roomLabel.SetAnchorsPreset(LayoutPreset.TopRight);
        _roomLabel.AnchorLeft = 1f;
        _roomLabel.AnchorRight = 1f;
        _roomLabel.OffsetLeft = -170f;
        _roomLabel.OffsetRight = -18f;
        _roomLabel.OffsetTop = 16f;
        _roomLabel.OffsetBottom = 38f;
        _roomLabel.AddThemeColorOverride("font_color", new Color(0.86f, 0.80f, 0.68f));
        _roomLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _roomLabel.AddThemeConstantOverride("outline_size", 6);
        AddChild(_roomLabel);
    }

    private void OnRoomEntered(int index, int total)
    {
        if (_roomLabel == null) return;
        // The last room is named rather than numbered: "10 of 10" and "the
        // Warden" are the same fact, and only one of them tells you to be
        // ready for it.
        _roomLabel.Text = index >= total - 1 ? "THE WARDEN" : $"Room {index + 1} of {total}";
    }

    private void BuildParryFlash()
    {
        _parryFlash = new ColorRect
        {
            Name = "ParryFlash",
            Color = new Color(1f, 0.9f, 0.45f, 0f),
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _parryFlash.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_parryFlash);
    }

    private void OnParried(bool perfect, Vector3 atPosition)
    {
        if (!perfect || _parryFlash == null || !IsInsideTree()) return;
        _parryFlash.Visible = true;
        _parryFlash.Color = new Color(1f, 0.92f, 0.5f, 0.26f);
        var tween = CreateTween();
        if (tween == null) return;
        tween.TweenProperty(_parryFlash, "color:a", 0f, 0.16f)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() =>
        {
            if (_parryFlash != null && IsInstanceValid(_parryFlash))
                _parryFlash.Visible = false;
        }));
    }

    private void UpdateHealthLabel()
    {
        _healthLabel.Text = $"{_currentHealth} / {MaxHealth}";
    }
}
