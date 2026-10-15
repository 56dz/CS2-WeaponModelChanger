using CounterStrikeSharp.API.Modules.Commands;

public static class Commands
{
    private static Plugin Instance => Plugin.Instance;

    private static readonly Dictionary<string, CommandInfo.CommandCallback> Registered = new(StringComparer.OrdinalIgnoreCase);

    public static void Register()
    {
        try
        {
            // 主菜单命令（config: Menu.Command，默认 css_cw；聊天里 !cw / /cw 都会触发）
            foreach (var command in Instance.Config.Menu.Command ?? new List<string>())
            {
                RegisterCommand(command, "打开武器皮肤菜单", (player, _) => Menu.Open(player));
            }

            // 分类专用命令（config: Categories.<name>.Command）
            foreach (var (categoryName, category) in Utils.EnumerateCategories())
            {
                if (category.Command == null) continue;

                foreach (var command in category.Command)
                {
                    if (string.IsNullOrWhiteSpace(command)) continue;

                    var localCategory = category;
                    RegisterCommand(command, $"打开 {categoryName} 分类菜单", (player, _) =>
                    {
                        try
                        {
                            if (!Players.IsHuman(player)) return;

                            if (!Utils.HasPermission(player, localCategory.Permission, localCategory.Team))
                            {
                                player!.PrintToChat($"{Instance.Config.Prefix} {Instance.Localizer["chat<nopermission>"]}");
                                return;
                            }

                            Menu.Open(player);
                        }
                        catch (Exception ex)
                        {
                            Utils.LogError($"[Commands] {command} 异常 {ex.GetType().Name}: {ex.Message}");
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Commands] 注册失败 {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RegisterCommand(string command, string description, CommandInfo.CommandCallback handler)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        if (Registered.ContainsKey(command)) return;

        try
        {
            Instance.AddCommand(command, description, handler);
            Registered[command] = handler;
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Commands] 注册 {command} 失败 {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        try
        {
            foreach (var (command, handler) in Registered)
            {
                try
                {
                    Instance.RemoveCommand(command, handler);
                }
                catch (Exception ex)
                {
                    Utils.LogError($"[Commands] 注销 {command} 失败 {ex.GetType().Name}: {ex.Message}");
                }
            }

            Registered.Clear();
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Commands] 注销异常 {ex.GetType().Name}: {ex.Message}");
        }
    }
}
