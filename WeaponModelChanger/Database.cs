using Microsoft.Data.Sqlite;
using System.IO;

/// <summary>
/// SQLite 持久层。
///
/// ⚠️ 重要教训（1.x 的致命 bug）：原来的连接串是相对路径
/// "Data Source=weaponmodelchanger.db"，基准是**进程 cwd**。CS2 服务的 cwd 与
/// 插件目录不同，SQLite 又是"打开即建库"，于是插件在 cwd 下静默新建了一个空库，
/// 玩家已保存的皮肤一条都读不到 → 换皮肤静默失效（而且没有任何异常日志）。
///
/// 现在一律使用**绝对路径**：Path.Combine(ModuleDirectory, "weaponmodelchanger.db")。
/// 表结构与列名与 1.x 完全一致，旧库可直接沿用。
/// </summary>
public static class Database
{
    private const string DbFileName = "weaponmodelchanger.db";

    public static string DbPath { get; private set; } = string.Empty;

    private static string ConnectionString => $"Data Source={DbPath}";

    /// <summary>SteamID64 -> (基准武器名 -> 子类名)。与 1.x 的内存结构保持一致。</summary>
    public static Dictionary<ulong, Dictionary<string, string>> PlayerSkinCache { get; } = new();

    public static void Initialize()
    {
        try
        {
            DbPath = ResolveDbPath();
            MigrateLegacyDatabaseIfNeeded(DbPath);

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS player_skins (
                    SteamId TEXT NOT NULL,
                    WeaponId TEXT NOT NULL,
                    SkinId TEXT NOT NULL,
                    PRIMARY KEY (SteamId, WeaponId)
                );";
            command.ExecuteNonQuery();

            Utils.Log($"[Database] 使用数据库：{DbPath}");
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Database] 初始化失败 {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>绝对路径：插件自己的部署目录（BasePlugin.ModuleDirectory），与 cwd 无关。</summary>
    private static string ResolveDbPath()
    {
        var moduleDirectory = Plugin.Instance?.ModuleDirectory;

        if (!string.IsNullOrWhiteSpace(moduleDirectory))
            return Path.Combine(moduleDirectory, DbFileName);

        // 理论上不会走到这里（ModuleDirectory 由 CSS 提供）
        return Path.Combine(AppContext.BaseDirectory, DbFileName);
    }

    /// <summary>
    /// 首次使用新路径时，把旧位置的库搬过来（cwd、AppContext.BaseDirectory、
    /// 以及 1.x 时代实际落地的 game/bin/linuxsteamrt64）。只在目标不存在时复制。
    /// </summary>
    private static void MigrateLegacyDatabaseIfNeeded(string targetPath)
    {
        try
        {
            if (File.Exists(targetPath)) return;

            foreach (var candidate in EnumerateLegacyPaths())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate)) continue;

                    var source = Path.GetFullPath(candidate);
                    var target = Path.GetFullPath(targetPath);
                    if (string.Equals(source, target, StringComparison.Ordinal)) continue;

                    File.Copy(source, target);
                    Utils.Log($"[Database] 已迁移旧数据库：{source} -> {target}");
                    return;
                }
                catch (Exception ex)
                {
                    Utils.LogError($"[Database] 迁移失败（{candidate}）：{ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Database] 迁移流程异常 {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IEnumerable<string> EnumerateLegacyPaths()
    {
        // 1) 进程 cwd（1.x 相对路径的基准）
        yield return Path.Combine(Directory.GetCurrentDirectory(), DbFileName);

        // 2) 插件 DLL 所在目录
        yield return Path.Combine(AppContext.BaseDirectory, DbFileName);

        // 3) 1.x 实际落地的位置：<game>/bin/linuxsteamrt64/weaponmodelchanger.db
        //    从插件目录向上找，最多回溯 6 层，不写死任何绝对路径。
        var current = Plugin.Instance?.ModuleDirectory;
        for (var i = 0; i < 6 && !string.IsNullOrWhiteSpace(current); i++)
        {
            var dir = current!;

            yield return Path.Combine(dir, "bin", "linuxsteamrt64", DbFileName);
            yield return Path.Combine(dir, "game", "bin", "linuxsteamrt64", DbFileName);

            var parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(parent) || parent == dir) break;

            current = parent;
        }
    }

    public static void LoadPlayerSkins(ulong steamId)
    {
        if (steamId == 0) return;
        if (PlayerSkinCache.ContainsKey(steamId)) return;

        try
        {
            var skins = new Dictionary<string, string>(StringComparer.Ordinal);

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT WeaponId, SkinId FROM player_skins WHERE SteamId = @steamId";
            command.Parameters.AddWithValue("@steamId", steamId.ToString());

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;

                var weaponId = reader.GetString(0);
                var skinId = reader.GetString(1);
                if (string.IsNullOrWhiteSpace(weaponId) || string.IsNullOrWhiteSpace(skinId)) continue;

                skins[weaponId] = skinId;
            }

            PlayerSkinCache[steamId] = skins;
            Utils.Debug($"[Database] 载入玩家 {steamId} 的皮肤 {skins.Count} 条");
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Database] 读取玩家皮肤失败 {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void SetPlayerSkin(ulong steamId, string weaponId, string skinId)
    {
        if (steamId == 0 || string.IsNullOrWhiteSpace(weaponId) || string.IsNullOrWhiteSpace(skinId)) return;

        try
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT OR REPLACE INTO player_skins (SteamId, WeaponId, SkinId)
                    VALUES (@steamId, @weaponId, @skinId);";
                command.Parameters.AddWithValue("@steamId", steamId.ToString());
                command.Parameters.AddWithValue("@weaponId", weaponId);
                command.Parameters.AddWithValue("@skinId", skinId);
                command.ExecuteNonQuery();
            }

            if (!PlayerSkinCache.TryGetValue(steamId, out var skins))
            {
                skins = new Dictionary<string, string>(StringComparer.Ordinal);
                PlayerSkinCache[steamId] = skins;
            }

            skins[weaponId] = skinId;
            Utils.Debug($"[Database] 保存 {steamId} -> {weaponId} = {skinId}");
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Database] 保存皮肤失败 {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void RemovePlayerSkin(ulong steamId, string weaponId)
    {
        if (steamId == 0 || string.IsNullOrWhiteSpace(weaponId)) return;

        try
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM player_skins WHERE SteamId = @steamId AND WeaponId = @weaponId";
                command.Parameters.AddWithValue("@steamId", steamId.ToString());
                command.Parameters.AddWithValue("@weaponId", weaponId);
                command.ExecuteNonQuery();
            }

            if (PlayerSkinCache.TryGetValue(steamId, out var skins))
                skins.Remove(weaponId);

            Utils.Debug($"[Database] 删除 {steamId} -> {weaponId}");
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Database] 删除皮肤失败 {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static bool TryGetSkin(ulong steamId, string weaponId, out string skinId)
    {
        skinId = string.Empty;

        if (steamId == 0 || string.IsNullOrWhiteSpace(weaponId)) return false;
        if (!PlayerSkinCache.TryGetValue(steamId, out var skins)) return false;
        if (!skins.TryGetValue(weaponId, out var value) || string.IsNullOrWhiteSpace(value)) return false;

        skinId = value;
        return true;
    }

    public static void OnPlayerDisconnect(ulong steamId)
    {
        try
        {
            if (steamId != 0) PlayerSkinCache.Remove(steamId);
        }
        catch (Exception ex)
        {
            Utils.LogError($"[Database] 清理缓存失败 {ex.GetType().Name}: {ex.Message}");
        }
    }
}
