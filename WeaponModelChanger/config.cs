using CounterStrikeSharp.API.Core;

public class Config : BasePluginConfig
{
    public string Prefix { get; set; } = "{orange}[WeaponModelChanger]{default}";

    public class Config_Menu
    {
        public string Type { get; set; } = "CenterHtmlMenu";
        public List<string> Command { get; set; } = new() { "css_cw" };
        public List<string> Permission { get; set; } = new() { };
        public string Team { get; set; } = "";
    }

    public Config_Menu Menu { get; set; } = new Config_Menu();

    public Dictionary<string, MenuCategory> Categories { get; set; } = new Dictionary<string, MenuCategory>
    {
        {
            "Weapons", new MenuCategory
            {
                AllowMultiple = true,
                Permission = new List<string> { "@css/generic" },
                Team = "",
                Equipment = new List<Equipment>
                {
                    new Equipment { Name = "Golden Knife", Weapon = "weapon_knife:weapon_knife_gold" },
                    new Equipment { Name = "Red AK47", Weapon = "weapon_ak47:weapon_ak47_red" },
                    new Equipment { Name = "Blue AK47", Weapon = "weapon_ak47:weapon_ak47_blue" }
                }
            }
        }
    };
}

public class MenuCategory
{
    public List<string> Command { get; set; } = new() { "" };
    public bool AllowMultiple { get; set; } = false;
    public List<string> Permission { get; set; } = new() { "" };
    public string Team { get; set; } = "";
    public List<Equipment> Equipment { get; set; } = new();
}

public class Equipment
{
    public string Name { get; set; } = "Equipment Name";
    public List<string> Permission { get; set; } = new() { "" };
    public string Team { get; set; } = "";
    public string Weapon { get; set; } = "";
}
