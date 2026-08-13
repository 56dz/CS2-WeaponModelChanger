using Microsoft.Data.Sqlite;
using System.Collections.Generic;

public static class Database
{
    private static string ConnectionString = "Data Source=weaponmodelchanger.db";
    public static Dictionary<ulong, Dictionary<string, string>> PlayerSkinCache = new();

    public static void Initialize()
    {
        using (var connection = new SqliteConnection(ConnectionString))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS player_skins (
                    SteamId TEXT NOT NULL,
                    WeaponId TEXT NOT NULL,
                    SkinId TEXT NOT NULL,
                    PRIMARY KEY (SteamId, WeaponId)
                );
            ";
            command.ExecuteNonQuery();
        }
    }

    public static void LoadPlayerSkins(ulong steamId)
    {
        if (PlayerSkinCache.ContainsKey(steamId)) return;

        var skins = new Dictionary<string, string>();
        using (var connection = new SqliteConnection(ConnectionString))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT WeaponId, SkinId FROM player_skins WHERE SteamId = @steamId";
            command.Parameters.AddWithValue("@steamId", steamId);

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    skins[reader.GetString(0)] = reader.GetString(1);
                }
            }
        }
        PlayerSkinCache[steamId] = skins;
    }

    public static void SetPlayerSkin(ulong steamId, string weaponId, string skinId)
    {
        using (var connection = new SqliteConnection(ConnectionString))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO player_skins (SteamId, WeaponId, SkinId)
                VALUES (@steamId, @weaponId, @skinId);
            ";
            command.Parameters.AddWithValue("@steamId", steamId);
            command.Parameters.AddWithValue("@weaponId", weaponId);
            command.Parameters.AddWithValue("@skinId", skinId);
            command.ExecuteNonQuery();
        }

        if (!PlayerSkinCache.ContainsKey(steamId))
            PlayerSkinCache[steamId] = new Dictionary<string, string>();

        PlayerSkinCache[steamId][weaponId] = skinId;
    }

    public static void RemovePlayerSkin(ulong steamId, string weaponId)
    {
        using (var connection = new SqliteConnection(ConnectionString))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM player_skins WHERE SteamId = @steamId AND WeaponId = @weaponId";
            command.Parameters.AddWithValue("@steamId", steamId);
            command.Parameters.AddWithValue("@weaponId", weaponId);
            command.ExecuteNonQuery();
        }

        if (PlayerSkinCache.ContainsKey(steamId) && PlayerSkinCache[steamId].ContainsKey(weaponId))
        {
            PlayerSkinCache[steamId].Remove(weaponId);
        }
    }

    public static void OnPlayerDisconnect(ulong steamId)
    {
        if (PlayerSkinCache.ContainsKey(steamId))
            PlayerSkinCache.Remove(steamId);
    }
}
