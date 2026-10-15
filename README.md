# CS2-WeaponModelChanger

CS2 服务端武器模型（皮肤）替换插件：玩家用 **WASD 菜单**选皮肤，选择立刻写库，每次进服/出生自动重新应用。

当前版本 **v2.0.2**。

v2.0.0 起对代码做了完整重写。早期版本有两个根因明确的问题：**换皮肤不生效**（数据库用相对路径，
进程工作目录一变就静默新建空库）与**进服后崩服**（对玩家武器做原生读取时用了未校验的实体索引 /
强制解引用）。重写补齐了换装时机、资源登记与弹匣刷新，逐条见文末「更新记录」。

---

## 一、功能

| 功能 | 说明 |
| :--- | :--- |
| WASD 菜单选皮肤 | `!cw` / `css_cw` 打开；W/S（或滚轮）移动，E 确认，Q/退出键返回或关闭 |
| 选择即持久化 | 选中的皮肤写入 SQLite，立刻生效（手里有这把武器时立即换，没有则出生/拾取时自动补上） |
| 每次进入自动加载 | 玩家出生、拾到武器、回合开始、插件热重载时都会从库里读回并应用 |
| 还原默认模型 | 子菜单第一项「默认模型」= 删除该武器的记录并把子类还原为原版 |
| 权限 / 队伍限制 | 分类级 + 条目级双重过滤，支持 `@css/vip` 这类权限和 `t` / `ct` 队伍限制 |

原理：把武器的实体子类（`m_nSubclassID`）改成资源包 `scripts/weapons.vdata` 里定义的
`weapon_skin_XXX`，从而整替换该武器的 VData（模型、骨骼、弹匣容量、开火音效等）。

---

## 二、依赖

| 依赖项 | 版本要求 | 说明 |
| :--- | :--- | :--- |
| [MetaMod:Source](https://www.sourcemm.net/downloads.php?branch=dev) | 最新开发版 | 插件加载框架 |
| [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) | **1.0.376+**（本项目按 1.0.376 编译，.NET 10 运行时） | CS2 的 C# 插件核心 |
| [MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager) | 最新版 | 挂载创意工坊资源包 |
| [CS2MenuManager](https://github.com/schwarper/CS2MenuManager) | **1.0.43+** | 菜单系统（本插件用 `WasdMenu`） |

编译：`dotnet build -c Release`（`net10.0`）。项目文件已把上述两个 NuGet 包固定到 `1.0.376` / `1.0.43`。

---

## 三、安装

1. 把 `WeaponModelChanger.dll`（以及你需要的 `lang/` 目录）放到
   `addons/counterstrikesharp/plugins/WeaponModelChanger/`。
2. 把资源包（含 `scripts/weapons.vdata` 及模型）挂到 MultiAddonManager：
   ```
   mm_extra_addons        "<addon_id>"
   mm_client_extra_addons "<addon_id>"
   ```
3. 首次加载后会自动生成配置 `addons/counterstrikesharp/configs/plugins/WeaponModelChanger/WeaponModelChanger.json`。
4. 重启服务器（或等 CSS 热重载完成）。

---

## 四、命令与菜单操作

| 输入 | 位置 | 效果 |
| :--- | :--- | :--- |
| `!cw` | 聊天框 | 打开主菜单（由 config 的 `Menu.Command` 决定，默认 `css_cw`） |
| `css_cw` | 控制台 / 聊天 | 同上 |
| `!<分类命令>` | 聊天框 | 打开该分类的菜单（config 里 `Categories.<名>.Command`） |

**WASD 菜单操作**：

| 按键 | 作用 |
| :--- | :--- |
| `W` / `S`（或鼠标滚轮） | 上 / 下移动选项，超出当页自动翻页 |
| `E` | 确认当前选项 |
| `Q` | 返回上一级菜单（子菜单 -> 主菜单） |
| `R`（退出键） | 关闭菜单 |

> 主菜单列出**你有权限使用的武器**（标题里带当前皮肤名），选中武器后进入子菜单：
> 「默认模型」+ 该武器的所有皮肤，当前正在用的那项带「（当前）」标记。

---

## 五、配置说明

配置文件：`addons/counterstrikesharp/configs/plugins/WeaponModelChanger/WeaponModelChanger.json`

`v2.0.0` **完全兼容 1.x 的配置格式**，老配置可直接使用（`Menu.Type` 旧字段仍然识别）。

```jsonc
{
  "Prefix": "{orange}[WeaponModelChanger]{default}",

  // 排错开关：true 时日志会输出每一步细节（含子类是否真正生效）。默认 false，只输出错误。
  "Debug": false,

  "Menu": {
    // 菜单类型：WasdMenu（默认）/ ChatMenu / CenterHtmlMenu / ConsoleMenu
    "MenuType": "WasdMenu",
    // 兼容旧字段（1.x 用 Type 表示菜单类型）；MenuType 优先级更高
    "Type": "WasdMenu",
    // 打开主菜单的命令（聊天里输入 !cw 即触发 css_cw）
    "Command": [ "css_cw" ],
    // 打开主菜单所需权限，[] = 所有人
    "Permission": [],
    // 限制队伍："" / "t" / "ct"
    "Team": "",
    // WASD 菜单是否冻结玩家移动（默认 false，避免战斗中被冻住）
    "FreezePlayer": false
  },

  "Categories": {
    "Weapons": {
      // 该分类的专用命令（"" 表示不注册）
      "Command": [ "" ],
      "AllowMultiple": true,
      "Permission": [],
      "Team": "",
      "Equipment": [
        {
          "Name": "ak103_hina [001]",
          "Permission": [],
          "Team": "",
          // 格式固定为 "基准武器实体名:子类名"
          "Weapon": "weapon_ak47:weapon_skin_001"
        }
      ]
    }
  }
}
```

**字段要点**

- `Weapon`：冒号左边是**武器实体名**（如 `weapon_awp`、`weapon_glock`、`weapon_ak47`），
  右边是资源包 `scripts/weapons.vdata` 里的**子类名**（如 `weapon_skin_089`）。两边写错都会"没反应"。
- 所有刀都是同一个实体 `weapon_knife`，因此 22 个刀皮肤全部写在 `weapon_knife:` 下（插件会把
  `weapon_knife_karambit` 等一律归一化成 `weapon_knife`）。
- `Permission` / `Team` 在**分类**和**条目**两级都会检查，两级都通过才显示。
- 队伍值：`""`（不限）/ `"t"` / `"terrorist"` / `"ct"` / `"counterterrorist"`。

**已知限制（与 1.x 一致，属于实体层限制）**

- `weapon_hkp2000`（P2000）与 `weapon_usp_silencer`（USP-S）在实体层是**同一个实体名 `weapon_hkp2000`**。
  v2.0.0 不再读取 `ItemDefinitionIndex`（那是原生 econ item view 读取，有崩溃风险），
  因此配置里写成 `weapon_usp_silencer:...` 的条目不会命中，只放在 `weapon_hkp2000:` 下才生效。
- 同理，M4A4 是 `weapon_m4a1`、M4A1-S 是 `weapon_m4a1_silencer`，两者实体名不同，可正常区分。

---

## 六、数据库

| 项 | 值 |
| :--- | :--- |
| 路径 | **`<插件部署目录>/weaponmodelchanger.db`**（= `addons/counterstrikesharp/plugins/WeaponModelChanger/weaponmodelchanger.db`） |
| 表 | `player_skins(SteamId TEXT, WeaponId TEXT, SkinId TEXT, PRIMARY KEY(SteamId, WeaponId))` |
| 键 | `WeaponId` = 基准武器名（`weapon_awp`）；`SkinId` = 子类名（`weapon_skin_089`） |

**自动迁移**：如果新路径下还没有库，插件会依次尝试从以下旧位置复制一份（只在目标不存在时复制）：

1. 进程当前目录（1.x 相对路径的基准）
2. 插件 DLL 所在目录
3. 从插件目录向上回溯最多 6 层寻找 `<...>/bin/linuxsteamrt64/weaponmodelchanger.db`（1.x 实际落地的位置）

> ⚠️ **为什么一定要用绝对路径**：SQLite 的连接串如果写相对路径，基准是**进程 cwd**，
> 而 CSS 的 cwd 不等于插件目录；SQLite 又是"打开即建库"，于是一个**空库**会在 cwd 被静默创建，
> 玩家已保存的皮肤一条都读不到 —— 表现就是"选了皮肤完全没反应"，而且**没有任何异常日志**。
> v2.0.0 改为 `Path.Combine(ModuleDirectory, "weaponmodelchanger.db")`，并在启动日志里打印绝对路径。

---

## 七、日志与排错

日志文件：`addons/counterstrikesharp/logs/log-WeaponModelChanger-<日期>.txt`（以及 `log-all-<日期>.txt`）。

| 现象 | 看什么 |
| :--- | :--- |
| 菜单打不开 | 是否 `[Load] v2.0.0 ...` 出现；`!cw` 是否有权限限制（`chat<nopermission>`） |
| 选了但模型没变 | 打开 `"Debug": true`，看 `ChangeSubclass` 前后 `subclassToken` 是否变化 |
| 提示 `子类 ... 未生效` | 子类名不在资源包里、资源包没挂载、或该 build 的引擎没有应用子类 |
| 玩家记录像是空的 | 看启动日志的 `[Database] 使用数据库：<绝对路径>` 是否正确、是否有 `已迁移旧数据库` |
| 完全没反应 | `Categories.Weapons.Equipment[].Weapon` 左边 == 游戏里的武器实体名？右边 == 资源包子类名？ |

`Debug = true` 时的典型日志：

```
[Load] v2.0.0 hotReload=False Debug=True 菜单类型=WasdMenu 分类数=1 皮肤条目=89 数据库=/.../plugins/WeaponModelChanger/weaponmodelchanger.db
[Database] 使用数据库：/.../plugins/WeaponModelChanger/weaponmodelchanger.db
[ApplyAllWeapons] weapon_awp 应用子类 weapon_skin_089：subclassToken 0 -> 2233641767
```

如果 token 是 `0 -> 0`，说明引擎根本没应用这个子类（不是插件逻辑问题）。

---

## 八、更新记录

### v2.0.2（2026-10-15）

**修复：AWP 购买后先显示 5 发，换一次弹才变 10 发**

`m_iDefaultClip1` 只在武器创建时生效，而子类是在创建之后才切换的 —— 弹匣按旧 VData 填了 5 发，
切到 10 发子类后已填充的 `m_iClip1` 不会自动重算。现在在 `ChangeSubclass` 成功
（`subclassToken` 前后不同）之后，读切换后的 `CBasePlayerWeaponVData.MaxClip1` 刷新弹匣。

安全闸（避免"换皮肤白嫖子弹"）：

- 只在子类**真的提高了上限**时补（`beforeMax < target`，如 AWP 5→10；AK 30→30 不补）
- 只升不降（`clip >= target` 直接跳过）
- 只在应用子类那一刻执行一次（复用幂等缓存，不挂 tick / 换弹 / 开火）
- 写 `weapon.Clip1`（`ref int` 直写 schema）+ `Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1", 0)` + 回读校验
- 可选兜底配置 `"RefillClip": { "weapon_awp": 10 }`，仅在 VData 读不到时使用

### v2.0.1（2026-10-15）

1. **修复 ERROR 模型**：皮肤模型文件存在于资源包中，但引擎报
   `RESOURCE_TYPE_MODEL ... requested but not resident (Missing from a manifest?)` ——
   模型没有被登记进服务端资源清单。现在在 `OnServerPrecacheResources` 中对 89 个皮肤的
   `m_szWorldModel` 逐条 `manifest.AddResource(...)`
   （路径 `weapons/models/<skin>/<skin>.vmdl`，不带 `_c`）。
2. **菜单武器名中文化**：`lang/zh-Hans.json` 提供「基准武器 → 中文名」映射，
   一级菜单显示 `格洛克`，二级显示 `格洛克 - glock_doro [007]`；缺键时回退原名。
3. **默认子类机制**：新增顶层 `DefaultSubclasses` 与条目级 `DefaultSubclass`，用于
   「没有选择皮肤时也应用某个子类」。配合资源包的 `weapon_awp_10round`，让**默认 AWP 也是 10 发**
   —— 因为 addon 的 `weapons.vdata` 是合并语义：**新增条目有效，覆盖基础游戏已有条目无效**。
   已选皮肤的玩家不会被默认子类覆盖。

### v2.0.0 —— 完全重写（2026-10-15）

**修复的两个根因**

1. **换皮肤静默失效**：1.x 用相对路径 `Data Source=weaponmodelchanger.db` 连库，
   基准是进程 cwd 而非插件目录 → 插件在 cwd 静默新建空库 → 玩家皮肤一条都读不到 →
   `ChangeSubclass` **从未被调用**（所以"格洛克/AWP 都是原版模型、AWP 还 5 发"）。
   现在：绝对路径（`ModuleDirectory`）+ 启动打印路径 + 旧库自动迁移。
2. **进服崩服（segfault in libserver.so）**：1.x 在 `OnEntityCreated` 里对**每一个** `weapon_*`
   实体延一帧处理，并在归属解析里用了未校验的索引与强制解引用：
   - `Utilities.GetPlayerFromIndex((int)weapon.OwnerEntity.Index)` —— 索引完全没校验；
   - `weapon.As<CCSWeaponBaseGun>().OwnerEntity.Value!.Index` —— `!.` 强制解引用；
   - `weapon.AttributeManager.Item.ItemDefinitionIndex` —— 原生 econ item view 读取，
     在实体刚创建时可能未就绪。

**v2.0.0 的防崩设计（逐条对应）**

| 设计 | 说明 |
| :--- | :--- |
| 不再订阅 `OnEntityCreated` | 只响应"玩家出生 / 拾到武器 / 回合开始 / 热重载"，即武器**已被玩家实际持有**时才动手 |
| 不使用 `Utilities.GetPlayerFromIndex(未校验索引)` | 归属解析改为 `weapon.OwnerEntity?.Value` -> 用 `pawn.Index` 与 `Utilities.GetPlayers()` 比对（与线上验证可用的 OldWeaponSounds 同款写法）；所有索引先判范围（`0..63`） |
| 不使用 `!.` 强制解引用 | 所有句柄先判空 + `IsValid`，控制器在检查后赋给局部变量 |
| 不读 `AttributeManager.Item.ItemDefinitionIndex` | 只用实体 `DesignerName`（`Contains("knife")` 归一化为 `weapon_knife`） |
| 唯一原生调用全程保护 | `AcceptInput("ChangeSubclass")` 包在 try/catch，前后读 `SubclassID`（反射 + try/catch）校验是否生效 |
| 幂等 | `steamId:武器句柄 -> 子类名` 记录已应用项，避免重复改子类 |
| 全链路容错 | 数据库、事件、命令、菜单回调、定时器回调全部独立 try/catch，异常只记日志不抛出 |
| 日志可开关 | `Debug` 默认 `false`，平时只输出错误 |

**其它改进**

- 菜单默认使用 **WasdMenu**，可用 `MenuType` 切换 `ChatMenu` / `CenterHtmlMenu` / `ConsoleMenu`。
- 数据库读写全部带日志与容错。注意 CSS 1.0.376 的 `SubclassID` **只有 getter**、`Schema.*` 全为只读，
  换子类只能走 `AcceptInput("ChangeSubclass")`，没有合法的替代写路径。
- 代码按职责拆分：`main / config / Utils / Players / Database / Weapons / Menu / Commands / Events`。

---

## 九、免责声明

- 本插件仅供学习和测试使用。
- 使用任何修改游戏内容的插件都可能存在风险，请在非正式环境中谨慎使用。
- 作者不对使用本插件造成的任何后果负责。
