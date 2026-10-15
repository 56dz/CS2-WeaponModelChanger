using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

public static class Utils
{
    private static Plugin Instance => Plugin.Instance;

    public static void Log(string message) => Instance.Logger.LogInformation(message);

    public static void LogError(string message) => Instance.Logger.LogError(message);

    public static void Debug(string message)
    {
        if (Instance.DebugEnabled) Instance.Logger.LogInformation(message);
    }

    /// <summary>把 "weapon_ak47:weapon_skin_001" 拆成 (weapon_ak47, weapon_skin_001)。</summary>
    public static (string WeaponBase, string Subclass)? ParseSpec(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;

        var index = spec.IndexOf(':');
        if (index <= 0 || index >= spec.Length - 1) return null;

        var weaponBase = spec[..index].Trim();
        var subclass = spec[(index + 1)..].Trim();
        if (weaponBase.Length == 0 || subclass.Length == 0) return null;

        return (weaponBase, subclass);
    }

    public static string GetWeaponDisplayName(string weaponBase)
    {
        if (string.IsNullOrEmpty(weaponBase)) return "?";

        return weaponBase switch
        {
            "weapon_m4a1" => "M4A4",
            "weapon_m4a1_silencer" => "M4A1-S",
            "weapon_usp_silencer" => "USP-S",
            "weapon_hkp2000" => "P2000/USP",
            "weapon_mp5sd" => "MP5-SD",
            "weapon_ssg08" => "SSG08",
            "weapon_xm1014" => "XM1014",
            "weapon_hegrenade" => "HE",
            "weapon_smokegrenade" => "烟雾",
            "weapon_incgrenade" => "燃烧",
            _ => weaponBase.Replace("weapon_", "").ToUpperInvariant()
        };
    }

    /// <summary>权限 + 队伍过滤；permissions 为空表示不限制。</summary>
    public static bool HasPermission(CCSPlayerController? player, List<string>? permissions, string? team = "")
    {
        if (player == null || !player.IsValid) return false;

        try
        {
            var required = permissions?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList() ?? new List<string>();

            var hasPermission = required.Count == 0;
            foreach (var permission in required)
            {
                if (permission.StartsWith('@') && AdminManager.PlayerHasPermissions(player, permission))
                {
                    hasPermission = true;
                    break;
                }

                if (permission.StartsWith('#') && AdminManager.PlayerInGroup(player, permission))
                {
                    hasPermission = true;
                    break;
                }
            }

            var t = (team ?? string.Empty).Trim().ToLowerInvariant();
            var isTeamValid = t.Length == 0 || t is "both" or "all"
                              || ((t is "t" or "terrorist") && player.Team == CsTeam.Terrorist)
                              || ((t is "ct" or "counterterrorist") && player.Team == CsTeam.CounterTerrorist);

            return hasPermission && isTeamValid;
        }
        catch (Exception ex)
        {
            LogError($"[HasPermission] 异常 {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static IEnumerable<(string CategoryName, MenuCategory Category)> EnumerateCategories()
    {
        var categories = Instance.Config?.Categories;
        if (categories == null) yield break;

        foreach (var kv in categories)
        {
            if (kv.Value == null) continue;
            yield return (kv.Key, kv.Value);
        }
    }

    public static IEnumerable<Equipment> EnumerateEquipments()
    {
        foreach (var (_, category) in EnumerateCategories())
        {
            foreach (var equipment in category.Equipment)
            {
                if (equipment == null) continue;
                if (ParseSpec(equipment.Weapon) == null) continue;
                yield return equipment;
            }
        }
    }

    public static MenuCategory? FindCategory(Equipment? equipment)
    {
        if (equipment == null) return null;

        foreach (var (_, category) in EnumerateCategories())
        {
            if (category.Equipment.Contains(equipment)) return category;
        }

        return null;
    }

    public static int CountEquipments(Config? config)
    {
        if (config?.Categories == null) return 0;

        var total = 0;
        foreach (var category in config.Categories.Values)
        {
            if (category?.Equipment == null) continue;
            foreach (var equipment in category.Equipment)
            {
                if (ParseSpec(equipment?.Weapon) != null) total++;
            }
        }

        return total;
    }
}
