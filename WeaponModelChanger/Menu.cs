using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Interface;
using System.Linq;

public static class Menu
{
    private static readonly Plugin Instance = Plugin.Instance;

    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public static void Open(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        if (!Utils.HasPermission(player, Instance.Config.Menu.Permission, Instance.Config.Menu.Team))
        {
            player.PrintToChat(Instance.Config.Prefix + Instance.Localizer["chat<nopermission>"]);
            return;
        }
        Database.LoadPlayerSkins(player.SteamID);
        MainMenu(player).Display(player, 0);
    }

    public static IMenu MainMenu(CCSPlayerController player)
    {
        IMenu mainMenu = MenuManager.MenuByType(Instance.Config.Menu.Type, "武器皮肤菜单", Instance);
        var weaponBases = Utils.GetAllWeaponBases();

        foreach (var weaponBase in weaponBases)
        {
            var equipments = Utils.GetEquipmentsByWeaponBase(weaponBase).ToList();
            if (equipments.Count == 0) continue;

            bool hasAnyPermission = false;
            foreach (var eq in equipments)
            {
                var category = Utils.FindCategoryByEquipment(eq);
                if (category != null && Utils.HasPermission(player, category.Permission, category.Team.ToLower()))
                {
                    hasAnyPermission = true;
                    break;
                }
            }

            if (!hasAnyPermission) continue;

            var firstCategory = Utils.FindCategoryByEquipment(equipments.First());
            string displayName = Utils.GetWeaponDisplayName(weaponBase);

            bool hasSkin = Database.PlayerSkinCache.TryGetValue(player.SteamID, out var skins) &&
                           skins.ContainsKey(weaponBase) &&
                           !string.IsNullOrEmpty(skins[weaponBase]);

            if (hasSkin) displayName += " [已启用]";

            mainMenu.AddItem(displayName, (p, menuOption) =>
            {
                if (firstCategory != null) SubMenu(p, weaponBase, firstCategory, mainMenu);
            });
        }

        return mainMenu;
    }

    public static void SubMenu(CCSPlayerController player, string weaponBase, MenuCategory category, IMenu? prevMenu)
    {
        IMenu subMenu = MenuManager.MenuByType(Instance.Config.Menu.Type, $"选择 {Utils.GetWeaponDisplayName(weaponBase)} 皮肤", Instance);
        var equipments = Utils.GetEquipmentsByWeaponBase(weaponBase).ToList();

        bool isDefault = true;
        Dictionary<string, string>? skins = null;
        if (Database.PlayerSkinCache.TryGetValue(player.SteamID, out skins) && skins.ContainsKey(weaponBase))
        {
            isDefault = false;
        }

        string defaultTitle = "默认模型";
        if (isDefault) defaultTitle += " (当前选择)";

        subMenu.AddItem(defaultTitle, (p, mo) =>
        {
            Database.RemovePlayerSkin(p.SteamID, weaponBase);
            Weapons.Unequip(p, $"{weaponBase}:{weaponBase}");
            SubMenu(p, weaponBase, category, prevMenu);
        });

        foreach (var equipment in equipments)
        {
            if (!Utils.HasPermission(player, equipment.Permission, equipment.Team)) continue;

            var parts = equipment.Weapon.Split(':');
            if (parts.Length < 2) continue;

            var skinId = parts[1];
            string itemTitle = equipment.Name;

            if (skins != null && skins.TryGetValue(weaponBase, out var activeSkin) && activeSkin == skinId)
            {
                itemTitle += " (当前选择)";
            }

            subMenu.AddItem(itemTitle, (p, mo) =>
            {
                Database.SetPlayerSkin(p.SteamID, weaponBase, skinId);
                Weapons.Equip(p, equipment.Weapon);
                SubMenu(p, weaponBase, category, prevMenu);
            });
        }

        subMenu.PrevMenu = prevMenu;
        subMenu.Display(player, 0);
    }
}
