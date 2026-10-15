using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

/// <summary>
/// 武器子类（皮肤）应用层。
///
/// 防崩设计要点（对应 1.x 的崩溃嫌疑点）：
/// 1) **不使用** Utilities.GetPlayerFromIndex(未校验索引)；
/// 2) **不使用** `!.` 强制解引用，所有句柄先判空 + IsValid；
/// 3) **不读取** AttributeManager.Item.ItemDefinitionIndex（原生 econ item view 读取，
///    在 OnEntityCreated 时机可能未就绪）；
/// 4) 唯一的原生调用 AcceptInput("ChangeSubclass") 全程 try/catch；
/// 5) 绝不订阅 OnEntityCreated 去"见到 weapon_* 就动手"，只在玩家**实际持有**武器时应用；
/// 6) 应用后读取 SubclassID 校验是否真的生效，失败只记日志、绝不抛出。
/// </summary>
public static class Weapons
{
    /// <summary>已应用记录："steamId:武器句柄" -> 子类名（幂等，避免重复改子类）。</summary>
    private static readonly Dictionary<string, string> Applied = new(StringComparer.Ordinal);

    private static string Key(ulong steamId, nint handle) => $"{steamId}:{handle}";

    public static void ClearState()
    {
        try
        {
            Applied.Clear();
        }
        catch
        {
            // ignored
        }
    }

    public static void OnPlayerDisconnected(ulong steamId)
    {
        try
        {
            var prefix = $"{steamId}:";
            foreach (var key in Applied.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                Applied.Remove(key);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>延时应用（用于出生/拾取之后等武器真正进入背包）。</summary>
    public static void ScheduleApply(CCSPlayerController? player, float delay)
    {
        try
        {
            if (!Players.IsHuman(player)) return;

            var controller = player!;
            var steamId = controller.SteamID;
            Plugin.Instance.AddTimer(delay, () =>
            {
                try
                {
                    var target = Players.FromSteamId(steamId);
                    if (target == null) return;

                    ApplyAllWeapons(target);
                }
                catch (Exception ex)
                {
                    Utils.LogError($"[Weapons] 延时应用异常 {ex.GetType().Name}: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Weapons] 调度异常 {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 把该玩家已保存的所有皮肤应用到其当前持有的武器上。
    /// 功能3：**没有**为某武器保存皮肤时，若配置了 DefaultSubclass（如 weapon_awp_10round），
    /// 则对该武器应用默认子类；两者都没有则完全不动这把武器。
    /// </summary>
    public static void ApplyAllWeapons(CCSPlayerController? player)
    {
        try
        {
            if (!Players.IsHuman(player)) return;

            var controller = player!;
            var steamId = controller.SteamID;
            Database.LoadPlayerSkins(steamId);

            // 注意：这里不再因为"没有皮肤"提前返回 —— 没皮肤的武器仍要走默认子类
            Database.PlayerSkinCache.TryGetValue(steamId, out var skins);
            var skinCount = skins?.Count ?? 0;
            Utils.Debug($"[Weapons] 玩家 {controller.PlayerName} 已保存皮肤 {skinCount} 条");

            foreach (var weapon in Players.GetWeapons(controller))
            {
                try
                {
                    var weaponBase = GetWeaponBaseName(weapon);
                    if (weaponBase == null) continue;

                    if (skins != null && skins.TryGetValue(weaponBase, out var subclass) && !string.IsNullOrWhiteSpace(subclass))
                    {
                        ApplySubclass(weapon, steamId, weaponBase, subclass, "ApplyAllWeapons");
                        continue;
                    }

                    // 未选皮肤 -> 默认子类（没配置 = 不动）
                    var defaultSubclass = Utils.GetDefaultSubclass(weaponBase);
                    if (defaultSubclass != null)
                        ApplySubclass(weapon, steamId, weaponBase, defaultSubclass, "Default");
                }
                catch (Exception ex)
                {
                    Utils.LogError($"[Weapons] 单把武器应用异常 {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Weapons] ApplyAllWeapons 异常 {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>菜单选择后立即应用（武器可能还没拿到，拿不到就只写库，出生时会自动补上）。</summary>
    public static bool ApplyToHeldWeapon(CCSPlayerController? player, string weaponBase, string subclass)
    {
        try
        {
            if (!Players.IsHuman(player)) return false;
            if (string.IsNullOrWhiteSpace(weaponBase) || string.IsNullOrWhiteSpace(subclass)) return false;

            var steamId = player!.SteamID;
            foreach (var weapon in Players.GetWeapons(player))
            {
                if (GetWeaponBaseName(weapon) != weaponBase) continue;

                return ApplySubclass(weapon, steamId, weaponBase, subclass, "Menu");
            }

            Utils.Debug($"[Weapons] 玩家当前未持有 {weaponBase}，已只写库（出生/拾取后会自动应用）");
            return false;
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Weapons] ApplyToHeldWeapon 异常 {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 还原为该武器的"默认态"：配置了 DefaultSubclass 就应用它（功能3，"默认态也走子类"），
    /// 没配置才回落到原版子类本身。
    /// </summary>
    public static void RestoreToVanilla(CCSPlayerController? player, string weaponBase)
    {
        try
        {
            if (!Players.IsHuman(player)) return;

            var steamId = player!.SteamID;
            var target = Utils.GetDefaultSubclass(weaponBase) ?? weaponBase;

            foreach (var weapon in Players.GetWeapons(player))
            {
                if (GetWeaponBaseName(weapon) != weaponBase) continue;

                ApplySubclass(weapon, steamId, weaponBase, target, target == weaponBase ? "Restore" : "Default");
            }
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Weapons] RestoreToVanilla 异常 {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 武器实体名 -> 配置里使用的基准名。
    /// 注意：**不读 ItemDefinitionIndex**，因此不区分共享实体名的武器：
    /// P2000/USP-S 在实体层都是 weapon_hkp2000，M4A4 是 weapon_m4a1，M4A1-S 是 weapon_m4a1_silencer。
    /// </summary>
    public static string? GetWeaponBaseName(CBasePlayerWeapon? weapon)
    {
        try
        {
            if (weapon == null || !weapon.IsValid) return null;

            var designerName = weapon.DesignerName;
            if (string.IsNullOrWhiteSpace(designerName)) return null;

            // 所有刀在实体层都是 weapon_knife（含 bayonet/karambit/butterfly/...）
            if (designerName.Contains("knife", StringComparison.OrdinalIgnoreCase)) return "weapon_knife";

            return designerName;
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Weapons] 读取武器名失败 {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>读取 m_nSubclassID（CUtlStringToken）。反射 + try/catch，读不到返回 null。</summary>
    public static long? TryGetSubclassToken(CEntityInstance? entity)
    {
        try
        {
            if (entity == null || !entity.IsValid) return null;

            var property = entity.GetType().GetProperty("SubclassID");
            var value = property?.GetValue(entity);
            if (value == null) return null;

            var valueProperty = value.GetType().GetProperty("Value");
            var raw = valueProperty?.GetValue(value);
            return raw == null ? null : Convert.ToInt64(raw);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 唯一原生调用点。幂等 + 前后校验 + 全程 try/catch。
    /// 返回是否"看起来生效"（无法确认时返回 false，但不影响其它逻辑）。
    /// </summary>
    private static bool ApplySubclass(CBasePlayerWeapon weapon, ulong steamId, string weaponBase, string subclass, string origin)
    {
        try
        {
            if (weapon == null || !weapon.IsValid) return false;

            if (steamId == 0) steamId = ResolveSteamId(weapon);

            var key = Key(steamId, weapon.Handle);

            if (Applied.TryGetValue(key, out var already) && string.Equals(already, subclass, StringComparison.Ordinal))
            {
                Utils.Debug($"[{origin}] {weaponBase} 已经是 {subclass}，跳过");
                return true;
            }

            var before = TryGetSubclassToken(weapon);

            weapon.AcceptInput("ChangeSubclass", weapon, weapon, subclass);
            Applied[key] = subclass;

            var after = TryGetSubclassToken(weapon);
            var effective = before.HasValue && after.HasValue && before.Value != after.Value;

            if (Plugin.Instance.DebugEnabled || !effective)
            {
                Utils.Log($"[{origin}] {weaponBase} 应用子类 {subclass}：" +
                          $"subclassToken {before?.ToString() ?? "?"} -> {after?.ToString() ?? "?"}" +
                          (effective ? "" : "（token 未变化，引擎可能未应用该子类）"));
            }

            if (!effective && before.HasValue && after.HasValue)
            {
                // 记录下来但不再重复尝试（避免刷屏/死循环）
                Utils.LogError(origin == "Default"
                    ? $"[Default] 默认子类 {subclass} 未生效（{weaponBase}，token 未变化）。" +
                      "常见原因：资源包 vdata 还没有该子类条目（AcceptInput 静默无效），" +
                      "或子类名拼写不对。等 vdata 加入后重启地图/服务器再验证。"
                    : $"[{origin}] 子类 {subclass} 未生效（{weaponBase}）。" +
                      "若模型/数值都没变，属引擎或资源包问题，与本插件无关。");
            }

            return effective;
        }
        catch (Exception ex)
        {
            Utils.LogError($"[{origin}] AcceptInput(ChangeSubclass, {subclass}) 异常 " +
                           $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 找出持有该武器的玩家。
    /// 采用与线上已验证可用的 OldWeaponSounds 插件相同的安全写法：
    /// weapon.OwnerEntity?.Value -> 用 pawn.Index 与玩家列表比对（不使用任何未校验索引）。
    /// </summary>
    private static ulong ResolveSteamId(CBasePlayerWeapon weapon)
    {
        try
        {
            var owner = weapon.OwnerEntity?.Value;

            // 情况 A：owner 本身就是玩家控制器
            if (owner is CCSPlayerController ownerController && Players.IsHuman(ownerController))
                return ownerController.SteamID;

            // 情况 B：owner 是 pawn -> 用 pawn 的 Index 反查控制器
            if (owner != null)
            {
                foreach (var candidate in Utilities.GetPlayers())
                {
                    if (!Players.IsHuman(candidate)) continue;

                    var pawn = candidate!.PlayerPawn?.Value;
                    if (pawn == null || !pawn.IsValid) continue;
                    if (pawn.Index == owner.Index) return candidate.SteamID;
                }
            }
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Weapons] 解析武器归属失败 {ex.GetType().Name}: {ex.Message}");
        }

        return 0;
    }
}
