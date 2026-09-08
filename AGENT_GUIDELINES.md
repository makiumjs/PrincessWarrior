# PrincessWarrior - Agent & Project Guidelines

## Project Overview
2.5D Action-Roguevania built with Godot 4.7.2 Mono (.NET 8 / C#).
Combat telegraph system (White/Gold/Red), 4 native baseline abilities, metric chunk generation, macro-branching biomes, and persistent shortcuts.

## Build & Test Commands
- **Build C#**: `dotnet build game/LostCrownlike.csproj`
- **Run Verification Gate**: `bash tools/verify.sh`
- **Run Game**: `tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --path game`
- **Headless Test**: `tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path game --scene res://scenes/tests/<TestScene>.tscn --quit-after 10`

## Architecture & Code Rules
1. **Zero Placeholders**: Never write `// TODO` or leave incomplete stubs. Every implementation must be production-ready.
2. **Spatial Metric Contract**:
   - Player single jump: 3.2m | Double jump: 5.6m | Dash: 6.4m
   - Platform spacing: Minimum 1.6m vertical clearance between overlapping platforms.
   - Walkways: Every elevated path must have grounding pillars/supports.
3. **Combat Telegraphs**:
   - `StandardWhite` (0): Normal parryable attack.
   - `CounterGold` (1): Counterable attack, awards 1.4x knockback on parry.
   - `UnparryableRed` (2): Shield-breaker, ignores parry, deals full damage. Must be dodged.
4. **Active Design Specs**:
   - Biome Fork & Crucible: `docs/decisions/the_crucible_design.md`
   - Visual Assets: `docs/assets/` and `game/assets/ui/`
5. **Clean Builds**: Zero compiler warnings, zero errors, zero headless engine warnings/errors.
6. **Always consult**: `PROMPT.md`, `ARCHITECTURE.md`, `STATUS.md`.