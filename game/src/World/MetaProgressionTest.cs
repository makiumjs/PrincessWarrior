using Godot;
using LostCrownlike.Combat;
using LostCrownlike.Core;
using LostCrownlike.PlayerCamera;
using LostCrownlike.Save;

namespace LostCrownlike.World;

/// <summary>
/// TEST-SCENE ONLY: End-to-end verification of Roguevania Meta-Progression (Fase 4).
/// Tests:
/// 1. Persistent currency (TotalEmbers) accumulation & spend logic.
/// 2. Perk 'rune_vigor': boosts MaxHealth to 120 and reflects immediately on HUD.
/// 3. Perk 'rune_parry_window': widens EffectivePerfectParryMs from 220ms to 250ms (+30ms).
/// 4. Perk 'rune_parry_heal': heals 6 HP on first perfect parry of each room.
/// 5. RunestoneShrine: correctly placed in Room 0 (The Camp) with interaction area.
/// 6. Cross-run persistence: ResetSave preserves WorldFlags, TotalEmbers and lifetime stats.
/// </summary>
public partial class MetaProgressionTest : Node
{
    private int _f;
    private PlayerController _player;
    private CombatController _combat;
    private DungeonRoomBuilder _builder;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= FindFirst<PlayerController>(GetTree().Root);
        _combat ??= FindFirst<CombatController>(GetTree().Root);
        _builder ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);

        if (_player == null || _combat == null || _builder == null)
        {
            if (_f > 180)
            {
                GD.Print($"[META] RESULT: FAIL (resolution failed: player={_player != null}, combat={_combat != null}, builder={_builder != null})");
                GetTree().Quit();
            }
            return;
        }

        if (_f == 10)
        {
            // 1. Clean slate setup
            SaveManager.Instance?.ResetSave();
            var current = SaveManager.Instance?.Current;
            if (current == null)
            {
                GD.Print("[META] RESULT: FAIL (current save is null)");
                GetTree().Quit();
                return;
            }
            current.TotalEmbers = 0;
            current.WorldFlags.Clear();

            // 2. Test Ember Currency accumulation & spending
            SaveManager.Instance.AddEmbers(100);
            if (current.TotalEmbers != 100)
            {
                GD.Print($"[META] RESULT: FAIL (expected 100 embers, got {current.TotalEmbers})");
                GetTree().Quit();
                return;
            }

            bool overspend = SaveManager.Instance.TrySpendEmbers(150);
            if (overspend || current.TotalEmbers != 100)
            {
                GD.Print("[META] RESULT: FAIL (overspending succeeded or corrupted balance)");
                GetTree().Quit();
                return;
            }

            bool validSpend = SaveManager.Instance.TrySpendEmbers(40);
            if (!validSpend || current.TotalEmbers != 60)
            {
                GD.Print($"[META] RESULT: FAIL (expected 60 embers after spending 40, got {current.TotalEmbers})");
                GetTree().Quit();
                return;
            }
            GD.Print("[META] 1. Embers currency accumulation and spending: PASS");
        }

        if (_f == 20)
        {
            // 3. Test Perk: rune_vigor (MaxHealth 100 -> 120)
            if (_player.MaxHealth != 100)
            {
                GD.Print($"[META] RESULT: FAIL (default MaxHealth should be 100, was {_player.MaxHealth})");
                GetTree().Quit();
                return;
            }

            SaveManager.Instance.SetWorldFlag("rune_vigor", true);
            // Trigger update via EmbersChanged
            SaveManager.Instance.AddEmbers(1);
            if (_player.MaxHealth != 120)
            {
                GD.Print($"[META] RESULT: FAIL (rune_vigor should boost MaxHealth to 120, got {_player.MaxHealth})");
                GetTree().Quit();
                return;
            }
            GD.Print("[META] 2. Perk 'rune_vigor' (+20 MaxHealth): PASS");
        }

        if (_f == 30)
        {
            // 4. Test Perk: rune_parry_window (+30ms to EffectivePerfectParryMs)
            float baseWindow = _combat.EffectivePerfectParryMs;
            if (Mathf.Abs(baseWindow - 220f) > 1f)
            {
                GD.Print($"[META] RESULT: FAIL (expected base window ~220ms, got {baseWindow}ms)");
                GetTree().Quit();
                return;
            }

            SaveManager.Instance.SetWorldFlag("rune_parry_window", true);
            float upgradedWindow = _combat.EffectivePerfectParryMs;
            if (Mathf.Abs(upgradedWindow - 250f) > 1f)
            {
                GD.Print($"[META] RESULT: FAIL (expected upgraded window ~250ms, got {upgradedWindow}ms)");
                GetTree().Quit();
                return;
            }
            GD.Print("[META] 3. Perk 'rune_parry_window' (+30ms parry window): PASS");
        }

        if (_f == 40)
        {
            // 5. Test Perk: rune_parry_heal (+6 HP on first perfect parry of room)
            SaveManager.Instance.SetWorldFlag("rune_parry_heal", true);

            // Damage player via ApplyBurn to bypass hurt invulnerability frames
            _player.ApplyBurn(30);

            int damagedHp = _player.CurrentHealth; // e.g. 120 - 30 = 90
            _combat.StartParry();

            // Simulate attack landing while in perfect parry window
            _player.TakeDamage(new DamageInfo
            {
                Amount = 15,
                SourcePosition = _player.GlobalPosition + Vector3.Right,
                Knockback = Vector3.Zero,
            });

            int healedHp = _player.CurrentHealth;
            if (healedHp != damagedHp + 6)
            {
                GD.Print($"[META] RESULT: FAIL (expected HP {damagedHp + 6} after parry heal, got {healedHp})");
                GetTree().Quit();
                return;
            }

            // Second parry in same room must NOT heal again
            _combat.StartParry();
            _player.TakeDamage(new DamageInfo
            {
                Amount = 15,
                SourcePosition = _player.GlobalPosition + Vector3.Right,
                Knockback = Vector3.Zero,
            });

            if (_player.CurrentHealth != healedHp)
            {
                GD.Print($"[META] RESULT: FAIL (parry heal triggered more than once in same room)");
                GetTree().Quit();
                return;
            }
            GD.Print("[META] 4. Perk 'rune_parry_heal' (single room heal): PASS");
        }

        if (_f == 50)
        {
            // 6. Test Runestone Shrine in Room 0
            var shrine = _builder.FindChild("RunestoneShrine", true, false) as RunestoneShrine;
            if (shrine == null)
            {
                GD.Print("[META] RESULT: FAIL (RunestoneShrine not found in Room 0)");
                GetTree().Quit();
                return;
            }
            var area = shrine.FindChild("InteractArea", true, false) as Area3D;
            if (area == null)
            {
                GD.Print("[META] RESULT: FAIL (RunestoneShrine InteractArea missing)");
                GetTree().Quit();
                return;
            }
            GD.Print("[META] 5. Runestone Shrine in Room 0: PASS");
        }

        if (_f == 60)
        {
            // 7. Test Cross-Run Persistence
            int embersBefore = SaveManager.Instance.Current.TotalEmbers;
            SaveManager.Instance.ResetSave();
            var resetSave = SaveManager.Instance.Current;

            bool flagsRetained = resetSave.WorldFlags.ContainsKey("rune_vigor")
                                 && resetSave.WorldFlags.ContainsKey("rune_parry_window")
                                 && resetSave.WorldFlags.ContainsKey("rune_parry_heal");
            bool embersRetained = resetSave.TotalEmbers == embersBefore;
            bool runsIncremented = resetSave.TotalRunsAttempted >= 1;

            if (!flagsRetained || !embersRetained || !runsIncremented)
            {
                GD.Print($"[META] RESULT: FAIL (ResetSave failed to preserve meta-progression: flags={flagsRetained}, embers={embersRetained}, runs={runsIncremented})");
                GetTree().Quit();
                return;
            }
            GD.Print("[META] 6. Cross-Run Meta-Progression Persistence: PASS");

            GD.Print("[META] RESULT: PASS (all roguevania hub and meta-progression systems operational)");
            GetTree().Quit();
        }
    }

    private static T FindFirst<T>(Node root) where T : class
    {
        if (root is T match) return match;
        foreach (var child in root.GetChildren())
        {
            var found = FindFirst<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
