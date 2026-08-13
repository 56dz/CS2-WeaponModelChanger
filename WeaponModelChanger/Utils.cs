using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Linq;

public static class Utils
{
    private static readonly Plugin Instance = Plugin.Instance;

    public static void LogError(string message) => Instance.Logger.LogError(message);

    public static string GetWeaponDisplayName(string weaponBase)
    {
        return weaponBase switch
        {
            "weapon_m4a1" => "M4A4",
            "weapon_m4a1_silencer" => "M4A1-S",
            "weapon_usp_silencer" => "USP-S",
            "weapon_hkp2000" => "P2000",
            "weapon_mp5sd" => "MP5-SD",
            _ => weaponBase.Replace("weapon_", "").ToUpper()
        };
    }

    public static bool HasPermission(CCSPlayerController player, List<string> permissions, string team = "")
    {
        bool requireCheck = permissions != null && permissions.Any(p => !string.IsNullOrWhiteSpace(p));
        bool hasPermission = !requireCheck;

        if (requireCheck)
        {
            foreach (string permission in permissions!)
            {
                if (!string.IsNullOrWhiteSpace(permission) && permission.StartsWith("@") && AdminManager.PlayerHasPermissions(player, permission))
                {
                    hasPermission = true;
                    break;
                }
                if (!string.IsNullOrWhiteSpace(permission) && permission.StartsWith("#") && AdminManager.PlayerInGroup(player, permission))
                {
                    hasPermission = true;
                    break;
                }
            }
        }

        team = (team ?? string.Empty).ToLower();
        bool isTeamValid = ((team == "t" || team == "terrorist") && player.Team == CsTeam.Terrorist) ||
                           ((team == "ct" || team == "counterterrorist") && player.Team == CsTeam.CounterTerrorist) ||
                           string.IsNullOrEmpty(team) || team == "both" || team == "all";

        return hasPermission && isTeamValid;
    }

    public static IEnumerable<(string fullPath, MenuCategory category)> EnumerateCategories()
    {
        foreach (var kv in Instance.Config.Categories)
        {
            yield return (kv.Key, kv.Value);
        }
    }


    public static HashSet<string> GetAllWeaponBases()
    {
        HashSet<string> bases = new();
        foreach (var (_, category) in EnumerateCategories())
        {
            foreach (var eq in category.Equipment)
            {
                if (!string.IsNullOrEmpty(eq.Weapon))
                {
                    var parts = eq.Weapon.Split(':');
                    if (parts.Length > 0) bases.Add(parts[0]);
                }
            }
        }
        return bases;
    }

    public static IEnumerable<Equipment> GetEquipmentsByWeaponBase(string weaponBase)
    {
        foreach (var (_, category) in EnumerateCategories())
        {
            foreach (var eq in category.Equipment)
            {
                if (!string.IsNullOrEmpty(eq.Weapon) && eq.Weapon.StartsWith(weaponBase + ":"))
                {
                    yield return eq;
                }
            }
        }
    }

    public static MenuCategory? FindCategoryByEquipment(Equipment targetEq)
    {
        foreach (var (_, category) in EnumerateCategories())
        {
            if (category.Equipment.Contains(targetEq)) return category;
        }
        return null;
    }
}
