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

    /// <summary>
    /// 基准武器 -> "未选皮肤时"使用的默认子类（功能3，例如 weapon_awp -> weapon_awp_10round）。
    /// 由 <see cref="RebuildDefaultSubclasses"/> 在配置解析后构建；
    /// 条目级 Equipment.DefaultSubclass 优先于顶层 Config.DefaultSubclasses。
    /// </summary>
    public static readonly Dictionary<string, string> DefaultSubclasses = new(StringComparer.Ordinal);

    /// <summary>按当前配置重建默认子类映射；返回条目数（Debug 日志用）。</summary>
    public static int RebuildDefaultSubclasses(Config? config)
    {
        try
        {
            DefaultSubclasses.Clear();
            if (config == null) return 0;

            // 1) 顶层字典
            if (config.DefaultSubclasses != null)
            {
                foreach (var kv in config.DefaultSubclasses)
                {
                    var weaponBase = (kv.Key ?? string.Empty).Trim();
                    var subclass = (kv.Value ?? string.Empty).Trim();
                    if (weaponBase.Length == 0 || subclass.Length == 0) continue;

                    DefaultSubclasses[weaponBase] = subclass;
                }
            }

            // 2) 条目级（更具体：覆盖顶层；同一武器取第一个非空条目）
            var claimedByEntry = new HashSet<string>(StringComparer.Ordinal);
            foreach (var equipment in EnumerateEquipments())
            {
                var spec = ParseSpec(equipment.Weapon);
                if (spec == null) continue;

                var subclass = (equipment.DefaultSubclass ?? string.Empty).Trim();
                if (subclass.Length == 0) continue;
                if (!claimedByEntry.Add(spec.Value.WeaponBase)) continue;

                DefaultSubclasses[spec.Value.WeaponBase] = subclass;
            }

            return DefaultSubclasses.Count;
        }
        catch (Exception ex)
        {
            LogError($"[Config] 构建默认子类映射异常 {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>取该武器的默认子类；没配置返回 null（= 完全不动这把武器）。</summary>
    public static string? GetDefaultSubclass(string? weaponBase)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(weaponBase)) return null;
            if (DefaultSubclasses.TryGetValue(weaponBase.Trim(), out var subclass) &&
                !string.IsNullOrWhiteSpace(subclass))
                return subclass.Trim();
        }
        catch (Exception ex)
        {
            LogError($"[Config] 读取默认子类异常 {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    /// <summary>基准武器 -> 子类生效后允许上补到的弹匣上限（v2.0.2 回退方案，首选读 VData）。</summary>
    public static readonly Dictionary<string, int> RefillClips = new(StringComparer.Ordinal);

    /// <summary>按当前配置重建弹匣刷新映射（条目级覆盖顶层）。返回条目数。</summary>
    public static int RebuildRefillClips(Config? config)
    {
        try
        {
            RefillClips.Clear();
            if (config == null) return 0;

            // 1) 顶层字典
            if (config.RefillClip != null)
            {
                foreach (var kv in config.RefillClip)
                {
                    var weaponBase = (kv.Key ?? string.Empty).Trim();
                    if (weaponBase.Length == 0 || kv.Value <= 0) continue;

                    RefillClips[weaponBase] = kv.Value;
                }
            }

            // 2) 条目级（更具体：覆盖顶层；同一武器取第一个 > 0 的条目）
            var claimedByEntry = new HashSet<string>(StringComparer.Ordinal);
            foreach (var equipment in EnumerateEquipments())
            {
                var spec = ParseSpec(equipment.Weapon);
                if (spec == null) continue;
                if (equipment.RefillClip <= 0) continue;
                if (!claimedByEntry.Add(spec.Value.WeaponBase)) continue;

                RefillClips[spec.Value.WeaponBase] = equipment.RefillClip;
            }

            return RefillClips.Count;
        }
        catch (Exception ex)
        {
            LogError($"[Config] 构建弹匣刷新映射异常 {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>取该武器配置的弹匣刷新上限；没配置返回 null（= 只按 VData 走）。</summary>
    public static int? GetRefillClip(string? weaponBase)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(weaponBase)) return null;
            if (RefillClips.TryGetValue(weaponBase.Trim(), out var value) && value > 0) return value;
        }
        catch (Exception ex)
        {
            LogError($"[Config] 读取弹匣刷新配置异常 {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 武器显示名（菜单一级 = 中文武器名，二级 = 中文武器名 - 皮肤名）。
    ///
    /// 优先走 CSS 本地化：<c>Localizer["weapon.ak47"]</c>，键在 lang/zh-Hans.json 与
    /// lang/en.json 里各有一份；命中则直接返回。
    /// 兜底（lang 缺键/加载失败时）：沿用 v2.0.0 的硬编码简写，最后退回大写原名。
    /// </summary>
    public static string GetWeaponDisplayName(string weaponBase)
    {
        if (string.IsNullOrEmpty(weaponBase)) return "?";

        // 1) 本地化优先
        try
        {
            var key = "weapon." + (weaponBase.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)
                ? weaponBase["weapon_".Length..]
                : weaponBase);

            var translated = Instance.Localizer[key];
            if (!string.IsNullOrWhiteSpace(translated) && !string.Equals(translated, key, StringComparison.Ordinal))
                return translated;
        }
        catch
        {
            // 本地化失败走兜底
        }

        // 2) 兜底：硬编码（历史行为，保留以防 lang 文件缺失）
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
