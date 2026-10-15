using CounterStrikeSharp.API.Core;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Interface;
using CS2MenuManager.API.Menu;

/// <summary>
/// WASD 菜单（CS2MenuManager 1.0.43）。
///
/// 结构：主菜单（该玩家有权限的武器列表） -> 子菜单（该武器的皮肤列表 + 默认模型）。
/// WASD/滚轮选择，E（或配置的键）确认，Q/退出键返回/关闭。
/// </summary>
public static class Menu
{
    private static Plugin Instance => Plugin.Instance;

    /// <summary>按配置创建菜单。类型名来自 config 的 Menu.MenuType（默认 WasdMenu）。</summary>
    private static IMenu CreateMenu(string title)
    {
        var type = Instance.Config.Menu.ResolveMenuType();

        return type.ToLowerInvariant() switch
        {
            "chatmenu" => new ChatMenu(title, Instance),
            "centerhtmlmenu" => new CenterHtmlMenu(title, Instance),
            "consolemenu" => new ConsoleMenu(title, Instance),
            _ => new WasdMenu(title, Instance),
        };
    }

    /// <summary>打开主菜单（!cw / css_cw）。</summary>
    public static void Open(CCSPlayerController? player)
    {
        try
        {
            if (!Players.IsHuman(player)) return;

            var controller = player!;

            if (!Utils.HasPermission(controller, Instance.Config.Menu.Permission, Instance.Config.Menu.Team))
            {
                Print(controller, Instance.Localizer["chat<nopermission>"]);
                return;
            }

            Database.LoadPlayerSkins(controller.SteamID);

            var menu = BuildMainMenu(controller);
            menu.Display(controller, 0);
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Menu] 打开菜单异常 {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IMenu BuildMainMenu(CCSPlayerController player)
    {
        var menu = CreateMenu("武器皮肤菜单");

        try
        {
            ConfigureWasdMenu(menu);

            // 按武器归组（保持配置文件里的出现顺序）
            var grouped = new List<(string WeaponBase, List<Equipment> Items)>();

            foreach (var equipment in Utils.EnumerateEquipments())
            {
                var spec = Utils.ParseSpec(equipment.Weapon);
                if (spec == null) continue;

                var category = Utils.FindCategory(equipment);
                if (category == null) continue;

                // 分类权限 + 具体条目权限/队伍，双重过滤
                if (!Utils.HasPermission(player, category.Permission, category.Team)) continue;
                if (!Utils.HasPermission(player, equipment.Permission, equipment.Team)) continue;

                var weaponBase = spec.Value.WeaponBase;
                var slot = grouped.FirstOrDefault(g => g.WeaponBase == weaponBase);
                if (slot.Items == null)
                {
                    slot = (weaponBase, new List<Equipment>());
                    grouped.Add(slot);
                }

                slot.Items.Add(equipment);
            }

            if (grouped.Count == 0)
            {
                menu.AddItem("没有可用皮肤", (_, _) => { });
                return menu;
            }

            Database.PlayerSkinCache.TryGetValue(player.SteamID, out var skins);

            foreach (var (weaponBase, items) in grouped)
            {
                var displayName = Utils.GetWeaponDisplayName(weaponBase);

                var current = skins != null && skins.TryGetValue(weaponBase, out var skin) && !string.IsNullOrWhiteSpace(skin)
                    ? items.FirstOrDefault(e => Utils.ParseSpec(e.Weapon)?.Subclass == skin)?.Name
                    : null;

                var title = current != null ? $"{displayName} [{current}]" : displayName;

                var localItems = items;
                menu.AddItem(title, (p, _) => OpenSubMenu(p, weaponBase, localItems));
            }
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Menu] 构建主菜单异常 {ex.GetType().Name}: {ex.Message}");
        }

        return menu;
    }

    private static void OpenSubMenu(CCSPlayerController player, string weaponBase, List<Equipment> items)
    {
        try
        {
            if (!Players.IsHuman(player)) return;

            var menu = CreateMenu($"{Utils.GetWeaponDisplayName(weaponBase)} 皮肤");
            ConfigureWasdMenu(menu);

            Database.PlayerSkinCache.TryGetValue(player.SteamID, out var skins);
            var currentSkin = skins != null && skins.TryGetValue(weaponBase, out var skin) ? skin : null;

            // 必须闭包捕获新变量，避免循环变量共享
            var localWeaponBase = weaponBase;
            var display = Utils.GetWeaponDisplayName(weaponBase);

            // 1) 还原选项（二级显示格式：中文武器名 - 条目名）
            var defaultTitle = $"{display} - 默认模型";
            menu.AddItem(currentSkin == null ? $"{defaultTitle}（当前）" : defaultTitle, (p, _) =>
            {
                try
                {
                    if (!Players.IsHuman(p)) return;

                    var controller = p!;
                    Database.RemovePlayerSkin(controller.SteamID, localWeaponBase);
                    Weapons.RestoreToVanilla(controller, localWeaponBase);

                    var restored = Utils.GetDefaultSubclass(localWeaponBase);
                    Print(controller, restored == null
                        ? $"已还原 {display} 的默认模型"
                        : $"已还原 {display} 的默认模型（默认子类 {restored}）");
                }
                catch (Exception ex)
                {
                    Utils.LogError($"[Menu] 还原异常 {ex.GetType().Name}: {ex.Message}");
                }
            });

            // 2) 皮肤选项（皮肤名保留原文，如 glock_doro [007]）
            foreach (var item in items)
            {
                var spec = Utils.ParseSpec(item.Weapon);
                if (spec == null) continue;

                var localSpec = spec.Value;
                var localItem = item;
                var isCurrent = currentSkin != null &&
                                string.Equals(currentSkin, localSpec.Subclass, StringComparison.Ordinal);

                var title = $"{display} - {localItem.Name}{(isCurrent ? "（当前）" : "")}";

                menu.AddItem(title, (p, _) =>
                {
                    try
                    {
                        if (!Players.IsHuman(p)) return;

                        var controller = p!;

                        // 选择即持久化
                        Database.SetPlayerSkin(controller.SteamID, localSpec.WeaponBase, localSpec.Subclass);

                        // 手里有这把武器就立刻换，否则出生/拾取时会自动补上
                        Weapons.ApplyToHeldWeapon(controller, localSpec.WeaponBase, localSpec.Subclass);

                        Print(p, $"已选择 {localItem.Name}（{display}）");
                    }
                    catch (Exception ex)
                    {
                        Utils.LogError($"[Menu] 选择皮肤异常 {ex.GetType().Name}: {ex.Message}");
                    }
                });
            }

            menu.Display(player, 0);
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Menu] 打开子菜单异常 {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ConfigureWasdMenu(IMenu menu)
    {
        try
        {
            if (menu is not BaseMenu baseMenu) return;

            baseMenu.WasdMenu_FreezePlayer = Instance.Config.Menu.FreezePlayer;
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Menu] 配置 WASD 菜单异常 {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Print(CCSPlayerController player, string message)
    {
        try
        {
            if (player == null || !player.IsValid) return;
            player.PrintToChat($"{Instance.Config.Prefix} {message}");
        }
        catch
        {
            // ignored
        }
    }
}
