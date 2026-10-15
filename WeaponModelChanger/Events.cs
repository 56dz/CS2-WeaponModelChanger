using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

public static class Events
{
    private static Plugin Instance => Plugin.Instance;

    public static void Register()
    {
        try
        {
            Instance.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            Instance.RegisterEventHandler<EventItemPickup>(OnItemPickup);
            Instance.RegisterEventHandler<EventRoundStart>(OnRoundStart);
            Instance.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            Instance.RegisterListener<Listeners.OnMapStart>(OnMapStart);
            // 功能1：服务端资源清单登记（1.x/2.0.0 一直空着的钩子，皮肤模型从未进清单）
            Instance.RegisterListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Events] 注册失败 {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        try
        {
            Instance.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            Instance.DeregisterEventHandler<EventItemPickup>(OnItemPickup);
            Instance.DeregisterEventHandler<EventRoundStart>(OnRoundStart);
            Instance.DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            Instance.RemoveListener<Listeners.OnMapStart>(OnMapStart);
            Instance.RemoveListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Events] 注销失败 {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void OnMapStart(string mapName)
    {
        try
        {
            Weapons.ClearState();
            Database.PlayerSkinCache.Clear();
            Utils.Debug($"[Events] 地图开始 {mapName}，已清空换装状态");
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Events] OnMapStart 异常 {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 功能1：服务端资源清单登记（地图加载/服务端首次启动时触发）。
    ///
    /// 背景：客户端 console.log 实测报
    /// "RESOURCE_TYPE_MODEL resource ... requested but not resident (Missing from a manifest?)"
    /// —— 皮肤模型在两边 VPK 里都完整存在，但从未被登记进服务端资源清单。
    /// 1.x 沿袭下来的这个钩子一直是**空函数**，这里补上真正的登记逻辑。
    ///
    /// 路径格式：与 vdata 的 m_szWorldModel 完全一致 —— 带 .vmdl 扩展名、不带 _c
    /// （实测 resourceinfo 依赖项即显示为 weapons/models/glock_doro/glock_doro.vmdl）。
    /// </summary>
    private static void OnServerPrecacheResources(ResourceManifest? manifest)
    {
        var ok = 0;
        var failed = 0;

        try
        {
            if (manifest == null)
            {
                Utils.LogError("[Precache] manifest 为空，跳过资源登记");
                return;
            }

            foreach (var path in SkinModelPaths.AllModelPaths)
            {
                try
                {
                    manifest.AddResource(path);
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Utils.LogError($"[Precache] 登记资源失败 {path} {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (failed > 0)
                Utils.LogError($"[Precache] 资源清单登记完成：成功 {ok} 条，失败 {failed} 条（共 {SkinModelPaths.ModelPathCount} 条去重模型）");
            else
                Utils.Debug($"[Precache] 已向服务端资源清单登记皮肤模型 {ok} 条（{SkinModelPaths.SkinCount} 个皮肤 / {SkinModelPaths.ModelPathCount} 条去重路径）");
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Precache] OnServerPrecacheResources 异常 {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>出生：读库 + 延时应用（此时武器还没发到手里，所以必须延后）。</summary>
    private static HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        try
        {
            var player = @event.Userid;
            if (!Players.IsHuman(player)) return HookResult.Continue;

            var controller = player!;
            var steamId = controller.SteamID;
            Database.LoadPlayerSkins(steamId);

            // 1 秒内多次尝试，覆盖"武器还没到手"的时间差
            Weapons.ScheduleApply(controller, 0.5f);
            Weapons.ScheduleApply(controller, 1.5f);
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Events] OnPlayerSpawn 异常 {ex.GetType().Name}: {ex.Message}");
        }

        return HookResult.Continue;
    }

    /// <summary>拾到武器：稍后应用（只对玩家真正持有的武器动手）。</summary>
    private static HookResult OnItemPickup(EventItemPickup @event, GameEventInfo info)
    {
        try
        {
            var player = @event.Userid;
            if (!Players.IsHuman(player)) return HookResult.Continue;

            var steamId = player!.SteamID;
            Plugin.Instance.AddTimer(0.1f, () =>
            {
                try
                {
                    var target = Players.FromSteamId(steamId);
                    if (target != null) Weapons.ApplyAllWeapons(target);
                }
                catch (Exception ex)
                {
                    Utils.LogError($"[Events] 拾取后应用异常 {ex.GetType().Name}: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Events] OnItemPickup 异常 {ex.GetType().Name}: {ex.Message}");
        }

        return HookResult.Continue;
    }

    private static HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        try
        {
            // 新回合武器会重新发放，延后统一补一遍
            Server.NextWorldUpdate(() =>
            {
                try
                {
                    foreach (var player in Utilities.GetPlayers())
                    {
                        if (!Players.IsHuman(player)) continue;
                        Weapons.ScheduleApply(player, 0.75f);
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogError($"[Events] 回合开始补应用异常 {ex.GetType().Name}: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Events] OnRoundStart 异常 {ex.GetType().Name}: {ex.Message}");
        }

        return HookResult.Continue;
    }

    private static HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        try
        {
            var player = @event.Userid;
            if (player == null) return HookResult.Continue;

            var steamId = player.SteamID;
            Database.OnPlayerDisconnect(steamId);
            Weapons.OnPlayerDisconnected(steamId);
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Events] OnPlayerDisconnect 异常 {ex.GetType().Name}: {ex.Message}");
        }

        return HookResult.Continue;
    }
}
