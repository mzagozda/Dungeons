using System.Text.Json.Serialization;

namespace Dungeons.Game.Persistence;

[JsonSerializable(typeof(GameSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(PlayerProfile))]
internal partial class ProfileJsonContext : JsonSerializerContext;
