using CounterStrikeSharp.API.Modules.Commands;
using System.Collections.Generic;

public static class Commands
{
    private static readonly Plugin Instance = Plugin.Instance;
    private static readonly Dictionary<string, CommandInfo.CommandCallback> registeredCategoryCommands = new();

    public static void Register()
    {
        foreach (var command in Instance.Config.Menu.Command)
            Instance.AddCommand(command, string.Empty, Menu.Open);

        foreach (var (fullPath, category) in Utils.EnumerateCategories())
        {
            if (category.Command == null) continue;
            foreach (var cmd in category.Command)
            {
                if (string.IsNullOrWhiteSpace(cmd)) continue;

                var catLocal = category;
                CommandInfo.CommandCallback handler = (player, info) =>
                {
                    if (player == null) return;
                    if (!Utils.HasPermission(player, catLocal.Permission, catLocal.Team))
                    {
                        player.PrintToChat(Instance.Config.Prefix + Instance.Localizer["chat<nopermission>"]);
                        return;
                    }
                    Database.LoadPlayerSkins(player.SteamID);
                    Menu.MainMenu(player).Display(player, 0);
                };

                Instance.AddCommand(cmd, $"Open equipment menu for {fullPath}", handler);
                registeredCategoryCommands[cmd] = handler;
            }
        }
    }

    public static void Unregister()
    {
        foreach (var command in Instance.Config.Menu.Command)
            Instance.RemoveCommand(command, Menu.Open);

        foreach (var kv in registeredCategoryCommands)
            Instance.RemoveCommand(kv.Key, kv.Value);

        registeredCategoryCommands.Clear();
    }
}
