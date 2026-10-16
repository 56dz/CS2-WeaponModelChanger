using CounterStrikeSharp.API.Core;

/// <summary>
/// 配置结构与 1.x 完全兼容（同样的 JSON 键名），用户已有的
/// configs/plugins/WeaponModelChanger/WeaponModelChanger.json 可直接继续使用。
/// </summary>
public class Config : BasePluginConfig
{
    public string Prefix { get; set; } = "{orange}[WeaponModelChanger]{default}";

    /// <summary>
    /// true 时输出每一步详细过程（含子类是否真正生效）。默认 false，只输出错误。
    /// </summary>
    public bool Debug { get; set; } = false;

    /// <summary>
    /// 可选（v2.0.3 修复"重进游戏后模型不加载，要等下一回合"）：
    /// 玩家进服后强制把已应用的子类**重新切换一次**（先切回原版子类、再切回皮肤子类），
    /// 目的是给客户端制造一次真实的"子类变更"网络事件 —— 因为客户端是在连接快照里
    /// 第一次拿到子类的，那时它还没挂载完资源包 vdata，模型解析会回退成原版模型；
    /// 之后子类值一直没变，客户端就再也不会重新解析，只能等实体重建（死亡/下一回合）。
    /// 强制切换会带来一次很短的模型闪动（默认 3 秒后执行，通常在准备时间内），
    /// 不想看到闪动可设为 false。
    /// </summary>
    public bool ForceModelRefresh { get; set; } = true;

    /// <summary>强制刷新的延迟秒数（等客户端挂载完 addon / 进服稳定）。</summary>
    public float ForceRefreshDelay { get; set; } = 3.0f;

    public Config_Menu Menu { get; set; } = new();

    /// <summary>
    /// 可选（功能3）：基准武器 -> "未选皮肤时"使用的默认子类。
    /// 例：{"weapon_awp": "weapon_awp_10round"}。
    /// 玩家为该武器选过皮肤时**不会**用到这里；两者都没配则完全不动该武器。
    /// 与条目级 Equipment.DefaultSubclass 并存时，**条目级（更具体）优先**。
    /// </summary>
    public Dictionary<string, string> DefaultSubclasses { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 可选（v2.0.2 回退方案）：基准武器 -> 子类生效后允许上补到的弹匣上限。
    /// 例：{"weapon_awp": 10}。
    /// 正常情况不需要配 —— 首选直接读切换后的 VData（CBasePlayerWeaponVData.m_iMaxClip1）；
    /// 只有 VData 读不到时才用这里，且**只对配置了的武器**刷新。
    /// 与条目级 Equipment.RefillClip 并存时，**条目级（更具体）优先**。
    /// </summary>
    public Dictionary<string, int> RefillClip { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, MenuCategory> Categories { get; set; } = new()
    {
        ["Weapons"] = new MenuCategory
        {
            AllowMultiple = true,
            Permission = new List<string>(),
            Team = "",
            Equipment = new List<Equipment>()
        }
    };

    public class Config_Menu
    {
        /// <summary>菜单类型：WasdMenu（默认）/ ChatMenu / CenterHtmlMenu / ConsoleMenu</summary>
        public string MenuType { get; set; } = "WasdMenu";

        /// <summary>兼容旧字段名（1.x 里菜单类型叫 Type）。</summary>
        public string Type { get; set; } = "WasdMenu";

        public List<string> Command { get; set; } = new() { "css_cw" };
        public List<string> Permission { get; set; } = new();
        public string Team { get; set; } = "";

        /// <summary>WASD 菜单是否冻结玩家移动（默认不冻结）。</summary>
        public bool FreezePlayer { get; set; } = false;

        /// <summary>取实际使用的菜单类型名：**固定优先 WasdMenu**（用户明确要求），
        /// 只有显式配置了别得类型才使用别的。</summary>
        public string ResolveMenuType()
        {
            if (!string.IsNullOrWhiteSpace(MenuType)) return MenuType.Trim();
            if (!string.IsNullOrWhiteSpace(Type)) return Type.Trim();
            return "WasdMenu";
        }
    }
}

public class MenuCategory
{
    public List<string> Command { get; set; } = new();
    public bool AllowMultiple { get; set; } = true;
    public List<string> Permission { get; set; } = new();
    public string Team { get; set; } = "";
    public List<Equipment> Equipment { get; set; } = new();
}

public class Equipment
{
    public string Name { get; set; } = "Equipment Name";
    public List<string> Permission { get; set; } = new();
    public string Team { get; set; } = "";

    /// <summary>格式 "基准武器实体名:子类名"，例如 "weapon_ak47:weapon_skin_001"。</summary>
    public string Weapon { get; set; } = "";

    /// <summary>
    /// 可选（功能3）：该基准武器"未选任何皮肤"时使用的默认子类。
    /// 例：AWP 的任一条目上写 "weapon_awp_10round"，则没有 AWP 皮肤的玩家
    /// 持枪时自动应用这个 10 发子类；已选皮肤的玩家不受影响。
    /// 同一武器的多个条目若配置不一致，取配置文件里**第一个**非空值。
    /// </summary>
    public string DefaultSubclass { get; set; } = "";

    /// <summary>
    /// 可选（v2.0.2）：该基准武器的弹匣刷新上限（0 = 不配置）。
    /// 与顶层 Config.RefillClip 同语义，优先级更高；同一武器取**第一个**非 0 条目。
    /// 注意：即使完全不配，只要 VData 里子类真的提高了 m_iMaxClip1，插件也会自动补
    /// （读 VData 是首选路径），这里只是 VData 读不到时的回退。
    /// </summary>
    public int RefillClip { get; set; } = 0;
}
