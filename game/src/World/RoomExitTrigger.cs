using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// The door at the end of a generated room. Rebuilds the room with the next
/// index and puts the player at its start.
///
/// Generated rooms are not .tscn files, so this cannot go through
/// LevelManager's PackedScene path — there is nothing to load. The transition
/// is a regeneration, which keeps every room inside the metric contract
/// instead of falling back to hand-authored blockout scenes.
/// </summary>
public partial class RoomExitTrigger : Area3D
{
    [Export] public int NextRoomIndex = 1;

    /// How many rooms the run is. The exit of the last room ends the run
    /// instead of rebuilding: without this the three layouts cycled forever,
    /// which is a sandbox, not a game with an ending.
    [Export] public int RunLength = 6;

    /// Set on the boss room's exit. The run does not end because the player
    /// reached the far wall; it ends because the thing guarding it is dead.
    [Export] public bool SealedUntilBossDies;

    /// Optional branching destination (e.g. "Catacombs" vs "Crucible").
    [Export] public string DestinationActName { get; set; } = "";

    /// Custom portal hue for branching biomes (e.g. Molten Crimson vs Ethereal Teal).
    [Export] public Color CustomPortalColor { get; set; } = Colors.Transparent;

    private bool _used;
    private MeshInstance3D _seal;
    private int _recheckFrames;

    private const string AssetDir = "res://assets/kaykit/dungeon/";
    private MeshInstance3D _portalVortex;
    private StandardMaterial3D _portalMaterial;
    private OmniLight3D _portalBeacon;
    private float _portalTime;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = PhysicsLayers.Player;
        Monitoring = true;
        BodyEntered += OnBodyEntered;

        BuildGatewayVisuals();
        if (SealedUntilBossDies) BuildSeal();
    }

    public override void _Process(double delta)
    {
        _portalTime += (float)delta;
        float pulse = 0.85f + 0.15f * Mathf.Sin(_portalTime * 3.2f);
        if (_portalMaterial != null)
            _portalMaterial.EmissionEnergyMultiplier = 2.8f * pulse;
        if (_portalBeacon != null)
            _portalBeacon.LightEnergy = 2.8f * pulse;
    }

    private void BuildGatewayVisuals()
    {
        // 1. Imposing Stone Archway
        var archScene = GD.Load<PackedScene>($"{AssetDir}wall_doorway.gltf");
        if (archScene != null)
        {
            var arch = archScene.Instantiate<Node3D>();
            arch.Position = new Vector3(0f, -1.2f, -0.4f);
            StripDoor(arch);
            AddChild(arch);
        }

        // 2. Flanking Stone Columns
        var columnScene = GD.Load<PackedScene>($"{AssetDir}column.gltf");
        if (columnScene != null)
        {
            var colL = columnScene.Instantiate<Node3D>();
            colL.Position = new Vector3(-1.8f, -1.2f, -0.2f);
            AddChild(colL);

            var colR = columnScene.Instantiate<Node3D>();
            colR.Position = new Vector3(1.8f, -1.2f, -0.2f);
            AddChild(colR);
        }

        // 3. Wall Torches flanking the portal
        var torchScene = GD.Load<PackedScene>($"{AssetDir}torch_mounted.gltf");
        if (torchScene != null)
        {
            var torchL = torchScene.Instantiate<Node3D>();
            torchL.Position = new Vector3(-1.6f, 0.4f, 0.1f);
            torchL.AddChild(new TorchFlicker
            {
                Position = new Vector3(0f, 0.35f, 0.5f),
                LightColor = new Color(1f, 0.72f, 0.42f),
                BaseEnergy = 2.4f,
                OmniRange = 6.0f,
                ShadowEnabled = false,
            });
            AddChild(torchL);

            var torchR = torchScene.Instantiate<Node3D>();
            torchR.Position = new Vector3(1.6f, 0.4f, 0.1f);
            torchR.AddChild(new TorchFlicker
            {
                Position = new Vector3(0f, 0.35f, 0.5f),
                LightColor = new Color(1f, 0.72f, 0.42f),
                BaseEnergy = 2.4f,
                OmniRange = 6.0f,
                ShadowEnabled = false,
            });
            AddChild(torchR);
        }

        // 4. Twin Royal Banners
        var bannerScene = GD.Load<PackedScene>($"{AssetDir}banner_red.gltf");
        if (bannerScene != null)
        {
            SpawnBanner(bannerScene, new Vector3(-1.95f, 1.0f, -0.1f));
            SpawnBanner(bannerScene, new Vector3(1.95f, 1.0f, -0.1f));
        }

        // 5. Dimensional Portal Veil
        bool isFinalExit = NextRoomIndex >= RunLength;
        bool hasCustomColor = CustomPortalColor.A > 0.01f;
        Color portalColor = hasCustomColor
            ? new Color(CustomPortalColor.R, CustomPortalColor.G, CustomPortalColor.B, 0.75f)
            : (isFinalExit
                ? new Color(1f, 0.85f, 0.3f, 0.7f)
                : new Color(0.2f, 0.8f, 1f, 0.7f));
        Color emissionColor = hasCustomColor
            ? CustomPortalColor
            : (isFinalExit
                ? new Color(1f, 0.82f, 0.25f)
                : new Color(0.3f, 0.88f, 1f));

        _portalMaterial = new StandardMaterial3D
        {
            AlbedoColor = portalColor,
            EmissionEnabled = true,
            Emission = emissionColor,
            EmissionEnergyMultiplier = 2.8f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };

        _portalVortex = new MeshInstance3D
        {
            Name = "PortalVortex",
            Position = new Vector3(0f, 0.2f, -0.1f),
            Mesh = new BoxMesh { Size = new Vector3(1.65f, 2.75f, 0.12f) },
            MaterialOverride = _portalMaterial,
        };
        AddChild(_portalVortex);

        // 6. Portal Beacon Light
        _portalBeacon = new OmniLight3D
        {
            Name = "PortalBeacon",
            Position = new Vector3(0f, 0.3f, 0.2f),
            LightColor = hasCustomColor ? CustomPortalColor : (isFinalExit ? new Color(1f, 0.85f, 0.4f) : new Color(0.35f, 0.88f, 1f)),
            LightEnergy = 2.8f,
            OmniRange = 10.0f,
            ShadowEnabled = false,
        };
        AddChild(_portalBeacon);
    }

    private static void StripDoor(Node node)
    {
        for (int i = node.GetChildCount() - 1; i >= 0; i--)
        {
            var child = node.GetChild(i);
            if (child.Name.ToString().StartsWith("wall_doorway_door"))
            {
                child.QueueFree();
            }
            else
            {
                StripDoor(child);
            }
        }
    }

    private void SpawnBanner(PackedScene scene, Vector3 at)
    {
        var pivot = new BannerSway { Position = at };
        AddChild(pivot);
        var banner = scene.Instantiate<Node3D>();
        banner.Position = new Vector3(0f, -0.6f, 0f);
        pivot.AddChild(banner);
    }

    /// A door the player cannot pass and cannot see is a bug report. The seal
    /// is drawn, and it is removed the frame the boss dies, so "it opened"
    /// is something that happens on screen rather than something the player
    /// has to infer by walking into the wall again.
    private void BuildSeal()
    {
        _seal = new MeshInstance3D
        {
            Name = "ExitSeal",
            Position = new Vector3(0f, 0.2f, 0.05f),
            Mesh = new BoxMesh { Size = new Vector3(1.7f, 2.85f, 0.25f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.9f, 0.15f, 0.12f, 0.8f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.2f, 0.1f),
                EmissionEnergyMultiplier = 3.6f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        var sealLight = new OmniLight3D
        {
            Name = "SealLight",
            LightColor = new Color(1f, 0.2f, 0.1f),
            LightEnergy = 3.2f,
            OmniRange = 8.5f,
            ShadowEnabled = false,
        };
        _seal.AddChild(sealLight);
        AddChild(_seal);
    }

    /// Physics, not _Process: this asks the physics server what overlaps the
    /// trigger, and in _Process that answer is one frame stale -- which is
    /// exactly long enough to miss a player who was already standing here.
    public override void _PhysicsProcess(double delta)
    {
        // A few frames of re-checking after the door opens, not one. A single
        // check reads whatever the physics server had settled at that instant,
        // and the boss's death knockback is very likely still moving the player
        // through the doorway on that frame.
        if (_recheckFrames > 0 && !_used)
        {
            _recheckFrames--;
            foreach (var body in GetOverlappingBodies())
                OnBodyEntered(body);
        }

        if (!SealedUntilBossDies) return;
        if (BossAlive()) return;

        SealedUntilBossDies = false;
        _seal?.QueueFree();
        _seal = null;
        GD.Print("[Exit] the boss is dead; the way out is open");

        // The player is very likely standing in the doorway at this exact
        // moment -- it is where you back away to, and where the boss's last
        // knockback throws you. BodyEntered does not fire again for a body
        // that never left, so without this the door opens and does nothing
        // until the player steps out and walks back in. Found by a check that
        // put the player in before the kill and then saw the run refuse to end.
        _recheckFrames = 12;
    }

    /// A boss counts as alive only while it is not in its death state. Checking
    /// merely that the node exists would keep the door shut for the seconds the
    /// corpse spends sinking, which reads as the kill not having registered.
    private bool BossAlive()
    {
        foreach (var n in GetTree().GetNodesInGroup("boss"))
            if (n is AI.EnemyController e
                && !e.IsQueuedForDeletion()
                && e.State != AI.EnemyController.EnemyState.Dead)
                return true;
        return false;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_used) return;
        // Not consumed: the player will walk into this again once the boss is
        // down, and marking it used here would seal the run shut for good.
        if (SealedUntilBossDies && BossAlive())
        {
            GD.Print("[Exit] sealed: the boss is still alive");
            return;
        }
        _used = true;

        var builder = GetParent<DungeonRoomBuilder>();
        if (builder == null)
        {
            GD.PushError("RoomExitTrigger: expected a DungeonRoomBuilder parent");
            return;
        }

        if (NextRoomIndex >= RunLength)
        {
            GD.Print($"[Exit] run complete after {NextRoomIndex} rooms");
            EventBus.Instance?.EmitRunCompleted(NextRoomIndex);
            return;
        }

        GD.Print($"[Exit] entering room {NextRoomIndex}");
        EventBus.Instance?.EmitLevelTransitionRequested($"generated:{NextRoomIndex}", "start");

        // Deferred: tearing the room down while the physics server is still
        // resolving this very overlap crashes or drops collisions.
        // Spread across frames rather than done on this one: the whole build
        // is about 155ms, which is nine dropped frames at the exact moment the
        // player walks through a door.
        builder.CallDeferred(nameof(DungeonRoomBuilder.BeginRebuild), NextRoomIndex);
    }
}
