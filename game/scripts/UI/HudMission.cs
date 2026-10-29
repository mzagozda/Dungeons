using System.Text;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.UI;

public partial class Hud
{
    private HudObjectives _objectives = null!;
    private HudBanner _missionBanner = null!, _notice = null!;
    private readonly MissionBannerState _messageFade = new();
    private readonly HudToastState _checkpointToast = new();
    private MissionState? _lastMission;
    private Func<string, string> _localize = null!;
    private string _messageSource = "", _lockSource = "", _lockedText = "";
    private float _counterScale = 1;
    private static readonly string[] AiModeNames = Enum.GetNames<EnemyMode>();
    private readonly int[] _aiCounts = new int[AiModeNames.Length];
    private readonly StringBuilder _aiText = new(256);
    private void BuildMissionControls(Control root)
    {
        _localize = Localize;
        _objectives = new() { Name = "Objectives" }; root.AddChild(_objectives); _objective = _objectives.Primary;
        _missionBanner = new() { Name = "MissionMessage", Visible = false }; root.AddChild(_missionBanner); _message = _missionBanner.TextLabel;
        _notice = new() { Name = "CentreNotice", Visible = false }; root.AddChild(_notice);
    }
    private void RefreshMissionHud(Entity hero, double delta, float reference, bool compact)
    {
        MissionState mission = _session.World.Mission;
        if (!ReferenceEquals(_lastMission, mission))
        { _lastMission = mission; _objectives.Bind(mission, _localize); _messageFade.Clear(); _checkpointToast.Show("", 0); _messageSource = ""; }
        _objectives.Refresh(_localize);
        bool paused = _session.State != SessionState.Running;
        _messageFade.Update(mission.Message, mission.MessageTicksRemaining, delta, paused); _checkpointToast.Advance(delta, paused);
        if (_messageSource != _messageFade.Text) { _messageSource = _messageFade.Text; _message.Text = Localize(_messageSource); }
        float pad = compact ? 12 : 32 * reference, gap = MathF.Max(4, 8 * reference);
        int heading = Math.Max(12, (int)MathF.Round((compact ? 16 : 24 * reference) * _textScale));
        int secondary = Math.Max(10, (int)MathF.Round(heading * .75f));
        _counterScale = MathF.Max(.5f, reference * _textScale); _compactCounters.Bind(hero.Inventory.Emeralds, _session.World.Lives, _counterScale);
        float counterWidth = _compactCounters.DesiredWidth; _compactCounters.Visible = true;
        _compactCounters.OffsetTop = pad; _compactCounters.OffsetBottom = pad + 28 * _counterScale;
        _compactCounters.OffsetRight = -pad - _pauseReserve; _compactCounters.OffsetLeft = _compactCounters.OffsetRight - counterWidth;
        float objectiveWidth = _root.Size.X - counterWidth - _pauseReserve - pad * 3, objectiveTop = pad;
        if (objectiveWidth < heading * 9) { objectiveWidth = _root.Size.X - pad * 2; objectiveTop = _compactCounters.OffsetBottom + gap; }
        objectiveWidth = MathF.Min(600 * reference * MathF.Max(1, _textScale), objectiveWidth);
        float objectiveLimit = MathF.Min(_root.Size.Y * .3f, 200 * reference * _textScale);
        float objectiveHeight = _objectives.Arrange(new(pad, objectiveTop), objectiveWidth, objectiveLimit, heading, secondary, gap);
        float headerBottom = MathF.Max(_compactCounters.OffsetBottom, objectiveTop + objectiveHeight);
        float width = MathF.Max(1, MathF.Min(760 * reference * MathF.Max(1, _textScale), _root.Size.X - pad * 2));
        float messageHeight = _missionBanner.Configure(width, heading, Math.Max(3, (int)MathF.Round(6 * reference)));
        _missionBanner.Position = new((_root.Size.X - width) / 2, headerBottom + gap);
        string noticeText = _checkpointToast.Text; float noticeAlpha = _checkpointToast.Alpha;
        if (noticeText.Length == 0 && _exitLock.Visible)
        {
            string source = mission.PrimaryText;
            if (_lockSource != source || _lockedText.Length == 0) { _lockSource = source; _lockedText = string.Format(Tr("HUD_LOCKED_OBJECTIVE"), source.Length > 0 ? Localize(source) : Tr("HUD_LOCKED_FALLBACK")); }
            noticeText = _lockedText; noticeAlpha = 1;
        }
        if (_notice.TextLabel.Text != noticeText) { _notice.TextLabel.Text = noticeText; }
        _notice.ShowLeading(_checkpointToast.Text.Length == 0 && _exitLock.Visible);
        float noticeHeight = _notice.Configure(width, heading, Math.Max(3, (int)MathF.Round(6 * reference)));
        float messageAlpha = _messageFade.Alpha;
        float minNotice = headerBottom + gap + (messageAlpha > 0 ? messageHeight + gap : 0), maxNotice = _bar.Position.Y - noticeHeight - gap;
        if (noticeText.Length > 0 && minNotice > maxNotice) { messageAlpha = 0; minNotice = headerBottom + gap; }
        _missionBanner.SetOpacity(messageAlpha); _notice.SetOpacity(noticeAlpha);
        float centre = (_root.Size.Y - noticeHeight) / 2;
        _notice.Position = new((_root.Size.X - width) / 2, MathF.Max(minNotice, MathF.Min(maxNotice, centre)));
        _debug.Position = new(pad, MathF.Max(120 * reference, headerBottom + gap));
        _debug.Size = new(MathF.Max(1, MathF.Min(800 * reference, _root.Size.X - pad * 2)), _debug.Size.Y);
    }
    private string AiSummary()
    {
        Array.Clear(_aiCounts);
        foreach (Entity entity in _session.World.Entities) { int mode = (int)entity.Ai.Mode; if (entity.Kind == EntityKind.Enemy && mode >= 0 && mode < _aiCounts.Length) { _aiCounts[mode]++; } }
        _aiText.Clear().Append("AI "); bool any = false;
        for (int i = 0; i < _aiCounts.Length; i++) { if (_aiCounts[i] == 0) { continue; } if (any) { _aiText.Append(" | "); } _aiText.Append(AiModeNames[i]).Append(' ').Append(_aiCounts[i]); any = true; }
        if (!any) { _aiText.Append("none"); } return _aiText.ToString();
    }
}
