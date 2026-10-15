using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Logging;

public partial class Plugin : BasePlugin, IPluginConfig<Config>
{
    public override string ModuleName => "WeaponModelChanger";
    public override string ModuleVersion => "2.0.2";
    public override string ModuleAuthor => "56dz";

    public static Plugin Instance { get; set; } = new();

    public Config Config { get; set; } = new();

    /// <summary>Debug=false（默认）时只输出错误，不输出逐次流程日志。</summary>
    internal bool DebugEnabled => Config.Debug;

    public override void Load(bool hotReload)
    {
        Instance = this;

        try
        {
            // 1) 数据库：绝对路径 + 旧库自动迁移（绝不依赖进程 cwd）
            Database.Initialize();

            // 2) 事件与命令
            Events.Register();
            Commands.Register();

            // 3) 热重载：把已经在线的玩家补一遍
            if (hotReload)
            {
                Server.NextWorldUpdate(() =>
                {
                    try
                    {
                        foreach (var player in Utilities.GetPlayers())
                        {
                            if (!Players.IsHuman(player)) continue;
                            Database.LoadPlayerSkins(player.SteamID);
                            Weapons.ScheduleApply(player, 0.5f);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[Load] 热重载恢复异常 {ex.GetType().Name}: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Load] 初始化异常 {ex.GetType().Name}: {ex.Message}");
        }

        Logger.LogInformation($"[Load] v{ModuleVersion} hotReload={hotReload} Debug={Config.Debug} " +
                              $"菜单类型={Config.Menu.MenuType} 分类数={Config.Categories.Count} " +
                              $"皮肤条目={Utils.CountEquipments(Config)} 默认子类={Utils.DefaultSubclasses.Count} " +
                              $"弹匣刷新={Utils.RefillClips.Count} " +
                              $"资源模型={SkinModelPaths.ModelPathCount} 数据库={Database.DbPath}");
    }

    public override void Unload(bool hotReload)
    {
        try
        {
            Events.Unregister();
            Commands.Unregister();
            Weapons.ClearState();
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Unload] 异常 {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void OnConfigParsed(Config config)
    {
        Config = config;
        Config.Prefix = StringExtensions.ReplaceColorTags(Config.Prefix);

        Config.Menu ??= new Config.Config_Menu();
        Config.Categories ??= new Dictionary<string, MenuCategory>();
        Config.DefaultSubclasses ??= new Dictionary<string, string>(StringComparer.Ordinal);
        Config.RefillClip ??= new Dictionary<string, int>(StringComparer.Ordinal);

        // 功能3：构建"未选皮肤时的默认子类"映射（条目级覆盖顶层）
        var defaultCount = Utils.RebuildDefaultSubclasses(Config);
        if (defaultCount > 0)
        {
            foreach (var kv in Utils.DefaultSubclasses)
                Utils.Debug($"[Config] 默认子类 {kv.Key} -> {kv.Value}");
        }

        // v2.0.2：弹匣刷新回退映射（首选读 VData，这里只是回退）
        var refillCount = Utils.RebuildRefillClips(Config);
        if (refillCount > 0)
        {
            foreach (var kv in Utils.RefillClips)
                Utils.Debug($"[Config] 弹匣刷新 {kv.Key} -> {kv.Value}");
        }
    }
}
