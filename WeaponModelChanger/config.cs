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

    public Config_Menu Menu { get; set; } = new();

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
}
