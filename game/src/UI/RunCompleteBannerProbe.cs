using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.UI;

/// CAPTURE-ONLY: fires RunCompleted so the HUD banner can be photographed.
/// The banner is built in code, so nothing in the editor shows whether it
/// actually renders; this exists to make that visible rather than assumed.
public partial class RunCompleteBannerProbe : Node
{
    private int _f;

    public override void _Process(double delta)
    {
        _f++;
        if (_f == 30) EventBus.Instance?.EmitRunCompleted(6);
    }
}
