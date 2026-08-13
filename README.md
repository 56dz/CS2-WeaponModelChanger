# CS2-WeaponModelChanger
### 此插件是在插件[equipments](https://github.com/exkludera-cssharp/equipments)基础上进行修改  
属于Equipments的精简修复版
移除了对[Clientprefs](https://github.com/Cruze03/Clientprefs)的依赖  
修复了更换模型后无法还原的BUG  
修复了修改后重进失效的BUG  
修复了选择后模型末尾出现menu的BUG  
___
### 插件的依赖
[MetaMod](https://www.sourcemm.net/downloads.php?branch=dev)  
[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)  
[MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager)  
[CS2MenuManager](https://github.com/schwarper/CS2MenuManager)  
___
### 使用方法
## 准备工作  
下载创意工坊资源包  
下载[Source 2 Viewer](https://s2v.app)  
找到创意工坊资源包目录，以下图为例  
![workshop](img/workshop.png)  
其中3672657008是创意工坊id，找到steam安装目录steamapps/workshop/content/730/创意工坊id
## 具体步骤
使用Source 2 Viewer打开创意工坊资源包(任意以.vpk结尾的文件)  
![3672657008](img/3672657008.png)  
打开之后点击上方find  
![find](img/find.png)  
查询vmdl  
![vmdl](img/vmdl.png)  
右键点击需要添加的武器模型，选择Copy name  
![get_name](img/get_name.png)  
打开scripts/weapons.vdata_c  
![vdata](img/vdata.png)  
再次点击find  
![find](img/find2.png)  
输入刚才复制文件name  
![vmdl_c](img/vmdl_c.png)  
删掉结尾的_c  
![vmdl2](img/vmdl2.png)  
找到武器名称并复制(weapon_skin_001)  
![weapon_skin_001](img/weapon_skin_001.png)  
## 配置文件编辑
```json
{
  // 聊天消息前缀
  // 支持颜色代码，例如: {white}, {darkred}, {green}, {lightblue} 等
  "Prefix": "{orange}[SkinChanger]{default}",
  // 主菜单设置
  "Menu": {
    // 菜单类型
    // "CenterHtmlMenu" - 屏幕中间的 HTML 菜单 (推荐)
    // "ChatMenu" - 聊天框内的文本菜单
    "Type": "CenterHtmlMenu",
    // 打开主菜单的指令列表
    // 玩家在控制台或聊天框输入这些指令即可打开菜单
    "Command": [ "css_skin", "css_models" ],
    // 打开主菜单所需的权限
    // 留空 [] 表示所有人都可以打开
    // 示例: ["@css/generic"] 表示需要通用管理员权限
    "Permission": [],
    // 限制使用菜单的队伍
    // "" - 无限制
    // "t" 或 "terrorist" - 仅T
    // "ct" 或 "counterterrorist" - 仅CT
    "Team": ""
  },
  // 分类配置
  // 您可以创建多个分类，例如 "Knives", "Gloves", "Rifles"
  "Categories": {
    // 分类名称 (Key)，自定义即可，例如这里叫 "Weapons"
    "Weapons": {
      // 直接打开此特定分类的指令
      // 配置后，玩家输入指令直接看到的是该分类下的皮肤，而不是主菜单
      "Command": [ "css_weapons" ],
      // 是否允许同时装备多个该分类下的皮肤
      // 注意：这通常取决于游戏逻辑，如果覆盖同一个武器槽位，通常只有最后一个生效
      "AllowMultiple": true,
      // 使用此分类下任何皮肤的权限要求
      "Permission": [ "@css/generic" ],
      // 限制使用此分类的队伍
      "Team": "",
      // 皮肤/装备列表
      "Equipment": [
        {
          // 在菜单中显示的名称
          "Name": "默认 M4A4",
          // 使用该特定皮肤的权限
          // 会与分类权限叠加检查
          "Permission": [],
          // 限制该皮肤使用的队伍
          "Team": "",
          // 核心配置：武器定义
          // 格式必须为： "基准武器名:目标模型名"
          // 
          // 1. 基准武器名 (冒号左边):
          //    用于检测玩家当前持有的武器。必须与游戏内部识别的武器名称一致。
          //    例如: weapon_ak47, weapon_knife, weapon_m4a1
          //
          // 2. 目标模型名 (冒号右边):
          //    用于替换模型。该名称会被传递给服务器的 AcceptInput "ChangeSubclass" 指令。
          //    它必须是服务器已加载的资源名称。
          //    例如: weapon_ak47_gold (假设你有一个金色的AK模型)
          "Weapon": "weapon_m4a1:weapon_m4a1"
        },
        {
          "Name": "红色皮肤 AK47",
          "Permission": [ "@css/vip" ], // 例子：仅VIP可用
          "Team": "t",                  // 例子：仅T可用
          "Weapon": "weapon_ak47:weapon_ak47_red"
        },
        {
          "Name": "蓝色皮肤 AK47",
          "Permission": [],
          "Team": "ct",                 // 例子：仅CT可用
          "Weapon": "weapon_ak47:weapon_ak47_blue"
        },
        {
          "Name": "黄金匕首",
          "Permission": [],
          "Team": "",
          "Weapon": "weapon_knife:weapon_knife_gold"
        }
      ]
    }
  }
}
```
