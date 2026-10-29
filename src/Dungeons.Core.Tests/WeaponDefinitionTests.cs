using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class WeaponDefinitionTests
{
    [Fact]
    public void ShippedHeroReferencesWeaponDataAndTypedActionRules()
    {
        string root = FindRoot(), folder = Path.Combine(root, "game/content");
        string[] files = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(folder, "index.json")))!;
        ContentRegistry content = ContentRegistry.Load(files.Select(p => (p, File.ReadAllText(Path.Combine(folder, p)))));
        Assert.True(content.IsValid, string.Join('\n', content.Diagnostics)); Assert.Equal(2, content.Weapons.Count);
        EntityDef hero = content.Entities["core:hero"];
        Assert.Same(content.Weapons["core:sword"], hero.Weapons.Melee); Assert.Same(content.Weapons["core:bow"], hero.Weapons.Ranged);
        Assert.Equal(3, hero.Weapons.Melee!.Swings.Length); MeleeSwing third = hero.Weapons.Melee.Swings[2];
        Assert.Equal((9, 7, 21, 18), (third.Windup, third.Active, third.Recovery, third.Damage)); Assert.Equal(2.5f, third.Knockback); Assert.Equal(1.9f, third.Reach);
        Assert.InRange(hero.Weapons.Melee.Swings[0].CosHalfAngle, .49999f, .50001f);
        Assert.Equal((24, 3f, 3, 20, 36), (hero.Controls.RollTicks, hero.Controls.RollDistance, hero.Controls.RollInvulnerableFirst, hero.Controls.RollInvulnerableLast, hero.Controls.RollCooldown));
        Assert.Equal((30, 99, 1800), (hero.Controls.ArrowsStart, hero.Controls.ArrowsMax, hero.Controls.PotionCooldown));
        ProjectileSpec arrow = content.Entities[hero.Weapons.Ranged!.ProjectileId!].Projectile!;
        Assert.Equal((10, 14f, 90, 1f), (arrow.Damage, arrow.Speed, arrow.LifetimeTicks, arrow.Height));
        Assert.Equal(Faction.Hero, hero.Faction); Assert.Equal(Faction.Monster, content.Entities["core:arrow_skeleton"].Faction);
        Assert.Equal(10, content.Entities["core:zombie"].Experience); Assert.Equal(.25f, content.Entities["core:food"].Pickup!.HealFraction);
        Assert.True(LevelLoader.Load(File.ReadAllText(Path.Combine(root, "game/levels/mossy_hollow/level.json")), content).Success);
    }
    [Fact]
    public void DefinitionsDefensivelyCopyAndRejectUnsafeOrWrongSlotData()
    {
        MeleeSwing[] swings = [new(6, 6, 12, 12, 1, 60, 1.7f)]; var sword = new WeaponDef("test:sword", swings); swings[0] = default;
        Assert.Equal(12, sword.Swings[0].Damage);
        Assert.Throws<ArgumentException>(() => new WeaponLoadout(ranged: sword));
        Assert.Throws<ArgumentException>(() => new HeroRules(rollTicks: 1));
        Assert.Throws<ArgumentException>(() => new HeroRules(rollInvulnerableLast: 25));
        Assert.Throws<ArgumentException>(() => new ProjectileSpec(1, float.NaN));
        Assert.Throws<ArgumentException>(() => new PickupSpec(PickupKind.Food, healFraction: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSwing(1, 0, 1, 1, 0, 45, 1));
        Assert.False(ContentRegistry.Load(new[] { ("weapons/bad.json", """{"id":"test:bad","kind":"ranged","projectile":"test:arrow","windupTicks":1.5,"recoveryTicks":1,"cooldownTicks":1}""") }).IsValid);
        ContentRegistry missing = ContentRegistry.Load(new[] { ("entities/hero.json", """{"id":"test:hero","kind":"Player","weapons":{"melee":"test:missing"}}""") });
        Assert.Contains(missing.Diagnostics, d => d.Code == DiagnosticCode.CNT1003);
    }

    [Theory]
    [InlineData("\"faction\":\"1\"")]
    [InlineData("\"stats\":{\"xp\":1.5}")]
    [InlineData("\"projectile\":{\"damage\":10,\"speed\":14,\"pierce\":0.5}")]
    [InlineData("\"pickup\":{\"kind\":\"emerald\",\"amount\":1.5}")]
    [InlineData("\"control\":{\"rollTicks\":1.5}")]
    public void AuthoredActionMetadataNeverSilentlyCoercesInvalidValues(string field)
    {
        string json = "{\"id\":\"test:bad\",\"kind\":\"Player\"," + field + "}";
        Assert.False(ContentRegistry.Load(new[] { ("entities/bad.json", json) }).IsValid);
    }
    private static string FindRoot()
    { DirectoryInfo? directory = new(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "specification.md"))) { directory = directory.Parent; } return directory!.FullName; }

    [Fact]
    public void MossyHollowMovementWalkthroughWithoutEnemiesUsesProductionGridAndAStar()
    {
        string root = FindRoot(), folder = Path.Combine(root, "game/content");
        string[] files = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(folder, "index.json")))!;
        ContentRegistry content = ContentRegistry.Load(files.Select(p => (p, File.ReadAllText(Path.Combine(folder, p)))));
        LevelDocument level = LevelLoader.Load(File.ReadAllText(Path.Combine(root, "game/levels/mossy_hollow/level.json")), content).Document!;
        // This M4 movement fixture deliberately exercises the all-gates-open terrain.
        // Authored combat/mission completion remain the M5/M6 walkthrough gates.
        var builder = new ScenarioBuilder(level.Width, level.Height, "m4.mossy_movement");
        for (int z = 0; z < level.Height; z++) { for (int x = 0; x < level.Width; x++) { builder.Cell(x, z, level.GetCell(x, z)); } }
        Vec2 spawn = level.Entities.Single(e => e.Definition.Id == "core:player_spawn").Position;
        Scenario scenario = builder.Entity(content.Entities["core:hero"], spawn).Build(record: true, replayCapacity: 10000);
        foreach (Vec2 point in new Vec2[] { new(19.5f, 34.5f), new(20.5f, 19.5f), new(15.5f, 14.5f), new(16.5f, 12.5f), new(16, 4) })
        { Assert.True(scenario.MoveToward(point, 30), "Could not reach " + point); }
        Assert.Equal(100, scenario.Session.World.Players[0].Health.Current);
        Assert.True(ReplayVerifier.Verify(builder.Build().Session, scenario.Session.Recorder!.Capture()).Success);
    }

    [Fact]
    public void CodeCreatedRulesAndLaunchStateParticipateInDeterministicIdentity()
    {
        EntityDef Make(int damage) => new("test:hero", EntityKind.Player, new(100, 4.5f), weapons: new(new WeaponDef("test:sword", new MeleeSwing[] { new(6, 6, 12, damage, 1, 60, 1.7f) })));
        GameSession a = new ScenarioBuilder(10, 10).Entity(Make(12), new(5, 5)).Build().Session;
        GameSession b = new ScenarioBuilder(10, 10).Entity(Make(24), new(5, 5)).Build().Session;
        Assert.NotEqual(a.Hash(), b.Hash());
        b = new ScenarioBuilder(10, 10).Entity(Make(12), new(5, 5)).Build().Session; Assert.Equal(a.Hash(), b.Hash());
        a.World.Players[0].ActionData.RollCooldown = 1; Assert.NotEqual(a.Hash(), b.Hash());
        a.World.Players[0].ActionData.RollCooldown = 0; Assert.Equal(a.Hash(), b.Hash());
        Entity? arrow = a.World.SpawnProjectile(a.World.Players[0], a.World.ProjectileDefinition("core:arrow_hero"), Vec2.North);
        b.World.SpawnProjectile(b.World.Players[0], b.World.ProjectileDefinition("core:arrow_hero"), Vec2.North);
        Assert.Equal(a.Hash(), b.Hash()); arrow!.ProjectileData.OwnerId = 99; Assert.NotEqual(a.Hash(), b.Hash());
    }
}
