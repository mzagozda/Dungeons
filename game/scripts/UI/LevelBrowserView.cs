using Dungeons.Core.Level;
using Dungeons.Game.Persistence;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>Responsive, focus-following card grid with visible-only thumbnail decoding.</summary>
public partial class LevelBrowserView : ScrollContainer
{
    private readonly List<LevelCard> _cards = [];
    private readonly LevelPreviewPool _previews = new();
    private GridContainer _grid = null!;
    public IReadOnlyList<LevelCard> Cards => _cards;
    public int Columns => _grid.Columns;
    public event Action<CatalogEntry>? Selected;
    public event Action<CatalogEntry>? DiagnosticRequested;
    public void Build(IReadOnlyList<CatalogEntry> entries, IReadOnlyDictionary<string, LevelRecord> records, string? focusedId = null)
    {
        Name = "LevelBrowser"; SizeFlagsVertical = SizeFlags.ExpandFill; SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // Disabled propagates the old grid width as our minimum and prevents a
        // window shrinking enough to trigger reflow. Keep the axis clipped,
        // without a scrollbar, until the next layout fits its new column count.
        HorizontalScrollMode = ScrollMode.ShowNever;
        _grid = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; AddChild(_grid);
        _grid.AddThemeConstantOverride("h_separation", 16); _grid.AddThemeConstantOverride("v_separation", 16);
        foreach (CatalogEntry entry in entries)
        {
            records.TryGetValue(entry.Id, out LevelRecord? record); var card = new LevelCard(); _grid.AddChild(card); card.Build(entry, record); _cards.Add(card);
            card.Activated += selected => Selected?.Invoke(selected); card.DiagnosticRequested += selected => DiagnosticRequested?.Invoke(selected);
            card.SelectButton.FocusEntered += () => Callable.From(() =>
            {
                if (!GodotObject.IsInstanceValid(card) || !card.IsInsideTree()) { return; }
                // EnsureControlVisible aligns the bottom of a card taller than
                // the viewport. Start such cards at their heading instead.
                if (card.Size.Y > Size.Y) { ScrollVertical = Math.Max(0, (int)card.Position.Y); }
                else { EnsureControlVisible(card); }
            }).CallDeferred();
        }
        LevelCard? focus = _cards.FirstOrDefault(card => card.Entry.Id == focusedId) ?? _cards.FirstOrDefault();
        if (focus is not null) { Callable.From(() => { if (GodotObject.IsInstanceValid(focus) && focus.IsInsideTree()) { focus.SelectButton.GrabFocus(); } }).CallDeferred(); }
    }
    public override void _Process(double delta)
    {
        if (_grid is null || Size.X <= 0) { return; }
        if (ScrollHorizontal != 0) { ScrollHorizontal = 0; }
        float width = MathF.Max(80, Size.X - (GetVScrollBar().Visible ? GetVScrollBar().Size.X : 0)); int columns = System.Math.Clamp((int)((width + 16) / 356), 1, 4);
        bool changed = columns != _grid.Columns; _grid.Columns = columns;
        float cardWidth = MathF.Floor((width - (columns - 1) * 16) / columns); Rect2 visible = GetGlobalRect();
        for (int i = 0; i < _cards.Count; i++)
        {
            LevelCard card = _cards[i]; card.SetCardWidth(cardWidth, GetViewport().GetVisibleRect().Size.Y < 420);
            card.SetPreview(card.GetGlobalRect().Intersects(visible) ? _previews.Get(card.Entry) : null);
            if (changed)
            {
                card.SelectButton.FocusNeighborLeft = GetPathFor(i % columns > 0 ? i - 1 : i);
                card.SelectButton.FocusNeighborRight = GetPathFor(i % columns < columns - 1 && i + 1 < _cards.Count ? i + 1 : i);
                card.SelectButton.FocusNeighborTop = GetPathFor(i >= columns ? i - columns : i);
                if (i + columns < _cards.Count) { card.SelectButton.FocusNeighborBottom = GetPathFor(i + columns); }
                else { card.SelectButton.FocusNeighborBottom = new NodePath(""); }
            }
            NodePath GetPathFor(int index) => card.SelectButton.GetPathTo(_cards[index].SelectButton);
        }
    }
    public override void _ExitTree() => _previews.Dispose();
}
