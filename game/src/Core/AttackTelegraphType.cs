namespace LostCrownlike.Core;

/// <summary>
/// 3-channel combat telegraph system (inspired by Nine Sols and Prince of Persia: The Lost Crown).
/// - StandardWhite: Normal melee or projectile attack. Parriable with standard timing, cancels damage.
/// - CounterGold: High-impact / boss critical attack. Perfect parry breaks armor, inflicts extended stagger.
/// - UnparryableRed: Heavy sweep or unblockable strike. Parry fails completely; player must dodge with i-frames or jump.
/// </summary>
public enum AttackTelegraphType
{
    StandardWhite = 0,
    CounterGold = 1,
    UnparryableRed = 2,
}
