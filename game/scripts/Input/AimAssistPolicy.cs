using Dungeons.Core.Sim;
using Dungeons.Game.Persistence;

namespace Dungeons.Game.Input;

/// <summary>Optional assist becomes an explicit recorded aim; replay never reads host settings.</summary>
public static class AimAssistPolicy
{
    public static PlayerInput Apply(World world, PlayerInput input, AimAssist assist, bool touch)
    {
        if (input.AimValid || (input.Pressed & (InputButtons.Attack | InputButtons.Ranged)) == 0 || world.Players.Count == 0 || assist == AimAssist.Normal || assist == AimAssist.Off && touch) { return input; }
        Entity hero = world.Players[0]; bool ranged = (input.Pressed & InputButtons.Attack) == 0 && (input.Pressed & InputButtons.Ranged) != 0;
        var aim = assist == AimAssist.Off ? hero.Facing : AutoAim.Resolve(world, hero, ranged, strong: true);
        return new(input.Move, aim, true, input.Pressed, input.Held);
    }
}
