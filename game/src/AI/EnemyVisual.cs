using Godot;

namespace LostCrownlike.AI;

/// <summary>
/// View layer for an enemy: instances a KayKit character and plays the clip
/// matching EnemyController's current state. Same rig/library merge trick as
/// PlayerVisual — model and clips live in separate .glb files authored on the
/// same Rig_Medium skeleton, so no retargeting is needed.
///
/// This also closes two findings from the Combat round-2 critic: the enemy had
/// no visible hit reaction, and a fatal hit left the corpse standing upright
/// because Die() had no visual at all.
/// </summary>
public partial class EnemyVisual : Node3D
{
    [Export] public string CharacterScene = "res://assets/kaykit/characters/Knight.glb";
    [Export] public string[] AnimationScenes =
    {
        "res://assets/kaykit/animations/Rig_Medium_MovementBasic.glb",
        "res://assets/kaykit/animations/Rig_Medium_General.glb",
    };
    [Export] public float CrossFade = 0.12f;
    [Export] public float ModelYawOffsetDegrees = 90f;

    /// KayKit rigs expose dedicated "handslot.r" / "handslot.l" bones for
    /// props — attaching there instead of the hand bone keeps the weapon from
    /// intersecting the fist geometry.
    [Export] public string WeaponScene = "res://assets/kaykit/props/sword_1handed.gltf";
    [Export] public string WeaponBone = "handslot.r";
    [Export] public string OffhandScene = "res://assets/kaykit/props/shield_round.gltf";
    [Export] public string OffhandBone = "handslot.l";


    private EnemyController _enemy;
    private AnimationPlayer _anim;
    private Skeleton3D _skel;
    private int _upperArm = -1, _lowerArm = -1;
    private Quaternion _restUpperArm = Quaternion.Identity, _restLowerArm = Quaternion.Identity;
    private System.Collections.Generic.List<MeshInstance3D> _weaponMeshes;
    private float _tellFade;
    private string _current = "";

    public override void _Ready()
    {
        _enemy = GetParent() as EnemyController ?? GetParent()?.GetParent() as EnemyController;

        var charScene = GD.Load<PackedScene>(CharacterScene);
        if (charScene == null)
        {
            GD.PrintErr($"EnemyVisual: could not load '{CharacterScene}'");
            return;
        }

        var character = charScene.Instantiate<Node3D>();
        character.Name = "Character";
        character.RotationDegrees = new Vector3(0f, ModelYawOffsetDegrees, 0f);
        AddChild(character);

        AttachProp(character, WeaponScene, WeaponBone);
        AttachProp(character, OffhandScene, OffhandBone);

        _anim = new AnimationPlayer { Name = "AnimationPlayer" };
        character.AddChild(_anim);

        // Shared, not reloaded per character: see Core.AnimationLibraryCache.
        // Each character used to instantiate both animation GLBs and deep-copy
        // their libraries, which cost about 100ms of a 231ms room rebuild for
        // three enemies.
        int libIndex = 0;
        foreach (var path in AnimationScenes)
        {
            foreach (var lib in Core.AnimationLibraryCache.Get(path))
            {
                _anim.AddAnimationLibrary($"kk{libIndex}", lib);
                libIndex++;
            }
        }

        CacheRigForTell();

        // Enemies slide for the same reason the player did: every KayKit clip
        // imports with LoopMode.None, so Walking_A stops after 1.07s and the
        // model holds its last pose while the body keeps moving.
        PlayerCamera.PlayerVisual.LoopContinuingClips(_anim);
    }

    public override void _Process(double delta)
    {
        if (_enemy == null || _anim == null) return;
        Play(ClipFor(_enemy.State));
        TickTell((float)delta);
    }

    /// Telegraphs the attack.
    ///
    /// Every enemy used to wind up with no distinct pose: EnemyState.Attack
    /// mapped to the same Throw clip for all three types, and the free KayKit
    /// pack has no melee swing to map instead. So the wind-up is posed from
    /// bones -- the weapon arm draws back over the whole wind-up, then snaps
    /// through on the strike -- and the weapon glows while it is drawing.
    ///
    /// The glow is not decoration. At this camera distance a raised arm on a
    /// character two centimetres tall is not a reliable signal, and an attack
    /// the player cannot see coming can only be answered by luck. This is also
    /// the thing a parry has to hang off: a perfect-parry window is meaningless
    /// until the wind-up reads.
    private void TickTell(float delta)
    {
        if (_skel == null) return;

        bool winding = _enemy.IsWindingUp;
        bool striking = _enemy.IsStriking;

        if (winding) _tellFade = 1f;
        else if (_tellFade > 0f) _tellFade = Mathf.Max(0f, _tellFade - delta * 4f);
        else if (!striking) { ReleaseBones(delta); SetGlow(0f); return; }

        // Draw back through the wind-up, then throw forward on the strike.
        float draw = winding ? Mathf.Lerp(0f, -95f, _enemy.WindupProgress)
                   : striking ? 55f
                   : 0f;
        float elbow = winding ? Mathf.Lerp(0f, -40f, _enemy.WindupProgress) : 15f;

        PoseBone(_upperArm, _restUpperArm, Mathf.DegToRad(draw), winding ? 0.35f : 0.6f);
        PoseBone(_lowerArm, _restLowerArm, Mathf.DegToRad(elbow), winding ? 0.35f : 0.6f);

        // Brightest just before the blow, so the cue peaks when the answer is
        // due rather than when the wind-up starts.
        SetGlow(winding ? _enemy.WindupProgress * _tellFade : _tellFade * 0.35f);
    }

    private void ReleaseBones(float delta)
    {
        PoseBone(_upperArm, _restUpperArm, 0f, 8f * delta);
        PoseBone(_lowerArm, _restLowerArm, 0f, 8f * delta);
    }

    private void PoseBone(int bone, Quaternion rest, float angle, float weight)
    {
        if (bone < 0) return;

        // Every operand normalised, and the result too. Slerp throws on a
        // quaternion that is not unit, and this reads its own previous output
        // back every frame -- float drift accumulates until it trips, which is
        // why it showed up as an INTERMITTENT flood of
        // "Quaternion is not normalized" rather than as a clean failure. The
        // product of two unit quaternions is unit in exact arithmetic and only
        // approximately unit in floats.
        var target = (rest * new Quaternion(Vector3.Right, angle)).Normalized();
        var current = _skel.GetBonePoseRotation(bone).Normalized();
        _skel.SetBonePoseRotation(bone, current.Slerp(target, Mathf.Clamp(weight, 0f, 1f)).Normalized());
    }

    private void SetGlow(float amount)
    {
        if (_weaponMeshes == null) return;
        foreach (var mi in _weaponMeshes)
        {
            if (!IsInstanceValid(mi)) continue;
            if (mi.MaterialOverlay is not StandardMaterial3D mat)
            {
                mat = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    EmissionEnabled = true,
                };
                mi.MaterialOverlay = mat;
            }
            mat.AlbedoColor = new Color(1f, 0.55f, 0.2f, amount * 0.5f);
            mat.Emission = new Color(1f, 0.5f, 0.15f);
            mat.EmissionEnergyMultiplier = amount * 3.5f;
        }
    }

    private void CacheRigForTell()
    {
        var character = GetChildOrNull<Node3D>(0);
        _skel = FindSkeleton(this);
        if (_skel != null)
        {
            _upperArm = _skel.FindBone("upperarm.r");
            _lowerArm = _skel.FindBone("lowerarm.r");
            if (_upperArm >= 0) _restUpperArm = _skel.GetBonePoseRotation(_upperArm);
            if (_lowerArm >= 0) _restLowerArm = _skel.GetBonePoseRotation(_lowerArm);

            // Found by BONE, not by node name. Godot forbids "." in node names
            // and silently rewrites them, so "Attach_handslot.r" never matches
            // what the node is actually called -- the lookup returned null every
            // time and the weapon glow measured 0.00 while the arm pose worked.
            _weaponMeshes = new System.Collections.Generic.List<MeshInstance3D>();
            foreach (var child in _skel.GetChildren())
            {
                if (child is not BoneAttachment3D att) continue;
                if (att.BoneName != WeaponBone) continue;
                CollectMeshes(att, _weaponMeshes);
            }
        }
        _ = character;
    }

    private static void CollectMeshes(Node n, System.Collections.Generic.List<MeshInstance3D> into)
    {
        if (n is MeshInstance3D mi) into.Add(mi);
        foreach (var c in n.GetChildren()) CollectMeshes(c, into);
    }

    private static Skeleton3D FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (var c in n.GetChildren()) { var f = FindSkeleton(c); if (f != null) return f; }
        return null;
    }

    private static string ClipFor(EnemyController.EnemyState state) => state switch
    {
        EnemyController.EnemyState.Patrol => "Walking_A",
        EnemyController.EnemyState.Chase => "Running_A",
        // No dedicated melee-swing clip in the free Adventurers pack; Throw is
        // the closest full-body committed action. Flagged, not hidden.
        EnemyController.EnemyState.Attack => "Throw",
        EnemyController.EnemyState.Stagger => "Hit_A",
        EnemyController.EnemyState.Dead => "Death_A",
        _ => "Idle_A",
    };

    private void Play(string clipName)
    {
        if (clipName == _current) return;
        foreach (var full in _anim.GetAnimationList())
        {
            if (!full.EndsWith("/" + clipName) && full != clipName) continue;
            // Death must not loop back to standing.
            _anim.Play(full, CrossFade);
            if (clipName == "Death_A")
                _anim.GetAnimation(full).LoopMode = Animation.LoopModeEnum.None;
            _current = clipName;
            return;
        }
    }


    private void AttachProp(Node3D character, string scenePath, string boneName)
    {
        if (string.IsNullOrEmpty(scenePath)) return;

        var skel = character.GetNodeOrNull<Skeleton3D>("Rig_Medium/Skeleton3D");
        if (skel == null)
        {
            GD.PrintErr($"{Name}: no Skeleton3D, cannot attach '{scenePath}'");
            return;
        }
        if (skel.FindBone(boneName) < 0)
        {
            GD.PrintErr($"{Name}: bone '{boneName}' not in rig, cannot attach '{scenePath}'");
            return;
        }

        var propScene = GD.Load<PackedScene>(scenePath);
        if (propScene == null)
        {
            GD.PrintErr($"{Name}: could not load prop '{scenePath}'");
            return;
        }

        var attachment = new BoneAttachment3D { Name = $"Attach_{boneName}" };
        skel.AddChild(attachment);
        attachment.BoneName = boneName;
        attachment.AddChild(propScene.Instantiate<Node3D>());
    }

    private static AnimationPlayer FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer ap) return ap;
        foreach (var child in node.GetChildren())
        {
            var found = FindAnimationPlayer(child);
            if (found != null) return found;
        }
        return null;
    }
}
