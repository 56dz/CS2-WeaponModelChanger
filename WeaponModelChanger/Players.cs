using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

/// <summary>
/// 玩家相关的**安全**辅助。所有索引/句柄使用前都先校验，绝不把未校验的索引传给
/// Utilities.GetPlayerFromIndex（1.x 崩溃嫌疑点之一）。
/// </summary>
public static class Players
{
    private const int MaxPlayerSlots = 64;

    public static bool IsHuman(CCSPlayerController? player)
    {
        try
        {
            return player != null
                   && player.IsValid
                   && !player.IsBot
                   && player.SteamID != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>只从 Utilities.GetPlayers() 拿控制器（不自己造索引）。</summary>
    public static CCSPlayerController? FromSteamId(ulong steamId)
    {
        if (steamId == 0) return null;

        try
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!IsHuman(player)) continue;
                if (player.SteamID == steamId) return player;
            }
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Players] 查找玩家失败 {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    /// <summary>按索引查找，但索引必须先通过范围校验。</summary>
    public static CCSPlayerController? FromIndexSafe(int index)
    {
        if (index < 0 || index >= MaxPlayerSlots) return null;

        try
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!IsHuman(player)) continue;
                if (player.Slot == index) return player;
            }
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Players] 按索引查找失败 {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    /// <summary>拿到玩家当前生效的武器列表（全程判空 + 校验）。</summary>
    public static List<CBasePlayerWeapon> GetWeapons(CCSPlayerController? player)
    {
        var result = new List<CBasePlayerWeapon>();

        try
        {
            if (player == null || !player.IsValid) return result;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return result;

            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return result;

            foreach (var handle in weaponServices.MyWeapons)
            {
                var weapon = handle?.Value;
                if (weapon == null || !weapon.IsValid) continue;

                result.Add(weapon);
            }
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Players] 读取武器列表失败 {ex.GetType().Name}: {ex.Message}");
        }

        return result;
    }
}
