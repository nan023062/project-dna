using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Dna.Auth;

public class UserStore : IDisposable
{
    private SqliteConnection? _db;

    public void Initialize(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "users.db");
        _db = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _db.Open();

        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;

            CREATE TABLE IF NOT EXISTS users (
                id            TEXT PRIMARY KEY,
                username      TEXT NOT NULL UNIQUE COLLATE NOCASE,
                password_hash TEXT NOT NULL,
                salt          TEXT NOT NULL,
                role          TEXT NOT NULL DEFAULT 'editor',
                created_at    TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        EnsureAdminExists();
    }

    public UserInfo? GetByUsername(string username)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT id, username, password_hash, salt, role, created_at FROM users WHERE username = @u";
        cmd.Parameters.AddWithValue("@u", username);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadUser(reader) : null;
    }

    public UserInfo? GetById(string id)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT id, username, password_hash, salt, role, created_at FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadUser(reader) : null;
    }

    public List<UserInfo> ListUsers()
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT id, username, password_hash, salt, role, created_at FROM users ORDER BY created_at";
        var list = new List<UserInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadUser(reader));
        return list;
    }

    public (bool Success, string Message, UserInfo? User) Register(string username, string password, string role = "editor")
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length < 2)
            return (false, "用户名至少 2 个字符", null);
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            return (false, "密码至少 4 个字符", null);
        if (role is not ("admin" or "editor" or "viewer"))
            return (false, "角色必须是 admin/editor/viewer", null);

        if (GetByUsername(username) != null)
            return (false, $"用户名 '{username}' 已存在", null);

        var id = Guid.NewGuid().ToString("N")[..12];
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = HashPassword(password, salt);

        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (id, username, password_hash, salt, role, created_at)
            VALUES (@id, @username, @hash, @salt, @role, @created_at)
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@username", username.Trim());
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@salt", salt);
        cmd.Parameters.AddWithValue("@role", role);
        cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        var user = GetById(id)!;
        return (true, "注册成功", user);
    }

    public (bool Success, string Message, UserInfo? User) Authenticate(string username, string password)
    {
        var user = GetByUsername(username);
        if (user == null)
            return (false, "用户名或密码错误", null);

        var hash = HashPassword(password, user.Salt);
        if (hash != user.PasswordHash)
            return (false, "用户名或密码错误", null);

        return (true, "登录成功", user);
    }

    public (bool Success, string Message, UserInfo? User) UpdateRole(string id, string role)
    {
        if (role is not ("admin" or "editor" or "viewer"))
            return (false, "角色必须是 admin/editor/viewer", null);

        var current = GetById(id);
        if (current == null)
            return (false, $"用户 '{id}' 不存在", null);

        if (string.Equals(current.Role, "admin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) &&
            CountUsersByRole("admin") <= 1)
        {
            return (false, "至少需要保留一个管理员账号", null);
        }

        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "UPDATE users SET role = @role WHERE id = @id";
        cmd.Parameters.AddWithValue("@role", role);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        return (true, "角色已更新", GetById(id));
    }

    public (bool Success, string Message, UserInfo? User) ResetPassword(string id, string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            return (false, "密码至少 4 个字符", null);

        var current = GetById(id);
        if (current == null)
            return (false, $"用户 '{id}' 不存在", null);

        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = HashPassword(password, salt);

        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            UPDATE users
            SET password_hash = @hash, salt = @salt
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@salt", salt);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        return (true, "密码已重置", GetById(id));
    }

    public (bool Success, string Message, UserInfo? User) DeleteUser(string id)
    {
        var current = GetById(id);
        if (current == null)
            return (false, $"用户 '{id}' 不存在", null);

        if (string.Equals(current.Role, "admin", StringComparison.OrdinalIgnoreCase) &&
            CountUsersByRole("admin") <= 1)
        {
            return (false, "至少需要保留一个管理员账号", null);
        }

        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        return (true, "用户已删除", current);
    }

    private void EnsureAdminExists()
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        if (count == 0)
        {
            var defaultPassword = Environment.GetEnvironmentVariable("DNA_ADMIN_PASSWORD") ?? "admin";
            Register("admin", defaultPassword, "admin");
        }
    }

    private static string HashPassword(string password, string salt)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(password + salt);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    private int CountUsersByRole(string role)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE role = @role";
        cmd.Parameters.AddWithValue("@role", role);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static UserInfo ReadUser(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Username = reader.GetString(1),
        PasswordHash = reader.GetString(2),
        Salt = reader.GetString(3),
        Role = reader.GetString(4),
        CreatedAt = DateTime.TryParse(reader.GetString(5), out var dt) ? dt : DateTime.UtcNow
    };

    public void Dispose()
    {
        if (_db is not null)
        {
            try
            {
                _db.Close();
            }
            finally
            {
                _db.Dispose();
                _db = null;
            }
        }

        GC.SuppressFinalize(this);
    }
}

public class UserInfo
{
    public string Id { get; init; } = "";
    public string Username { get; init; } = "";
    public string PasswordHash { get; init; } = "";
    public string Salt { get; init; } = "";
    public string Role { get; init; } = "editor";
    public DateTime CreatedAt { get; init; }
}
