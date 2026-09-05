using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.UI;

/// <summary>
/// Player HUD: health bar, unlocked-ability icons, transient checkpoint prompt.
/// Pure consumer of LostCrownlike.Core.EventBus — never references PlayerController
/// or any other subsystem's types (see ARCHITECTURE.md "### UI").
/// </summary>
public partial class Hud : Control
{
    [Export] public int MaxHealth = 100;

    private int _currentHealth;

    private ProgressBar _healthBar;
    private Label _healthLabel;
    private Control _abilityDoubleJump;
    private Control _abilityDash;
    private Control _abilityWallJump;
    private Control _abilityChargeAttack;
    private Label _checkpointPrompt;
    private Timer _checkpointTimer;
    private Label _runCompleteBanner;
    private ProgressBar _bossBar;
    private Label _bossLabel;

    public override void _Ready()
    {
        _healthBar = GetNode<ProgressBar>("%HealthBar");
        _healthLabel = GetNode<Label>("%HealthLabel");
        _abilityDoubleJump = GetNode<Control>("%AbilityDoubleJump");
        _abilityDash = GetNode<Control>("%AbilityDash");
        _abilityWallJump = GetNode<Control>("%AbilityWallJump");
        _abilityChargeAttack = GetNode<Control>("%AbilityChargeAttack");
        _checkpointPrompt = GetNode<Label>("%CheckpointPrompt");
        _checkpointTimer = GetNode<Timer>("%CheckpointTimer");

        _currentHealth = MaxHealth;
        _healthBar.MaxValue = MaxHealth;
        _healthBar.Value = _currentHealth;
        UpdateHealthLabel();

        _abilityDoubleJump.Visible = false;
        _abilityDash.Visible = false;
        _abilityWallJump.Visible = false;
        _abilityChargeAttack.Visible = false;
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

        if (EventBus.Instance != null)
        {
            EventBus.Instance.PlayerDamaged += OnPlayerDamaged;
            EventBus.Instance.PlayerHealthChanged += OnPlayerHealthChanged;
            EventBus.Instance.PlayerDied += OnPlayerDied;
            EventBus.Instance.AbilityUnlocked += OnAbilityUnlocked;
            EventBus.Instance.CheckpointReached += OnCheckpointReached;
            EventBus.Instance.RunCompleted += OnRunCompleted;
            EventBus.Instance.BossStateChanged += OnBossStateChanged;
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
            EventBus.Instance.AbilityUnlocked -= OnAbilityUnlocked;
            EventBus.Instance.CheckpointReached -= OnCheckpointReached;
            EventBus.Instance.BossStateChanged -= OnBossStateChanged;
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

    private void OnAbilityUnlocked(int abilityBits)
    {
        var ability = (AbilityFlags)abilityBits;
        if (ability.HasFlag(AbilityFlags.DoubleJump)) _abilityDoubleJump.Visible = true;
        if (ability.HasFlag(AbilityFlags.Dash)) _abilityDash.Visible = true;
        if (ability.HasFlag(AbilityFlags.WallJump)) _abilityWallJump.Visible = true;
        if (ability.HasFlag(AbilityFlags.ChargeAttack)) _abilityChargeAttack.Visible = true;
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

    private void UpdateHealthLabel()
    {
        _healthLabel.Text = $"{_currentHealth} / {MaxHealth}";
    }
}
