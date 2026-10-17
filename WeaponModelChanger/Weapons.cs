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
    /// <summary>
    /// 已应用记录："steamId:武器句柄" -> (子类名, 应用后读到的 subclassToken)。
    ///
    /// ⚠️ 重要教训（v2.0.3 修的真实 bug）：
    /// 引擎会**复用实体句柄**。原来这里只存子类名，重进游戏/实体重建后新武器拿到
    /// 旧句柄时会被误判成"已经应用过"而跳过，结果新实体仍是默认子类 —— 现象就是
    /// 用户报的"退出重进后武器皮肤没加载、匕首模型没加载，死后下一回合才全部正常"。
    /// 所以现在连**应用后读到的 token** 一起存，跳过前必须回读实体当前 token 比对。
    /// </summary>
    private static readonly Dictionary<string, AppliedEntry> Applied = new(StringComparer.Ordinal);

    /// <summary>一条已应用记录：子类名 + 应用成功后从实体读到的 subclassToken（读不到为 null）。</summary>
    private readonly record struct AppliedEntry(string Subclass, long? Token);

    private static string Key(ulong steamId, nint handle) => $"{steamId}:{handle}";

    /// <summary>
    /// 每个连接已经强制刷新过的"轮次"计数（steamId -> 次数），断开/换图时清空。
    /// 用计数而不是布尔锁：客户端挂载 addon 的时机不确定，多次出生各刷一次能明显提高
    /// 命中率；超过 MaxRefreshPerConnect 次就停手，避免反复制造模型闪动。
    /// </summary>
    private static readonly Dictionary<ulong, int> RefreshRounds = new();

    /// <summary>每次连接最多强制刷新几轮。</summary>
    private const int MaxRefreshPerConnect = 3;

    public static void ClearState()
    {
        try
        {
            Applied.Clear();
            RefreshRounds.Clear();
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
            RefreshRounds.Remove(steamId);

            var prefix = $"{steamId}:";
            foreach (var key in Applied.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                Applied.Remove(key);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// v2.0.3：进服后强制刷新一次子类（每个连接只做一次）。
    ///
    /// 背景（实测日志定位）：玩家"退出重进"时，服务端的武器实体**没有重建**，
    /// 已应用的子类值一直没变；而新客户端是在连接快照里第一次收到这个子类值的，
    /// 那时它还没挂载完资源包 vdata，模型解析回退成原版模型。之后子类没有任何"变更"，
    /// 客户端就再也不会重新解析模型 —— 现象就是"重进后皮肤/匕首模型没加载，
    /// 死后下一回合（实体真正重建）才全部正常"。
    ///
    /// 做法：对该玩家当前持有、且我们已应用过皮肤子类的武器，
    /// **先切回原版子类、稍后（0.25s）再切回皮肤子类**，制造两次真实的子类变更事件，
    /// 让已经挂载好 addon 的客户端重新解析模型。全程 try/catch + 有效性校验。
    /// </summary>
    public static void ForceRefreshAll(CCSPlayerController? player)
    {
        try
        {
            if (!Players.IsHuman(player)) return;

            var controller = player!;
            var steamId = controller.SteamID;
            if (steamId == 0) return;

            RefreshRounds.TryGetValue(steamId, out var rounds);
            if (rounds >= MaxRefreshPerConnect)
            {
                Utils.Debug($"[Refresh] 玩家 {controller.PlayerName} 本次连接已强制刷新 {rounds} 轮，跳过");
                return;
            }
            RefreshRounds[steamId] = rounds + 1;

            Database.LoadPlayerSkins(steamId);
            Database.PlayerSkinCache.TryGetValue(steamId, out var skins);

            var queued = 0;

            foreach (var weapon in Players.GetWeapons(controller))
            {
                try
                {
                    if (weapon == null || !weapon.IsValid) continue;

                    var weaponBase = GetWeaponBaseName(weapon);
                    if (weaponBase == null) continue;

                    // 目标子类：玩家选的皮肤，否则配置的默认子类
                    string? target = null;
                    if (skins != null && skins.TryGetValue(weaponBase, out var skin) && !string.IsNullOrWhiteSpace(skin))
                        target = skin;
                    else
                        target = Utils.GetDefaultSubclass(weaponBase);

                    if (string.IsNullOrWhiteSpace(target)) continue;
                    if (string.Equals(target, weaponBase, StringComparison.Ordinal)) continue; // 本来就是原版

                    // 不该用 Applied 缓存做守卫：断线时已按 steamId 清空，用它会导致
                    // "重进后一条都刷不到" —— 恰好把要修的场景排除了。
                    // 只要这台武器"应该有非原版子类"，就刷新它对不对都由引擎状态决定。
                    var liveToken = TryGetSubclassToken(weapon);
                    if (!liveToken.HasValue) continue; // 实体不可读（正在销毁等）→ 跳过

                    var localWeapon = weapon;
                    var localTarget = target;
                    var localBase = weaponBase;
                    var handle = weapon.Handle;

                    // 目标子类对应的 token：先按"皮肤子类"解析一次拿基准（用于兜底复核比对）。
                    // 这一步不改实体，只是把当前皮肤状态下的 token 记下来。
                    var skinTokenBeforeReset = liveToken;

                    // ① 先切回原版，制造一次真实的变更（异步生效，见 WaitForToken）
                    ApplySubclass(localWeapon, steamId, localBase, localBase, "RefreshReset", force: true);

                    // ② reset 生效后再切回皮肤子类。
                    //    v2.0.3 的教训：引擎的 ChangeSubclass 是**异步**的，AcceptInput 只入队，
                    //    实际 token 变化要等后面若干 tick。原来固定 0.25s 后直接 apply，
                    //    在武器已稳定存在时会和尚未完成的 reset 撞车被引擎吞掉
                    //    （日志实证：08:08:34.744 reset 成功 -> .756 apply 却 2923023425 -> 2923023425）。
                    //    改为**轮询回读到 reset 的 token 变了**才继续，彻底去掉时序赌博。
                    WaitForToken(localWeapon, handle, resetFrom: liveToken, timeoutSeconds: 2.0f, then: () =>
                    {
                        if (!localWeapon.IsValid) return;

                        // ③ 应用目标皮肤子类；引擎可能吞掉偶发的切换，轮询确认 + 有限次重试。
                        //    先读 apply 前的 token，再发切换，然后等 token 变化。
                        var tokenBeforeApply = TryGetSubclassToken(localWeapon);
                        ApplySubclass(localWeapon, steamId, localBase, localTarget, "RefreshApply", force: true);

                        WaitForToken(localWeapon, handle, resetFrom: null, timeoutSeconds: 2.0f,
                                     then: () =>
                        {
                            try
                            {
                                if (!localWeapon.IsValid) return;

                                var owner = Players.FromSteamId(steamId);
                                if (owner == null) return;

                                var w = Players.GetWeapons(owner).FirstOrDefault(x => x != null && x.IsValid && x.Handle == handle);
                                if (w == null) return; // 已经不在身上了

                                var cur = TryGetSubclassToken(w);
                                if (cur.HasValue && cur.Value == skinTokenBeforeReset.Value)
                                    return; // 已回到目标皮肤子类

                                // 补应用：cross-tick，且每次切换之间都等待引擎落地
                                Utils.Log($"[Refresh] 兜底复核：{localBase} 未停在 {localTarget}" +
                                          $"（当前 token {cur?.ToString() ?? "?"}），补应用一次");
                                ApplySubclass(w, steamId, localBase, localTarget, "RefreshVerify", force: true);
                            }
                            catch (Exception ex)
                            {
                                Utils.LogError($"[Refresh] 兜底复核异常 {ex.GetType().Name}: {ex.Message}");
                            }
                        }, expectDifferentFrom: tokenBeforeApply);
                    });

                    queued++;
                }
                catch (Exception ex)
                {
                    Utils.LogError($"[Refresh] 单把武器刷新异常 {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (queued > 0)
                Utils.Log($"[Refresh] 玩家 {controller.PlayerName} 已排队强制刷新 {queued} 把武器的子类" +
                          $"（第 {RefreshRounds[steamId]}/{MaxRefreshPerConnect} 轮，修复重进后模型不加载）");
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Refresh] ForceRefreshAll 异常 {ex.GetType().Name}: {ex.Message}");
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
    /// 轮询等待引擎把 ChangeSubclass 真正落地，落地后再执行 <paramref name="then"/>。
    ///
    /// 为什么必须轮询：`AcceptInput("ChangeSubclass")` 是**异步**的 —— 调用只把切换入队，
    /// m_nSubclassID 要等引擎后续 tick 才更新。v2.0.3 用固定 0.25s 延时去"等"，
    /// 在武器实体已稳定存在时会被引擎吞掉（日志实证：reset 与 apply 间隔 12ms，
    /// apply 的 token 原地不动 2923023425 -> 2923023425），玩家的皮肤就一直不加载，
    /// 只有死亡重生换新实体后才恢复。
    ///
    /// 判据二选一：
    ///  - <paramref name="resetFrom"/> 有值：等到 token **不再是** resetFrom（= reset 已落地）
    ///  - <paramref name="expectDifferentFrom"/> 有值：等到 token **不再等于**该值（= apply 已落地）
    /// 超时（默认 2s）则放弃等待，由调用方后续兜底复核处理。
    /// </summary>
    private static void WaitForToken(CEntityInstance? entity, nint handle,
                                     long? resetFrom, float timeoutSeconds, Action then,
                                     long? expectDifferentFrom = null)
    {
        var deadline = timeoutSeconds;
        var elapsed = 0f;
        const float step = 0.05f;

        void Poll()
        {
            try
            {
                elapsed += step;

                // 实体失效（换图/武器被移除）→ 放弃，交给兜底层
                if (entity == null || !entity.IsValid)
                {
                    Utils.Debug($"[WaitForToken] 实体已失效（handle {handle}），放弃等待");
                    return;
                }

                var token = TryGetSubclassToken(entity);

                var done = false;
                if (resetFrom.HasValue)
                    done = token.HasValue && token.Value != resetFrom.Value;
                else if (expectDifferentFrom.HasValue)
                    done = token.HasValue && token.Value != expectDifferentFrom.Value;

                if (done)
                {
                    Utils.Debug($"[WaitForToken] 落地（handle {handle}，token {token?.ToString() ?? "?"}，" +
                                $"耗时 {elapsed:F2}s）");
                    then();
                    return;
                }

                if (elapsed >= deadline)
                {
                    Utils.LogError($"[WaitForToken] 等待超时 {deadline:F1}s（handle {handle}，" +
                                   $"当前 token {token?.ToString() ?? "?"}），交给兜底复核");
                    then();
                    return;
                }

                Plugin.Instance.AddTimer(step, Poll);
            }
            catch (Exception ex)
            {
                Utils.LogError($"[WaitForToken] 异常 {ex.GetType().Name}: {ex.Message}");
            }
        }

        Plugin.Instance.AddTimer(step, Poll);
    }

    /// <summary>
    /// 唯一原生调用点。幂等 + 前后校验 + 全程 try/catch。
    /// 返回是否"看起来生效"（无法确认时返回 false，但不影响其它逻辑）。
    ///
    /// force=true 时**跳过幂等缓存**，强制发一次 ChangeSubclass
    /// （ForceRefreshAll 的"先切回原版再切回皮肤"需要真实变更，不能被缓存吞掉）。
    /// </summary>
    private static bool ApplySubclass(CBasePlayerWeapon weapon, ulong steamId, string weaponBase, string subclass, string origin, bool force = false)
    {
        try
        {
            if (weapon == null || !weapon.IsValid) return false;

            if (steamId == 0) steamId = ResolveSteamId(weapon);

            var key = Key(steamId, weapon.Handle);

            // 跳过前必须回读实体当前 token 与缓存比对（句柄会被复用，不能只信缓存）。
            if (!force && Applied.TryGetValue(key, out var already) &&
                string.Equals(already.Subclass, subclass, StringComparison.Ordinal))
            {
                var liveToken = TryGetSubclassToken(weapon);

                if (already.Token.HasValue && liveToken.HasValue)
                {
                    if (already.Token.Value == liveToken.Value)
                    {
                        Utils.Debug($"[{origin}] {weaponBase} 已是 {subclass}（token {liveToken.Value} 一致），跳过");
                        return true;
                    }

                    // 句柄复用 / 实体被重建：缓存不可信，重新应用
                    Utils.Debug($"[{origin}] {weaponBase} 缓存命中 {subclass} 但实体 token 不一致" +
                                $"（缓存 {already.Token.Value} / 实体 {liveToken.Value}），重新应用");
                }
                else
                {
                    // 有一边读不到 token（拿不准）→ 维持旧的保守行为，避免每次调用都重发
                    Utils.Debug($"[{origin}] {weaponBase} 已是 {subclass}（token 不可读，按缓存跳过）");
                    return true;
                }
            }

            var before = TryGetSubclassToken(weapon);
            var beforeMax = TryGetMaxClip1(weapon); // 切换前的 VData 弹匣上限（用于"只在上限真提高时补"）

            weapon.AcceptInput("ChangeSubclass", weapon, weapon, subclass);

            var after = TryGetSubclassToken(weapon);
            Applied[key] = new AppliedEntry(subclass, after);

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

            // 子类真的生效了才刷新弹匣（v2.0.2：购买时按旧 VData 填的 5 发，
            // 切到 10 发子类后 m_iClip1 不会自动重算，换一次弹才会补满）
            if (effective)
                TryRefillClip(weapon, weaponBase, subclass, beforeMax, origin);

            return effective;
        }
        catch (Exception ex)
        {
            Utils.LogError($"[{origin}] AcceptInput(ChangeSubclass, {subclass}) 异常 " +
                           $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>读切换后的 VData 弹匣上限（CBasePlayerWeaponVData.m_iMaxClip1）。失败返回 null。</summary>
    public static int? TryGetMaxClip1(CBasePlayerWeapon? weapon)
    {
        try
        {
            if (weapon == null || !weapon.IsValid) return null;

            var vdata = weapon.VData; // CSS: CBasePlayerWeapon.VData -> GetVData<CBasePlayerWeaponVData>()
            if (vdata == null) return null;

            return vdata.MaxClip1; // ref int 读 CBasePlayerWeaponVData.m_iMaxClip1
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读当前弹匣（CBasePlayerWeapon.m_iClip1）。失败返回 null。</summary>
    public static int? TryGetClip1(CBasePlayerWeapon? weapon)
    {
        try
        {
            if (weapon == null || !weapon.IsValid) return null;
            return weapon.Clip1;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 写弹匣 + 标记网络字段变更 + 回读校验。全程 try/catch，任何一步失败都返回 false。
    /// m_iClip1 是网络化字段，写完必须 SetStateChanged 才会同步到客户端 HUD。
    /// </summary>
    private static bool TryWriteClip1(CBasePlayerWeapon weapon, int value)
    {
        try
        {
            if (weapon == null || !weapon.IsValid) return false;

            weapon.Clip1 = value; // ref 返回属性，直写 schema 内存（不碰引擎签名/gamedata）

            try
            {
                Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1", 0);
            }
            catch (Exception ex)
            {
                Utils.LogError($"[Clip] SetStateChanged(m_iClip1) 异常 {ex.GetType().Name}: {ex.Message}");
            }

            var readback = weapon.Clip1;
            return readback == value;
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Clip] 写入 m_iClip1={value} 异常 {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 子类生效后刷新弹匣（v2.0.2）。
    ///
    /// 规则（安全）：
    /// 1) 目标上限 = max(VData 切换后的 m_iMaxClip1, 配置 RefillClip[weapon])，必须 > 0；
    ///    VData 读不到才只用配置（配置也没配就完全不动）。
    /// 2) **只在子类真的提高了上限时补**（beforeMax &lt; target）：换皮肤/选皮肤时上限没变
    ///    （如 AK 30 -> 30）一律不补，避免"换皮肤白嫖子弹"的观感。
    /// 3) 只升不降：当前 clip &gt;= 目标就什么都不做；clip &lt; 目标才上补到目标。
    /// 4) 只在 ApplySubclass 那一刻执行一次（复用 Applied 幂等缓存，不挂在任何 tick/换弹/开火）。
    /// </summary>
    private static void TryRefillClip(CBasePlayerWeapon weapon, string weaponBase, string subclass, int? beforeMax, string origin)
    {
        try
        {
            if (weapon == null || !weapon.IsValid) return;

            var afterMax = TryGetMaxClip1(weapon);
            var configured = Utils.GetRefillClip(weaponBase);

            int? target = null;
            if (afterMax.HasValue && afterMax.Value > 0) target = afterMax.Value;
            if (configured.HasValue && configured.Value > 0 && (!target.HasValue || configured.Value > target.Value))
                target = configured.Value;

            if (!target.HasValue)
            {
                Utils.Debug($"[{origin}] {weaponBase} 无弹匣上限可读（VData 为空且未配置 RefillClip），跳过刷新");
                return;
            }

            // 只在子类真的提高了上限时补（beforeMax 读不到时按配置补 —— 回退语义）
            if (beforeMax.HasValue && beforeMax.Value > 0 && target.Value <= beforeMax.Value)
            {
                Utils.Debug($"[{origin}] {weaponBase} 弹匣上限未提高（{beforeMax.Value} -> {target.Value}），不刷新");
                return;
            }

            var clip = TryGetClip1(weapon);
            if (!clip.HasValue || clip.Value < 0)
            {
                Utils.Debug($"[{origin}] {weaponBase} 当前弹匣读不到（{clip?.ToString() ?? "?"}），跳过刷新");
                return;
            }

            if (clip.Value >= target.Value) return; // 只升不降

            var from = clip.Value;
            if (TryWriteClip1(weapon, target.Value))
            {
                Utils.Debug($"[Clip] {weaponBase} 弹匣 {from} -> {target.Value}（子类 {subclass}，origin={origin}）");
            }
            else
            {
                Utils.LogError($"[Clip] {weaponBase} 弹匣刷新失败：{from} -> {target.Value}（子类 {subclass}）回读不一致");
            }
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Clip] {weaponBase} 弹匣刷新异常 {ex.GetType().Name}: {ex.Message}");
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
