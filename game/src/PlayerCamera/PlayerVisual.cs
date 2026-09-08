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
    [Export] public string CharacterScene = "res://assets/quaternius/characters/Female_Ranger.gltf";
    [Export] public string[] AnimationScenes = System.Array.Empty<string>();

    private static readonly string[] DefaultQuaterniusAnimScenes =
    {
        "res://assets/quaternius/animations/UAL1_Standard.glb",
        "res://assets/quaternius/animations/UAL2_Standard.glb",
    };

    private static readonly string[] DefaultKayKitAnimScenes =
    {
        "res://assets/kaykit/animations/Rig_Medium_MovementBasic.glb",
        "res://assets/kaykit/animations/Rig_Medium_General.glb",
        "res://assets/kaykit/animations/Rig_Medium_Combat.glb",
    };

    /// Blend time between clips, seconds. Keeps state flips from popping.
    [Export] public float CrossFade = 0.12f;

    /// The models face +Z, but gameplay runs along X (PlayerController
    /// flips facing by rotating the body's Y between 0 and 180). Without this
    /// offset the character sprints sideways, looking straight at the camera.
    [Export] public float ModelYawOffsetDegrees = 90f;

    /// KayKit rigs expose dedicated "handslot.r" / "handslot.l" bones for
    /// props; Quaternius humanoid rigs expose "hand_r" / "hand_l".
    [Export] public string WeaponScene = "res://assets/kaykit/props/sword_1handed.gltf";
    [Export] public string WeaponBone = "hand_r";
    [Export] public string OffhandScene = "";
    [Export] public string OffhandBone = "hand_l";


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
    private bool _isQuaternius;

    /// One trail, built once and reused for every swing. See SlashTrail for why
    /// it is pooled rather than spawned.
    private Combat.SlashTrail _slashTrail;
    private bool _wasSwinging;

    /// How far the weapon travels through a swing, degrees.
    /// Blade finish. 0.85 metallic against the kit's near-zero is what makes
    /// the torches land on the edge; 0.22 roughness keeps the highlight tight
    /// enough to travel as the arm moves instead of washing the whole face.
    [Export] public float BladeMetallic { get; set; } = 0.85f;
    [Export] public float BladeRoughness { get; set; } = 0.22f;

    [Export] public float SwingArcDegrees = 140f;

    /// How far the whole model leans into a heavy swing, degrees.
    [Export] public float SwingLeanDegrees = 7f;
    private float _parrySuccessTimer;
    private bool _parrySuccessIsPerfect;
    private int _lastComboStep = -1;
    private bool _wasAttacking, _wasParrying;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>()
                  ?? GetTree().GetFirstNodeInGroup("player") as PlayerController;

        if (Core.EventBus.Instance != null)
            Core.EventBus.Instance.Parried += OnParried;

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

        _skel = character.GetNodeOrNull<Skeleton3D>("Rig_Medium/Skeleton3D")
             ?? character.GetNodeOrNull<Skeleton3D>("Armature/Skeleton3D")
             ?? FindSkeleton(character);

        _isQuaternius = _skel != null && (_skel.FindBone("hand_r") >= 0 || CharacterScene.Contains("quaternius"));

        if (_skel != null)
        {
            _upperArm = _skel.FindBone("upperarm.r");
            if (_upperArm < 0) _upperArm = _skel.FindBone("upperarm_r");
            _lowerArm = _skel.FindBone("lowerarm.r");
            if (_lowerArm < 0) _lowerArm = _skel.FindBone("lowerarm_r");
            if (_upperArm >= 0) _restUpperArm = _skel.GetBonePoseRotation(_upperArm);
            if (_lowerArm >= 0) _restLowerArm = _skel.GetBonePoseRotation(_lowerArm);
        }
        AddChild(character);

        string wBone = WeaponBone;
        if (!_isQuaternius && wBone == "hand_r") wBone = "handslot.r";
        else if (_isQuaternius && wBone == "handslot.r") wBone = "hand_r";

        string oBone = OffhandBone;
        if (!_isQuaternius && oBone == "hand_l") oBone = "handslot.l";
        else if (_isQuaternius && oBone == "handslot.l") oBone = "hand_l";

        AttachProp(character, WeaponScene, wBone);
        AttachProp(character, OffhandScene, oBone);

        _anim = new AnimationPlayer { Name = "AnimationPlayer" };
        character.AddChild(_anim);

        string[] scenesToLoad = AnimationScenes != null && AnimationScenes.Length > 0
            ? AnimationScenes
            : (_isQuaternius ? DefaultQuaterniusAnimScenes : DefaultKayKitAnimScenes);

        int libIndex = 0;
        foreach (var path in scenesToLoad)
        {
            foreach (var lib in Core.AnimationLibraryCache.Get(path))
            {
                _anim.AddAnimationLibrary($"lib{libIndex}", lib);
                libIndex++;
            }
        }

        MakeLoopable();
        BuildBlendTree(character);
    }

    // ---- Upper body over legs ---------------------------------------------
    //
    // Reported from play: attack while running and the character SLIDES. The
    // cause was not the attack clip. Slash_A has five tracks -- chest, both
    // upper arms, both forearms -- and touches no leg at all; the legs stopped
    // because AnimationPlayer plays ONE clip, so starting the swing stopped
    // Running_A and left the legs in whatever pose they were holding.
    //
    // So the swing is layered instead of substituted. AnimationNodeOneShot
    // blends the action over the locomotion, and its filter is built FROM THE
    // ACTION CLIP'S OWN TRACK LIST rather than from five paths typed out here:
    // the clip already states which bones it claims, and a hand-written filter
    // is a second copy of that which goes stale the first time the rig changes.

    private AnimationTree _tree;
    // The clip lives on the NODE, not in the tree's parameter list:
    // AnimationNodeAnimation exposes `animation` as a resource property and not
    // as a blend parameter, so setting "parameters/loco/animation" writes into
    // nothing and the tree plays silence. Cost one red check to find.
    private AnimationNodeAnimation _locoNode, _actionNode;
    private const string ScaleParam = "parameters/scale/scale";
    private const string ShotRequest = "parameters/shot/request";
    private const string ShotActive = "parameters/shot/active";

    /// TEST SEAM. The action clip currently layered over the legs, short name,
    /// empty when none is. With a tree driving the skeleton, AnimationPlayer's
    /// own CurrentAnimation is empty and says nothing.
    public string CurrentActionClip =>
        _tree != null && _tree.Get(ShotActive).AsBool() ? _actionClip : "";

    private string _actionClip = "", _locoClip = "";

    private string ResolveLocoClip(string clipName)
    {
        if (!_isQuaternius) return clipName;
        return clipName switch
        {
            "Idle_A" or "Idle_B" => "Idle",
            "Running_A" => "Jog_Fwd",
            "Running_B" => "Sprint",
            "Walking_A" or "Walking_B" or "Walking_C" => "Walk",
            "Jump_Start" => "Jump_Start",
            "Jump_Idle" => "Jump",
            "Hit_A" => "Hit_Chest",
            _ => "Idle",
        };
    }

    private string ResolveActionClip(string clipName)
    {
        if (!_isQuaternius) return clipName;
        if (clipName == "Parry_A") return "Sword_Block";
        if (clipName == "Slash_A")
        {
            if (_combat != null && _combat.SwingIsHeavy) return "Sword_Heavy_Combo";
            int step = _combat != null ? _combat.ComboStep : 0;
            return step switch
            {
                1 => "Sword_Regular_B",
                2 => "Sword_Regular_C",
                _ => "Sword_Regular_A",
            };
        }
        return clipName;
    }

    private void BuildBlendTree(Node3D character)
    {
        _locoNode = new AnimationNodeAnimation();
        _actionNode = new AnimationNodeAnimation();
        var loco = _locoNode;
        var action = _actionNode;
        var scale = new AnimationNodeTimeScale();
        var shot = new AnimationNodeOneShot
        {
            FadeInTime = 0.05,
            FadeOutTime = 0.12,
            FilterEnabled = true,
        };

        string sampleAction = _isQuaternius ? "Sword_Regular_A" : "Slash_A";
        foreach (var path in ActionTrackPaths(sampleAction))
        {
            if (_isQuaternius)
            {
                string p = path.ToString();
                if (p.Contains("pelvis") || p.Contains("thigh") || p.Contains("calf") || p.Contains("foot") || p.Contains("toe"))
                    continue;
            }
            shot.SetFilterPath(path, true);
        }

        var root = new AnimationNodeBlendTree();
        root.AddNode("loco", loco);
        root.AddNode("action", action);
        root.AddNode("scale", scale);
        root.AddNode("shot", shot);
        root.ConnectNode("scale", 0, "action");
        root.ConnectNode("shot", 0, "loco");
        root.ConnectNode("shot", 1, "scale");
        root.ConnectNode("output", 0, "shot");

        _tree = new AnimationTree { Name = "AnimationTree", TreeRoot = root };
        character.AddChild(_tree);
        _tree.AnimPlayer = _tree.GetPathTo(_anim);
        _tree.Active = true;
    }

    /// The bones an action clip claims, read off the clip.
    private Godot.Collections.Array<NodePath> ActionTrackPaths(string clip)
    {
        var paths = new Godot.Collections.Array<NodePath>();
        string full = Full(clip);
        if (full.Length == 0) return paths;
        var a = _anim.GetAnimation(full);
        for (int i = 0; i < a.GetTrackCount(); i++) paths.Add(a.TrackGetPath(i));
        return paths;
    }

    private string Full(string clipName)
    {
        foreach (var f in _anim.GetAnimationList())
            if (f == clipName || f.ToString().EndsWith("/" + clipName)) return f;
        return "";
    }

    /// The legs. Switching this never interrupts an action: that is the point.
    private void SetLoco(string clipName)
    {
        string resolved = ResolveLocoClip(clipName);
        if (resolved == _locoClip) return;
        string full = Full(resolved);
        if (full.Length == 0) return;
        _locoClip = resolved;
        _locoNode.Animation = full;
    }

    /// Layers an action over the legs. `restart` re-fires the one-shot, which
    /// is what a new combo step needs and what holding a guard does not.
    private void SetAction(string clipName, float speed, bool restart)
    {
        string resolved = ResolveActionClip(clipName);
        string full = Full(resolved);
        if (full.Length == 0) return;
        if (clipName != _actionClip || _actionNode.Animation != full)
        {
            _actionClip = clipName;
            _actionNode.Animation = full;
            restart = true;
        }
        _tree.Set(ScaleParam, speed);
        if (restart || !_tree.Get(ShotActive).AsBool())
            _tree.Set(ShotRequest, (int)AnimationNodeOneShot.OneShotRequest.Fire);
    }

    private void StopAction()
    {
        if (_tree != null && _tree.Get(ShotActive).AsBool())
            _tree.Set(ShotRequest, (int)AnimationNodeOneShot.OneShotRequest.FadeOut);
    }

    public override void _ExitTree()
    {
        if (Core.EventBus.Instance != null)
            Core.EventBus.Instance.Parried -= OnParried;
    }

    private void OnParried(bool perfect, Vector3 atPosition)
    {
        _parrySuccessTimer = 0.28f;
        _parrySuccessIsPerfect = perfect;

        // Parented to the ROOM, not to the player: an effect that rides the
        // character reads as attached to them rather than as having happened at
        // a place, and the parry happened where the blades met. A room teardown
        // then takes it along, which is the same rule the impact bursts follow.
        //
        // Folded into the handler this class already had rather than added as a
        // second subscription to the same signal: two handlers is two things to
        // unsubscribe, and this file has exactly one _ExitTree.
        Combat.ClashRing.Spawn(_player?.GetParent() ?? GetParent(), atPosition, perfect);
    }

    public override void _Process(double delta)
    {
        if (_player == null || _anim == null) return;
        _combat ??= _player.GetNodeOrNull<Combat.CombatController>("CombatController");

        bool parrying = _combat != null && _combat.IsParrying;
        bool attacking = _combat != null && _combat.IsAttacking && !parrying;

        // The legs follow the movement state ALWAYS now, action or no action.
        // That single line is the slide: they used to be switched off whenever
        // an action clip took the player over.
        SetLoco(ClipFor(_player.CurrentState));

        if (_parrySuccessTimer > 0f)
        {
            _parrySuccessTimer -= (float)delta;
            // Holds the guard rather than dropping to rest. The deflect is the
            // frame the player is being asked to read, and it used to be spent
            // standing at rest with a glowing sword.
            SetAction("Parry_A", 1f, restart: false);
        }
        else if (parrying)
        {
            // The guard is up by frame 3 of a 22-frame clip -- 0.12s of its
            // 0.92s -- and the parry window is 380ms, so the clip is sped up to
            // put the pose inside the window rather than arriving after it.
            SetAction("Parry_A",
                      0.92f / Mathf.Max(_combat.ParryWindowMs / 1000f, 0.05f),
                      restart: !_wasParrying);
            _wasParrying = true;
        }
        else if (attacking)
        {
            float totalDur = _combat.CurrentAttackTotalDuration;
            bool fresh = !_wasAttacking || _combat.ComboStep != _lastComboStep;
            SetAction("Slash_A", 0.833f / Mathf.Max(totalDur, 0.05f), restart: fresh);
            _lastComboStep = _combat.ComboStep;
            _wasAttacking = true;
        }
        else
        {
            _wasAttacking = false;
            _wasParrying = false;
            _lastComboStep = -1;
            StopAction();
        }

        if (!parrying && _parrySuccessTimer <= 0f) _wasParrying = false;

        TickSwing((float)delta);
    }

    /// Animates the attack and defensive reactions. The authored Slash_A clip
    /// carries the body (chest, shoulders, head, legs), while procedural code
    /// drives the weapon pivot for precision and controls blade glow and deflect stance.
    private void TickSwing(float delta)
    {
        _combat ??= _player.GetNodeOrNull<Combat.CombatController>("CombatController");
        if (_skel == null) return;

        // 1. Parry success: definite deflect / riposte stance
        if (_parrySuccessTimer > 0f)
        {
            float ratio = Mathf.Clamp(_parrySuccessTimer / 0.28f, 0f, 1f);
            // The arm is the CLIP's now. Posing the same bones from here fought
            // whatever the AnimationPlayer was driving that frame -- which was
            // Idle_A or Running_A, because the parry had no clip of its own --
            // and a single bone nudged against a full-body pose is what "the
            // sword lights up but it is not a parry" looked like.

            if (_modelRoot != null)
            {
                float lean = _parrySuccessIsPerfect ? -7f : -3f;
                _modelRoot.RotationDegrees = new Vector3(lean * ratio, _modelRoot.RotationDegrees.Y, _modelRoot.RotationDegrees.Z);
            }

            if (_swingPivot != null)
                _swingPivot.RotationDegrees = new Vector3(30f * ratio, 0f, 0f);

            Color glow = _parrySuccessIsPerfect ? new Color(1f, 0.88f, 0.35f) : new Color(0.75f, 0.9f, 1f);
            float energy = (_parrySuccessIsPerfect ? 5.5f : 3.0f) * ratio;
            SetWeaponGlow(glow, energy);
            return;
        }

        // 2. Active parry guard
        bool parrying = _combat != null && _combat.IsParrying;
        if (parrying)
        {
            _swingReleaseTimer = 0.2f;

            // Snappy discrete tell at window onset (first 70ms) transitioning into the perfect/block state
            bool isOnset = _combat.ParryHeldFor < 0.07f;
            Color glowColor = isOnset
                ? new Color(1f, 1f, 1f)
                : (_combat.ParryIsPerfectNow ? new Color(1f, 0.95f, 0.85f) : new Color(1f, 0.65f, 0.22f));
            float energy = isOnset
                ? 6.0f
                : (_combat.ParryIsPerfectNow ? 4.2f : 1.6f);

            SetWeaponGlow(glowColor, energy);
            return;
        }

        // 3. Attack swing: Slash_A drives the body bones; procedural code drives the weapon pivot & glow
        bool swinging = _combat != null && _combat.IsSwinging;

        // Fired on the RISING edge only. IsSwinging is true for every frame of
        // the swing, so firing on the flag would restart the trail sixty times a
        // second and it would never fade -- a solid arc welded to the blade
        // rather than an afterimage left behind it.
        if (swinging && !_wasSwinging)
        {
            EnsureSlashTrail();
            _slashTrail?.Fire(
                _combat.SwingIsCharged ? Combat.SlashTrail.Weight.Charged
              : _combat.SwingIsHeavy ? Combat.SlashTrail.Weight.Heavy
              : Combat.SlashTrail.Weight.Light);
        }
        _wasSwinging = swinging;

        if (swinging)
        {
            _swingReleaseTimer = 0.25f;
            float t = _combat.SwingProgress;
            float arc = _combat.SwingIsHeavy ? SwingArcDegrees * 1.25f : SwingArcDegrees;
            float weaponAngle = Mathf.Lerp(-arc * 0.4f, arc * 0.6f, t);

            if (_swingPivot != null)
                _swingPivot.RotationDegrees = new Vector3(weaponAngle, 0f, 0f);

            if (_combat.IsActivePhase)
            {
                Color glow = _combat.SwingIsHeavy ? new Color(1f, 0.45f, 0.2f) : new Color(0.85f, 0.95f, 1f);
                float energy = _combat.SwingIsHeavy ? 2.5f : 1.5f;
                SetWeaponGlow(glow, energy);
            }
            else
            {
                SetWeaponGlow(new Color(1f, 1f, 1f), 0f);
            }
            return;
        }

        // 4. Return weapon pivot and glow to rest
        if (_swingReleaseTimer > 0f) _swingReleaseTimer -= delta;
        if (_swingPivot != null && _swingPivot.RotationDegrees != Vector3.Zero)
            _swingPivot.RotationDegrees = _swingPivot.RotationDegrees.Lerp(Vector3.Zero, 14f * delta);

        if (_modelRoot != null && _modelRoot.RotationDegrees.X != 0f)
            _modelRoot.RotationDegrees = new Vector3(Mathf.Lerp(_modelRoot.RotationDegrees.X, 0f, 12f * delta), _modelRoot.RotationDegrees.Y, _modelRoot.RotationDegrees.Z);

        SetWeaponGlow(new Color(1f, 1f, 1f), 0f);
    }

    /// <summary>
    /// Builds the one trail this visual will ever use, on the pivot the weapon
    /// already swings on, so it inherits the blade's motion for free rather
    /// than trying to track a bone transform only the AnimationPlayer really
    /// knows.
    ///
    /// Lazy rather than done in _Ready: the pivot is created by AttachProp,
    /// which runs during _Ready, and a builder that assumed the order would
    /// break the day the props moved. Runs once -- every later swing finds the
    /// trail already there.
    /// </summary>
    private void EnsureSlashTrail()
    {
        if (_slashTrail != null && IsInstanceValid(_slashTrail)) return;
        if (_swingPivot == null) return;

        _slashTrail = new Combat.SlashTrail { Name = "SlashTrail" };
        _swingPivot.AddChild(_slashTrail);
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
                    if (child is BoneAttachment3D att && (att.BoneName == WeaponBone || att.BoneName == "hand_r" || att.BoneName == "handslot.r"))
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
    public static readonly System.Collections.Generic.HashSet<string> KayKitLooping = new()
    {
        "Idle_A", "Idle_B", "Running_A", "Running_B",
        "Walking_A", "Walking_B", "Walking_C", "Jump_Idle",
    };

    public static readonly System.Collections.Generic.HashSet<string> QuaterniusLooping = new()
    {
        "Crouch_Fwd", "Crouch_Idle", "Dance", "Driving", "Idle", "Idle_Talking", "Idle_Torch",
        "Jog_Fwd", "Jump", "Pistol_Idle", "Push", "Sitting_Idle", "Sitting_Talking",
        "Spell_Simple_Idle", "Sprint", "Swim_Fwd", "Swim_Idle", "Walk", "Walk_Formal",
        "Idle_FoldArms", "Idle_Lantern", "Idle_No", "Idle_Rail", "Idle_Shield",
        "Idle_TalkingPhone", "NinjaJump_Idle", "Slide", "TreeChopping", "Walk_Carry",
        "Zombie_Idle", "Zombie_Walk_Fwd",
    };

    public static readonly System.Collections.Generic.HashSet<string> Looping = new()
    {
        "Idle_A", "Idle_B", "Running_A", "Running_B",
        "Walking_A", "Walking_B", "Walking_C", "Jump_Idle",
        "Crouch_Fwd", "Crouch_Idle", "Dance", "Driving", "Idle", "Idle_Talking", "Idle_Torch",
        "Jog_Fwd", "Jump", "Pistol_Idle", "Push", "Sitting_Idle", "Sitting_Talking",
        "Spell_Simple_Idle", "Sprint", "Swim_Fwd", "Swim_Idle", "Walk", "Walk_Formal",
        "Idle_FoldArms", "Idle_Lantern", "Idle_No", "Idle_Rail", "Idle_Shield",
        "Idle_TalkingPhone", "NinjaJump_Idle", "Slide", "TreeChopping", "Walk_Carry",
        "Zombie_Idle", "Zombie_Walk_Fwd",
    };

    public static bool IsContinuingClip(string name) =>
        KayKitLooping.Contains(name) || QuaterniusLooping.Contains(name);

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
            if (!IsContinuingClip(name)) continue;

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


    private void AttachProp(Node3D character, string scenePath, string boneName)
    {
        if (string.IsNullOrEmpty(scenePath)) return;

        var skel = character.GetNodeOrNull<Skeleton3D>("Rig_Medium/Skeleton3D")
                ?? character.GetNodeOrNull<Skeleton3D>("Armature/Skeleton3D")
                ?? FindSkeleton(character);
        if (skel == null)
        {
            GD.PrintErr($"{Name}: no Skeleton3D, cannot attach '{scenePath}'");
            return;
        }

        string actualBone = boneName;
        if (skel.FindBone(actualBone) < 0)
        {
            if (boneName == WeaponBone || boneName == "handslot.r" || boneName == "hand_r")
            {
                if (skel.FindBone("hand_r") >= 0) actualBone = "hand_r";
                else if (skel.FindBone("hand.R") >= 0) actualBone = "hand.R";
                else if (skel.FindBone("handslot.r") >= 0) actualBone = "handslot.r";
            }
            else if (boneName == OffhandBone || boneName == "handslot.l" || boneName == "hand_l")
            {
                if (skel.FindBone("hand_l") >= 0) actualBone = "hand_l";
                else if (skel.FindBone("hand.L") >= 0) actualBone = "hand.L";
                else if (skel.FindBone("handslot.l") >= 0) actualBone = "handslot.l";
            }
        }

        if (skel.FindBone(actualBone) < 0)
        {
            GD.PrintErr($"{Name}: bone '{actualBone}' not in rig, cannot attach '{scenePath}'");
            return;
        }

        var propScene = GD.Load<PackedScene>(scenePath);
        if (propScene == null)
        {
            GD.PrintErr($"{Name}: could not load prop '{scenePath}'");
            return;
        }

        var attachment = new BoneAttachment3D { Name = $"Attach_{actualBone.Replace('.', '_')}" };
        skel.AddChild(attachment);
        attachment.BoneName = actualBone;

        var pivot = new Node3D { Name = "SwingPivot" };
        attachment.AddChild(pivot);
        var prop = propScene.Instantiate<Node3D>();
        pivot.AddChild(prop);

        if (actualBone == "hand_r")
        {
            prop.Position = new Vector3(0f, 0.05f, 0f);
            prop.RotationDegrees = new Vector3(-90f, 0f, 0f);
        }

        if (boneName == WeaponBone || actualBone == "hand_r" || actualBone == "handslot.r")
        {
            _swingPivot = pivot;
            PolishBlade(prop);
        }
    }

    private static Skeleton3D FindSkeleton(Node node)
    {
        if (node is Skeleton3D skel) return skel;
        foreach (var child in node.GetChildren())
        {
            var found = FindSkeleton(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Makes the blade read as metal.
    ///
    /// The KayKit props ship with a flat, mostly-diffuse material because they
    /// are built to be readable at any lighting setup. This game lights its
    /// rooms with flickering torches, and a blade that does not take a
    /// specular highlight sits in the frame as another piece of kit geometry --
    /// the exact complaint the visual-fidelity score has been carrying.
    ///
    /// Applied as a surface OVERRIDE on the mesh instance rather than by
    /// editing the imported material: the .gltf's material is a shared imported
    /// resource, and writing to it would re-tint every other copy of that
    /// sword in the project, including any an enemy is holding.
    ///
    /// Runs once, at attach time. Nothing here touches _Process.
    /// </summary>
    private void PolishBlade(Node from)
    {
        if (from is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int surface = 0; surface < mi.Mesh.GetSurfaceCount(); surface++)
            {
                // Duplicated per surface so the override owns its material and
                // the imported one is left alone.
                if (mi.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D src) continue;

                var polished = (StandardMaterial3D)src.Duplicate();
                polished.Metallic = BladeMetallic;
                polished.Roughness = BladeRoughness;
                // Sharpens the highlight rather than brightening the whole
                // blade: a torch should travel along the edge as the player
                // turns, which is what sells it as steel.
                polished.MetallicSpecular = 0.62f;
                mi.SetSurfaceOverrideMaterial(surface, polished);
            }
        }

        foreach (var child in from.GetChildren()) PolishBlade(child);
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
