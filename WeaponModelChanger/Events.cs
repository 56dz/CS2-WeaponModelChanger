using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

public static class Events
{
    private static readonly Plugin Instance = Plugin.Instance;

    public static void Register()
    {
        Instance.RegisterListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
        Instance.RegisterListener<Listeners.OnEntityCreated>(OnEntityCreated);
        Instance.RegisterListener<Listeners.OnMapStart>(OnMapStart);
        Instance.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        Instance.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
    }

    public static void Unregister()
    {
        Instance.RemoveListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
        Instance.RemoveListener<Listeners.OnEntityCreated>(OnEntityCreated);
        Instance.RemoveListener<Listeners.OnMapStart>(OnMapStart);
        Instance.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        Instance.DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
    }

    private static void OnServerPrecacheResources(ResourceManifest manifest) { }

    private static void OnEntityCreated(CEntityInstance entity)
    {
        if (!entity.DesignerName.StartsWith("weapon_")) return;

        Server.NextWorldUpdate(() =>
        {
            if (entity == null || !entity.IsValid) return;

            var weapon = entity.As<CBasePlayerWeapon>();
            if (weapon == null) return;

            Weapons.ProcessEntity(weapon);
        });
    }

    private static void OnMapStart(string mapname)
    {
        Weapons.oldDesignerName.Clear();
    }

    private static HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || player.IsBot) return HookResult.Continue;
        OnPlayerSpawnInternal(player);
        return HookResult.Continue;
    }

    public static void OnPlayerSpawnInternal(CCSPlayerController player)
    {
        Database.LoadPlayerSkins(player.SteamID);
    }

    private static HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;
        Database.OnPlayerDisconnect(player.SteamID);
        return HookResult.Continue;
    }
}
