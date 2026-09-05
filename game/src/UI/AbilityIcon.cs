using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.UI;

/// A drawn glyph for an ability, replacing a flat ColorRect.
///
/// The HUD showed each ability as a coloured rectangle with its name under it,
/// which is the same "no art here yet" impression the ability pickups gave
/// before they became potions. Drawn rather than textured on purpose: the free
/// KayKit packs have no UI art, and four small line glyphs cost nothing, scale
/// cleanly and stay readable against any background -- which an imported sprite
/// at this size would not.
///
/// The colour still matches the pickup and its light, so what you saw on the
/// floor is what appears in the corner.
[GlobalClass]
public partial class AbilityIcon : Control
{
    [Export] public AbilityFlags Ability = AbilityFlags.DoubleJump;

    public override void _Ready() => CustomMinimumSize = new Vector2(40, 40);

    public override void _Draw()
    {
        var r = Size;
        float w = r.X, h = r.Y;
        var tint = AbilityLook.ColourFor(Ability);
        float line = Mathf.Max(2.5f, w * 0.075f);

        // A dark plate behind every glyph, so the line art reads over the
        // dungeon as well as over the black bars.
        DrawRect(new Rect2(Vector2.Zero, r), new Color(0.06f, 0.07f, 0.10f, 0.85f));
        DrawRect(new Rect2(Vector2.Zero, r), new Color(tint, 0.55f), false, line * 0.6f);

        switch (Ability)
        {
            case AbilityFlags.DoubleJump:
                // Two chevrons: one jump, then another.
                Chevron(new Vector2(w * 0.5f, h * 0.42f), w * 0.26f, tint, line);
                Chevron(new Vector2(w * 0.5f, h * 0.66f), w * 0.26f, tint, line);
                break;

            case AbilityFlags.Dash:
                // Three trailing streaks: speed, left to right.
                for (int i = 0; i < 3; i++)
                {
                    float y = h * (0.35f + i * 0.15f);
                    float inset = w * (0.18f + i * 0.06f);
                    DrawLine(new Vector2(inset, y), new Vector2(w - w * 0.18f, y), tint, line);
                }
                break;

            case AbilityFlags.WallJump:
                // A solid wall on the left, and a figure leaving it up-right.
                // The first version drew the wall in the same weight as the
                // arrow and the whole glyph read as a tick.
                DrawRect(new Rect2(new Vector2(w * 0.18f, h * 0.14f), new Vector2(w * 0.10f, h * 0.72f)), tint);
                DrawLine(new Vector2(w * 0.34f, h * 0.72f), new Vector2(w * 0.72f, h * 0.32f), tint, line);
                DrawLine(new Vector2(w * 0.72f, h * 0.32f), new Vector2(w * 0.54f, h * 0.34f), tint, line);
                DrawLine(new Vector2(w * 0.72f, h * 0.32f), new Vector2(w * 0.70f, h * 0.52f), tint, line);
                break;

            default:
                // A blade, hilt low-left, with impact lines off the tip.
                DrawLine(new Vector2(w * 0.24f, h * 0.78f), new Vector2(w * 0.64f, h * 0.34f), tint, line * 1.3f);
                DrawLine(new Vector2(w * 0.20f, h * 0.62f), new Vector2(w * 0.36f, h * 0.80f), tint, line);
                for (int i = 0; i < 3; i++)
                {
                    var tip = new Vector2(w * 0.66f, h * 0.32f);
                    float a = Mathf.DegToRad(-70f + i * 45f);
                    DrawLine(tip, tip + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * w * 0.18f, new Color(tint, 0.8f), line * 0.8f);
                }
                break;
        }
    }

    private void Chevron(Vector2 centre, float half, Color tint, float line)
    {
        DrawLine(centre + new Vector2(-half, half * 0.6f), centre, tint, line);
        DrawLine(centre, centre + new Vector2(half, half * 0.6f), tint, line);
    }
}
