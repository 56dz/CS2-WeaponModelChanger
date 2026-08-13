using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;

public partial class Plugin : BasePlugin, IPluginConfig<Config>
{
    public override string ModuleName => "WeaponModelChanger";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "56dz";
    public static Plugin Instance { get; set; } = new();

    public override void Load(bool hotReload)
    {
        Instance = this;
        Database.Initialize();
        Events.Register();
        Commands.Register();

        if (hotReload)
        {
            Server.NextWorldUpdate(() =>
            {
                foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.SteamID != 0))
                {
                    Events.OnPlayerSpawnInternal(player);
                }
            });
        }
    }

    public override void Unload(bool hotReload)
    {
        Events.Unregister();
        Commands.Unregister();
    }

    public Config Config { get; set; } = new();

    public void OnConfigParsed(Config config)
    {
        Config = config;
        Config.Prefix = StringExtensions.ReplaceColorTags(Config.Prefix);
    }
}
