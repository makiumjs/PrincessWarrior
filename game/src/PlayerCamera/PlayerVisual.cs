using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.PlayerCamera;

/// <summary>
/// View layer for the player: instances the KayKit character model, merges the
/// shared Rig_Medium animation libraries onto it, and plays the clip matching
/// PlayerController's current MovementState.
///
/// Reads PlayerController state; never writes it. The model and clips ship in
/// separate .glb files authored on the same rig with identical node names, so
/// the imported tracks ("Rig_Medium/Skeleton3D:BoneName") resolve with no
/// retargeting once an AnimationPlayer is added at the same relative position.
/// </summary>
public partial class PlayerVisual : Node3D
{
    [Export] public string CharacterScene = "res://assets/kaykit/characters/Rogue.glb";
    [Export] public string[] AnimationScenes =
    {
        "res://assets/kaykit/animations/Rig_Medium_MovementBasic.glb",
        "res://assets/kaykit/animations/Rig_Medium_General.glb",
    };

    /// Blend time between clips, seconds. Keeps state flips from popping.
    [Export] public float CrossFade = 0.12f;

    /// The KayKit models face +Z, but gameplay runs along X (PlayerController
    /// flips facing by rotating the body's Y between 0 and 180). Without this
    /// offset the character sprints sideways, looking straight at the camera.
    [Export] public float ModelYawOffsetDegrees = 90f;

    /// KayKit rigs expose dedicated "handslot.r" / "handslot.l" bones for
    /// props — attaching there instead of the hand bone keeps the weapon from
    /// intersecting the fist geometry.
    [Export] public string WeaponScene = "res://assets/kaykit/props/dagger.gltf";
    [Export] public string WeaponBone = "handslot.r";
    [Export] public string OffhandScene = "";
    [Export] public string OffhandBone = "handslot.l";


    private PlayerController _player;
    private AnimationPlayer _anim;
    private Node3D _swingPivot;
    private Skeleton3D _skel;
    private float _swingReleaseTimer;
    private System.Collections.Generic.List<MeshInstance3D> _weaponMeshes;
    private int _upperArm = -1, _lowerArm = -1;
    private Quaternion _restUpperArm = Quaternion.Identity, _restLowerArm = Quaternion.Identity;
    private Node3D _modelRoot;
    private Combat.CombatController _combat;

    /// How far the weapon travels through a swing, degrees.
    [Export] public float SwingArcDegrees = 140f;

    /// How far the whole model leans into a heavy swing, degrees.
    [Export] public float SwingLeanDegrees = 7f;
    private string _current = "";

    public override void _Ready()
    {
        _player = GetParent<PlayerController>()
                  ?? GetTree().GetFirstNodeInGroup("player") as PlayerController;

        var charScene = GD.Load<PackedScene>(CharacterScene);
        if (charScene == null)
        {
            GD.PrintErr($"PlayerVisual: could not load '{CharacterScene}'");
            return;
        }

        var character = charScene.Instantiate<Node3D>();
        character.Name = "Character";
        character.RotationDegrees = new Vector3(0f, ModelYawOffsetDegrees, 0f);
        _modelRoot = character;
        _skel = character.GetNodeOrNull<Skeleton3D>("Rig_Medium/Skeleton3D");
        if (_skel != null)
        {
            _upperArm = _skel.FindBone("upperarm.r");
            _lowerArm = _skel.FindBone("lowerarm.r");
            if (_upperArm >= 0) _restUpperArm = _skel.GetBonePoseRotation(_upperArm);
            if (_lowerArm >= 0) _restLowerArm = _skel.GetBonePoseRotation(_lowerArm);
        }
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

        MakeLoopable();
    }

    public override void _Process(double delta)
    {
        if (_player == null || _anim == null) return;
        Play(ClipFor(_player.CurrentState));
        TickSwing((float)delta);
    }

    /// Draws the attack, because no clip exists to play. The weapon rotates
    /// through an arc on a pivot under the hand bone, and the model leans into
    /// it; both return to rest the moment the swing ends. Cheap, but it is the
    /// difference between a hit that reads and one that is invisible.
    private void TickSwing(float delta)
    {
        _combat ??= _player.GetNodeOrNull<Combat.CombatController>("CombatController");
        if (_skel == null) return;

        bool parrying = _combat != null && _combat.IsParrying;
        bool swinging = _combat != null && _combat.IsSwinging && !parrying;

        // Guard pose. A parry with no pose is a parry the player cannot see
        // working, and the difference between the two kinds has to be visible
        // too: the blade comes up across the body either way, and glows WHITE
        // while the perfect window is open, amber after it has closed. The
        // colour is the only thing separating "you are safe" from "you are safe
        // and about to punish".
        if (parrying)
        {
            _swingReleaseTimer = 0.2f;
            PoseBone(_upperArm, Mathf.DegToRad(-70f), _restUpperArm, delta, true);
            PoseBone(_lowerArm, Mathf.DegToRad(-75f), _restLowerArm, delta, true);
            SetWeaponGlow(_combat.ParryIsPerfectNow
                ? new Color(1f, 1f, 0.95f)
                : new Color(1f, 0.7f, 0.25f),
                _combat.ParryIsPerfectNow ? 4.5f : 1.6f);
            return;
        }
        SetWeaponGlow(new Color(1f, 1f, 1f), 0f);


        // Hands off the skeleton unless a swing is happening or just ended.
        // Writing bone poses every frame would override the run cycle's own arm
        // motion and leave the character running with a stiff arm -- trading one
        // reported animation bug for another.
        if (swinging) _swingReleaseTimer = 0.25f;
        else if (_swingReleaseTimer > 0f) _swingReleaseTimer -= (float)delta;
        if (!swinging && _swingReleaseTimer <= 0f) return;
        float t = swinging ? _combat.SwingProgress : 0f;
        float scale = swinging && _combat.SwingIsHeavy ? 1.3f : 1f;

        // Pose the ARM, not just the weapon. Rotating the prop on its bone was
        // a wrist flick: the blade moved, the character did not, and at this
        // camera distance it read as "he crouches slightly and you cannot see
        // the sword". There is no attack clip to play -- the free KayKit pack
        // ships none -- so the swing is built from bone poses.
        //
        // Raise behind the head, then drive down and forward. The upper arm
        // carries the arc, the forearm follows a beat later so the blade is not
        // a rigid stick, and the chest twists into it.
        float lift  = swinging ? SwingCurve(t, -110f, 85f) : 0f;
        float elbow = swinging ? SwingCurve(Mathf.Clamp(t - 0.12f, 0f, 1f), -50f, 20f) : 0f;
        float twist = swinging ? Mathf.Sin(t * Mathf.Pi) * SwingLeanDegrees : 0f;

        PoseBone(_upperArm, Mathf.DegToRad(lift * scale), _restUpperArm, delta, swinging);
        PoseBone(_lowerArm, Mathf.DegToRad(elbow * scale), _restLowerArm, delta, swinging);

        if (_modelRoot != null)
            _modelRoot.RotationDegrees = new Vector3(
                Mathf.Lerp(_modelRoot.RotationDegrees.X, twist * scale, swinging ? 0.5f : 12f * delta),
                _modelRoot.RotationDegrees.Y, _modelRoot.RotationDegrees.Z);
    }

    /// Lights the blade. Found by BONE rather than by node name: Godot forbids
    /// "." in node names and silently rewrites them, so "Attach_handslot.r"
    /// matches nothing -- the same trap that made the enemies' attack tell
    /// measure zero glow while their arms moved correctly.
    private void SetWeaponGlow(Color colour, float energy)
    {
        if (_weaponMeshes == null)
        {
            _weaponMeshes = new System.Collections.Generic.List<MeshInstance3D>();
            if (_skel != null)
                foreach (var child in _skel.GetChildren())
                    if (child is BoneAttachment3D att && att.BoneName == WeaponBone)
                        Collect(att, _weaponMeshes);
        }

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
            mat.AlbedoColor = new Color(colour.R, colour.G, colour.B, energy > 0f ? 0.35f : 0f);
            mat.Emission = colour;
            mat.EmissionEnergyMultiplier = energy;
        }
    }

    private static void Collect(Node n, System.Collections.Generic.List<MeshInstance3D> into)
    {
        if (n is MeshInstance3D mi) into.Add(mi);
        foreach (var c in n.GetChildren()) Collect(c, into);
    }

    /// Wind back for the first third, then throw through the rest. A single
    /// sine starts the blade already moving, which reads as a poke.
    private static float SwingCurve(float t, float back, float through) =>
        t < 0.33f ? Mathf.Lerp(0f, back, t / 0.33f)
                  : Mathf.Lerp(back, through, (t - 0.33f) / 0.67f);

    private void PoseBone(int bone, float angle, Quaternion rest, double delta, bool swinging)
    {
        if (bone < 0) return;
        // Normalised on both sides and on the way out: Slerp throws on a
        // non-unit quaternion, and feeding its own output back every frame lets
        // float drift build until it does. Seen as an intermittent flood from
        // the enemy version of this same code.
        var target = (rest * new Quaternion(Vector3.Right, angle)).Normalized();
        var current = _skel.GetBonePoseRotation(bone).Normalized();
        // Snap into the swing, ease out of it: a slash that fades in is not a
        // slash, but popping back to rest at the end is a visible glitch.
        _skel.SetBonePoseRotation(bone, current.Slerp(swinging ? target : rest.Normalized(),
                                                      swinging ? 0.55f : (float)(10.0 * delta)).Normalized());
    }

    /// Maps the movement state machine onto the clips the pack actually ships.
    private static string ClipFor(MovementState state) => state switch
    {
        MovementState.Run => "Running_A",
        MovementState.Jump => "Jump_Start",
        MovementState.DoubleJump => "Jump_Start",
        MovementState.Fall => "Jump_Idle",
        MovementState.WallSlide => "Jump_Idle",
        MovementState.WallJump => "Jump_Start",
        // No dedicated dash clip in the free pack: the fast forward-lean run
        // reads better for a burst than an airborne pose. Flagged as a gap to
        // fill if the paid tier or a second pack adds a proper dash/roll.
        MovementState.Dash => "Running_B",
        MovementState.Hurt => "Hit_A",
        _ => "Idle_A",
    };

    /// Clips that describe a CONTINUING state have to loop, and the KayKit
    /// GLBs ship every clip with LoopMode.None. Running_A is 0.80s: it played
    /// once, stopped, and left the model frozen in its last pose while the
    /// character kept moving -- the character appeared to slide. Reported from
    /// play, not caught by any check, because nothing here was ever looking at
    /// the animation after its first cycle.
    ///
    /// Only continuing states are looped. A jump start, a landing or a hit are
    /// one-shot by nature and looping them would be its own bug.
    public static readonly System.Collections.Generic.HashSet<string> Looping = new()
    {
        "Idle_A", "Idle_B", "Running_A", "Running_B",
        "Walking_A", "Walking_B", "Walking_C", "Jump_Idle",
    };

    /// Applied to the AnimationPlayer's own animations, after every library is
    /// added. Setting it on the duplicated AnimationLibrary before adding it
    /// silently did nothing -- the clips still read back LoopMode.None -- so
    /// this reaches for the resources that are actually played.
    private void MakeLoopable() => LoopContinuingClips(_anim);

    /// Shared with EnemyVisual: enemies build the same libraries from the same
    /// GLBs and walked with the same frozen-pose slide.
    public static int LoopContinuingClips(AnimationPlayer player)
    {
        var _anim = player;
        int looped = 0;
        foreach (string key in _anim.GetAnimationList())
        {
            string name = key;
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            if (!Looping.Contains(name)) continue;

            var anim = _anim.GetAnimation(key);
            anim.LoopMode = Animation.LoopModeEnum.Linear;
            // Read back. Godot's imported resources can be read-only, and a
            // silently ignored write here is exactly the class of failure that
            // let this ship: the count said 8, the clips still said None.
            if (_anim.GetAnimation(key).LoopMode != Animation.LoopModeEnum.Linear)
                GD.PushError($"PlayerVisual: setting LoopMode on '{key}' did not stick.");
            looped++;
        }
        return looped;
    }

    private void Play(string clipName)
    {
        if (clipName == _current) return;

        foreach (var full in _anim.GetAnimationList())
        {
            if (!full.EndsWith("/" + clipName) && full != clipName) continue;
            _anim.Play(full, CrossFade);
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

        // A pivot between the bone and the prop, so the weapon can be swung
        // without touching the skeleton. There is no attack clip to play: the
        // free KayKit pack ships none.
        var pivot = new Node3D { Name = "SwingPivot" };
        attachment.AddChild(pivot);
        pivot.AddChild(propScene.Instantiate<Node3D>());
        if (boneName == WeaponBone) _swingPivot = pivot;
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
