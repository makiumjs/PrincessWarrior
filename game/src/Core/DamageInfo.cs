using Godot;

namespace LostCrownlike.Core;

public struct DamageInfo
{
    public int Amount;
    public Vector3 SourcePosition;
    public Vector3 Knockback;
    public bool IsCritical;
}
