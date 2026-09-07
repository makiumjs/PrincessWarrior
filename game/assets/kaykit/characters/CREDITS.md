# KayKit characters

All CC0, by Kay Lousberg (`kaylousberg.itch.io`): free for personal and
commercial use, attribution not required. Credit is given anyway.

- **Adventurers 2.0** — `Barbarian`, `Knight`, `Mage`, `Ranger`, `Rogue`,
  `Rogue_Hooded`, and the `Rig_Medium_*` animation libraries.
- **Skeletons 1.1 (FREE)** — `Skeleton_Minion`, `Skeleton_Rogue`,
  `Skeleton_Mage`, `Skeleton_Warrior`.

The two packs share the **same `Rig_Medium` armature**, verified rather than
assumed: comparing the node lists of `Skeleton_Warrior.glb` and `Rogue.glb`
gives 24 nodes in common, `upperarm.r`, `lowerarm.r`, `chest` and `upperleg.l`
among them. So the skeletons inherit every clip the adventurers use, including
`Slash_A` and `Parry_A`, which are authored here in `tools/anim/build_slash.py`
and keyed on those same bone names. Changing an enemy's model is changing one
string in its `.tscn`.

## Who wears what, and why it is one each

| Enemy | Model |
|---|---|
| `BasicMelee` | `Skeleton_Minion` |
| `Skirmisher` | `Skeleton_Rogue` |
| `Warlock` | `Skeleton_Mage` |
| `Sentinel` | `Skeleton_Warrior` |
| `CrossbowSentry` | `Ranger` |
| `Warden` | `Barbarian` |

Before this, six enemy types wore four models: the **Warden and the Skirmisher
were both a Barbarian**, so the final boss shared a silhouette with a common
enemy, and the **Sentinel and the grunt were both a Knight**. This project had
already found and hand-fixed the same defect once -- the first crossbow sentry
was the grunt's knight with a dagger, invisible at the camera's distance -- and
it came back, because nothing was watching for it. Check 32 watches now, and
names the offenders when it fails.

The two living humans are deliberate: a crypt of skeletons with a living Warden
at the end, and one living sentry among them, is the cheapest way to make the
boss read as the boss.
