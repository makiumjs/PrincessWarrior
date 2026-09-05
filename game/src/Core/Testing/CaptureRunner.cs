using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.Core.Testing;

/// <summary>
/// Generic critic-capture harness. Run windowed (NOT --headless — see
/// ARCHITECTURE.md's "Critic tooling" note: --headless has no renderer, so
/// viewport screenshots throw there):
///
///   godot --path game scenes/CaptureRunner.tscn -- --target=scenes/player/PlayerTestScene.tscn --out=docs/critic-captures/playercamera --frames=180 --shot-every=20 --script=5:move_right:press,60:jump,75:jump,140:dash,140:move_right:release
///
/// Instantiates the target scene as a child, logs per-frame process time to
/// <out>/frametimes.csv (for offline p50/p95/p99), saves a PNG every
/// shot-every frames to <out>/frame_XXXX.png, then quits.
///
/// --script drives real Godot input actions via Input.ActionPress/Release so
/// PlayerController (or any other subsystem reading the Input singleton)
/// reacts exactly as it would to a human. Each entry is
/// "frame:actionName[:press|release]" — no suffix means a tap (press this
/// frame, auto-release 4 frames later); ":press"/":release" hold/release
/// explicitly, for sustained input like movement.
/// </summary>
public partial class CaptureRunner : Node
{
    private enum ScriptEventType { Tap, Press, Release }
    private struct ScriptEvent { public int Frame; public string Action; public ScriptEventType Type; }

    private string _targetScenePath;
    private string _outDir;
    private int _totalFrames = 180;
    private int _shotEvery = 20;
    private int _frameCount;
    private StreamWriter _csv;
    private Node _targetInstance;
    private readonly List<ScriptEvent> _scriptEvents = new();
    private ulong _lastTickUsec;

    public override void _Ready()
    {
        // Cap to a known, fixed rate. Uncapped, this machine's GPU runs the
        // (near-empty) test scenes at ~240fps, so "frame N" no longer maps to
        // ~N/60 real seconds — breaks any --script frame number written
        // assuming 60fps, and breaks real-time Timers in the target scene
        // (e.g. a 2s Timer needs 120 frames at 60fps, not 120 frames at
        // 240fps = 0.5s). Found via the UI subsystem's round-1 critic: its
        // driver's 2s Timer never fired in a 400-frame capture because only
        // ~1.7s of real time had elapsed.
        Engine.MaxFps = 60;

        ParseArgs();

        if (string.IsNullOrEmpty(_targetScenePath))
        {
            GD.PrintErr("CaptureRunner: no --target=<scene path> given. Exiting.");
            GetTree().Quit(1);
            return;
        }

        Directory.CreateDirectory(_outDir);

        _csv = new StreamWriter(Path.Combine(_outDir, "frametimes.csv"), false) { AutoFlush = true };
        // wall_frame_ms: measured ourselves via Time.GetTicksUsec() deltas, not
        // Performance.GetMonitor() — that monitor only refreshes when a live
        // profiler/debugger is attached, so under a plain run it returns a
        // stale/coarse value (confirmed: near-identical readings across 140+
        // frames in round 1's capture). wall_frame_ms is always live.
        _csv.WriteLine("frame,wall_frame_ms,process_time_ms,physics_time_ms");
        _lastTickUsec = Time.GetTicksUsec();

        var targetScene = GD.Load<PackedScene>(_targetScenePath);
        if (targetScene == null)
        {
            GD.PrintErr($"CaptureRunner: failed to load target scene '{_targetScenePath}'.");
            GetTree().Quit(1);
            return;
        }

        _targetInstance = targetScene.Instantiate();
        AddChild(_targetInstance);

        _eventsCsv = new StreamWriter(Path.Combine(_outDir, "events.csv"), false) { AutoFlush = true };
        _eventsCsv.WriteLine("frame,event");
        SubscribeEventBus();

        GD.Print($"CaptureRunner: loaded '{_targetScenePath}', capturing {_totalFrames} frames, shot every {_shotEvery} to '{_outDir}'.");
    }

    private StreamWriter _eventsCsv;

    private void LogEvent(string name) => _eventsCsv?.WriteLine($"{_frameCount},{name}");

    private void SubscribeEventBus()
    {
        var bus = EventBus.Instance;
        if (bus == null) return;
        bus.Jumped += () => LogEvent("Jumped");
        bus.DoubleJumped += () => LogEvent("DoubleJumped");
        bus.Dashed += () => LogEvent("Dashed");
        bus.WallJumped += () => LogEvent("WallJumped");
        bus.Landed += () => LogEvent("Landed");
        bus.PlayerDamaged += (_, _, _, _) => LogEvent("PlayerDamaged");
        bus.PlayerDied += () => LogEvent("PlayerDied");
        bus.EnemyDamaged += (_, _, _, _, _) => LogEvent("EnemyDamaged");
        bus.EnemyDied += _ => LogEvent("EnemyDied");
        bus.AbilityUnlocked += bits => LogEvent($"AbilityUnlocked:{(AbilityFlags)bits}");
        bus.CheckpointReached += id => LogEvent($"CheckpointReached:{id}");
        bus.LevelTransitionRequested += (scenePath, spawnId) => LogEvent($"LevelTransitionRequested:{scenePath}->{spawnId}");
    }

    public override void _Process(double delta)
    {
        _frameCount++;

        foreach (var evt in _scriptEvents)
        {
            if (evt.Frame != _frameCount) continue;
            switch (evt.Type)
            {
                case ScriptEventType.Press:
                    Input.ActionPress(evt.Action);
                    GD.Print($"CaptureRunner: [frame {_frameCount}] press '{evt.Action}'");
                    break;
                case ScriptEventType.Release:
                    Input.ActionRelease(evt.Action);
                    GD.Print($"CaptureRunner: [frame {_frameCount}] release '{evt.Action}'");
                    break;
                case ScriptEventType.Tap:
                    Input.ActionPress(evt.Action);
                    GD.Print($"CaptureRunner: [frame {_frameCount}] tap-press '{evt.Action}'");
                    break;
            }
        }

        // auto-release taps 4 frames after they were pressed
        foreach (var evt in _scriptEvents)
        {
            if (evt.Type == ScriptEventType.Tap && evt.Frame + 4 == _frameCount)
            {
                Input.ActionRelease(evt.Action);
                GD.Print($"CaptureRunner: [frame {_frameCount}] tap-release '{evt.Action}'");
            }
        }

        ulong nowTick = Time.GetTicksUsec();
        double wallFrameMs = (nowTick - _lastTickUsec) / 1000.0;
        _lastTickUsec = nowTick;

        double processMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        double physicsMs = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        _csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F3},{2:F3},{3:F3}", _frameCount, wallFrameMs, processMs, physicsMs));

        if (_frameCount % _shotEvery == 0)
        {
            var img = GetViewport().GetTexture().GetImage();
            string shotPath = Path.Combine(_outDir, $"frame_{_frameCount:D4}.png");
            var err = img.SavePng(shotPath);
            if (err != Error.Ok)
                GD.PrintErr($"CaptureRunner: SavePng failed for frame {_frameCount}: {err}");
        }

        if (_frameCount >= _totalFrames)
        {
            _csv.Flush();
            _csv.Close();
            _eventsCsv?.Flush();
            _eventsCsv?.Close();
            GD.Print($"CaptureRunner: done. {_frameCount} frames logged to '{_outDir}'.");
            GetTree().Quit(0);
        }
    }

    private void ParseArgs()
    {
        string[] args = OS.GetCmdlineUserArgs();
        var map = new Dictionary<string, string>();
        foreach (string arg in args)
        {
            if (!arg.StartsWith("--")) continue;
            string kv = arg.Substring(2);
            int eq = kv.IndexOf('=');
            if (eq < 0) continue;
            map[kv.Substring(0, eq)] = kv.Substring(eq + 1);
        }

        if (map.TryGetValue("target", out string target))
        {
            string cleaned = target;
            if (cleaned.StartsWith("res://")) cleaned = cleaned.Substring("res://".Length);
            cleaned = cleaned.TrimStart('/');
            _targetScenePath = "res://" + cleaned;
        }
        if (map.TryGetValue("out", out string outDir)) _outDir = outDir;
        else _outDir = "docs/critic-captures/default";
        if (map.TryGetValue("frames", out string frames) && int.TryParse(frames, out int f)) _totalFrames = f;
        if (map.TryGetValue("shot-every", out string shotEvery) && int.TryParse(shotEvery, out int s)) _shotEvery = s;

        if (map.TryGetValue("script", out string script))
        {
            foreach (string entry in script.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                if (parts.Length < 2) continue;
                if (!int.TryParse(parts[0], out int frame)) continue;
                string action = parts[1];
                ScriptEventType type = ScriptEventType.Tap;
                if (parts.Length >= 3)
                {
                    if (parts[2] == "press") type = ScriptEventType.Press;
                    else if (parts[2] == "release") type = ScriptEventType.Release;
                }
                _scriptEvents.Add(new ScriptEvent { Frame = frame, Action = action, Type = type });
            }
        }
    }
}
