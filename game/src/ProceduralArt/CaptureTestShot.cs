using Godot;

namespace LostCrownlike.ProceduralArt;

/// <summary>
/// One-off headless capture harness for ProceduralArtTestScene. Attached to
/// the scene root; waits a few frames (so shaders finish compiling and the
/// AnimationTree has settled into a non-bind pose), grabs the viewport,
/// saves a PNG, then quits the process. Not part of the runtime subsystem —
/// this is throwaway verification tooling, kept in this folder per the task
/// instructions rather than under scenes/.
/// </summary>
public partial class CaptureTestShot : Node
{
    [Export] public string OutputPath { get; set; } = "C:/Users/Mako/Desktop/Gioco/docs/proceduralart-test-capture.png";
    [Export] public int FramesToWait { get; set; } = 40;

    private int _frames = 0;

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames == FramesToWait)
        {
            var img = GetViewport().GetTexture().GetImage();
            var err = img.SavePng(OutputPath);
            GD.Print(err == Error.Ok
                ? $"[CaptureTestShot] Saved capture to {OutputPath}"
                : $"[CaptureTestShot] FAILED to save capture: {err}");
        }
        else if (_frames > FramesToWait + 5)
        {
            GetTree().Quit();
        }
    }
}
