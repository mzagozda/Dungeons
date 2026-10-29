using Godot;

namespace Dungeons.Game.Editor;

/// <summary>Fixed packaged diagnostics; never accepts an executable script or scene path.</summary>
public static class PackagedQa
{
    public static bool TryStart(Node owner, IReadOnlyList<string> arguments)
    {
        string[] requests = arguments.Where(argument => argument == "--qa-mode" || argument.StartsWith("--qa-mode=", StringComparison.Ordinal)).ToArray();
        if (requests.Length == 0) { return false; }
        string mode = requests.Length == 1 && requests[0].StartsWith("--qa-mode=", StringComparison.Ordinal) ? requests[0]["--qa-mode=".Length..] : "";
        string? audit = mode switch
        {
            "palette" => "res://editor/qa/audit_editor_palette.gd",
            "ui" => "res://editor/qa/audit_ui_theme.gd",
            "textures" => "res://editor/qa/audit_desktop_texture_imports.gd",
            _ => null
        };
        if (mode == "soak")
        { owner.GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://editor/soak.tscn"); return true; }
        if (audit is null)
        { GD.Print("PACKAGED_QA=FAIL Unknown or ambiguous diagnostic mode."); owner.GetTree().Quit(2); return true; }
        var runner = new Node { Name = "PackagedResourceAudit" };
        runner.SetScript(GD.Load<Script>(audit)); owner.AddChild(runner); return true;
    }
}
