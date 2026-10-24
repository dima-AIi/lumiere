using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;
using NpgsqlTypes;

namespace Lumiere.Services;

/// <summary>Durable documents, small JPEG uploads, sessions and cookie encryption keys.</summary>
public sealed class PostgresState : IDisposable, IXmlRepository
{
    private readonly NpgsqlDataSource? _source;
    private readonly string _schema = "public";
    public bool Enabled => _source is not null;

    public PostgresState(IConfiguration config)
    {
        if (config.GetValue<bool>("StaticExport")) return;
        var value = config["DATABASE_URL"] ?? config.GetConnectionString("Lumiere");
        if (string.IsNullOrWhiteSpace(value)) return;
        NpgsqlConnectionStringBuilder settings;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "postgres" or "postgresql")
        {
            var credentials = uri.UserInfo.Split(':', 2);
            settings = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host, Port = uri.IsDefaultPort ? 5432 : uri.Port,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
                Username = Uri.UnescapeDataString(credentials[0]),
                Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "",
                SslMode = SslMode.VerifyFull
            };
        }
        else settings = new NpgsqlConnectionStringBuilder(value);
        settings.MaxPoolSize = 5;
        // Neon uses verified TLS. Do not probe unavailable Kerberos libraries in a small Linux image.
        settings.GssEncryptionMode = GssEncryptionMode.Disable;
        if (config["Postgres:Schema"] is { Length: > 0 } schema)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^[a-z_][a-z0-9_]*$")) throw new InvalidOperationException("Invalid database schema name.");
            _schema = schema;
        }
        settings.Timeout = 15;
        settings.CommandTimeout = 30;
        _source = NpgsqlDataSource.Create(settings.ConnectionString);
    }

    public async Task InitializeAsync(string contentRoot, CancellationToken ct = default)
    {
        if (_source is null) return;
        await using var connection = await _source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var migration = new NpgsqlCommand(Sql(await File.ReadAllTextAsync(
            Path.Combine(contentRoot, "Migrations", "001_durable_state.sql"), ct)), connection, tx);
        await migration.ExecuteNonQueryAsync(ct);
        var menu = await File.ReadAllTextAsync(Path.Combine(contentRoot, "wwwroot", "data", "menu.json"), ct);
        await using var seed = new NpgsqlCommand(Sql("INSERT INTO lumiere_documents(key,data) VALUES ('menu',@data) ON CONFLICT DO NOTHING"), connection, tx);
        seed.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, menu);
        await seed.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public string? GetDocument(string key)
    {
        using var cmd = CreateCommand("SELECT data::text FROM lumiere_documents WHERE key=@key");
        cmd.Parameters.AddWithValue("key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SaveDocument(string key, string json)
    {
        using var cmd = CreateCommand("INSERT INTO lumiere_documents(key,data) VALUES (@key,@data) ON CONFLICT(key) DO UPDATE SET data=EXCLUDED.data");
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, json);
        cmd.ExecuteNonQuery();
    }

    public void MutateOrders(Action<List<Order>> change)
    {
        using var connection = _source!.OpenConnection();
        using var tx = connection.BeginTransaction();
        using var create = new NpgsqlCommand(Sql("INSERT INTO lumiere_documents(key,data) VALUES ('orders','[]') ON CONFLICT DO NOTHING"), connection, tx);
        create.ExecuteNonQuery();
        using var read = new NpgsqlCommand(Sql("SELECT data::text FROM lumiere_documents WHERE key='orders' FOR UPDATE"), connection, tx);
        var orders = JsonSerializer.Deserialize<List<Order>>((string)read.ExecuteScalar()!)
            ?? throw new InvalidDataException("Invalid order document; refusing to overwrite it.");
        change(orders);
        using var update = new NpgsqlCommand(Sql("UPDATE lumiere_documents SET data=@data WHERE key='orders'"), connection, tx);
        update.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(orders));
        update.ExecuteNonQuery();
        tx.Commit();
    }

    public async Task SaveImageAsync(string name, byte[] bytes, CancellationToken ct)
    {
        await using var cmd = CreateCommand("INSERT INTO lumiere_media(name,body) VALUES (@name,@body)");
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("body", bytes);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<byte[]?> GetImageAsync(string name, CancellationToken ct)
    {
        await using var cmd = CreateCommand("SELECT body FROM lumiere_media WHERE name=@name");
        cmd.Parameters.AddWithValue("name", name);
        return await cmd.ExecuteScalarAsync(ct) as byte[];
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var cmd = CreateCommand("SELECT xml FROM lumiere_keys ORDER BY id");
        using var reader = cmd.ExecuteReader();
        var keys = new List<XElement>();
        while (reader.Read()) keys.Add(XElement.Parse(reader.GetString(0)));
        return keys;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var cmd = CreateCommand("INSERT INTO lumiere_keys(id,xml) VALUES (@id,@xml) ON CONFLICT DO NOTHING");
        cmd.Parameters.AddWithValue("id", friendlyName);
        cmd.Parameters.AddWithValue("xml", element.ToString(SaveOptions.DisableFormatting));
        cmd.ExecuteNonQuery();
    }

    private string Sql(string sql)
    {
        // Explicit schema names also work through transaction poolers, which may reset search_path.
        foreach (var table in new[] { "lumiere_documents", "lumiere_media", "lumiere_keys", "lumiere_cache" })
            sql = System.Text.RegularExpressions.Regex.Replace(sql, $@"\b{table}\b", $"\"{_schema}\".{table}");
        return sql;
    }
    internal NpgsqlCommand CreateCommand(string sql) => _source!.CreateCommand(Sql(sql));
    public void Dispose() => _source?.Dispose();
}

/// <summary>Persist ASP.NET sessions across Render restarts, including pending payments.</summary>
public sealed class PostgresCache(PostgresState database) : IDistributedCache
{
    private const string ReadSql = """
        UPDATE lumiere_cache SET expires_at=CASE WHEN sliding_ms IS NULL THEN expires_at
        ELSE LEAST(clock_timestamp()+sliding_ms*interval '1 millisecond',COALESCE(absolute_at,'infinity')) END
        WHERE id=@id AND expires_at>clock_timestamp() RETURNING value
        """;
    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        await using var cmd = database.CreateCommand(ReadSql);
        cmd.Parameters.AddWithValue("id", key);
        return await cmd.ExecuteScalarAsync(token) as byte[];
    }
    public void Refresh(string key) => Get(key);
    public async Task RefreshAsync(string key, CancellationToken token = default) => _ = await GetAsync(key, token);
    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        await using var cmd = database.CreateCommand("DELETE FROM lumiere_cache WHERE id=@id");
        cmd.Parameters.AddWithValue("id", key);
        await cmd.ExecuteNonQueryAsync(token);
    }
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => SetAsync(key, value, options).GetAwaiter().GetResult();
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        var now = DateTimeOffset.UtcNow;
        var absolute = options.AbsoluteExpiration ?? (options.AbsoluteExpirationRelativeToNow is { } duration ? now + duration : (DateTimeOffset?)null);
        var expires = options.SlidingExpiration is { } sliding ? now + sliding : absolute ?? now.AddHours(3);
        if (absolute is { } limit && expires > limit) expires = limit;
        await using var cmd = database.CreateCommand("""
            DELETE FROM lumiere_cache WHERE expires_at<clock_timestamp();
            INSERT INTO lumiere_cache(id,value,expires_at,sliding_ms,absolute_at) VALUES (@id,@value,@expires,@sliding,@absolute)
            ON CONFLICT(id) DO UPDATE SET value=EXCLUDED.value,expires_at=EXCLUDED.expires_at,
            sliding_ms=EXCLUDED.sliding_ms,absolute_at=EXCLUDED.absolute_at
            """);
        cmd.Parameters.AddWithValue("id", key);
        cmd.Parameters.AddWithValue("value", value);
        cmd.Parameters.AddWithValue("expires", expires);
        cmd.Parameters.AddWithValue("sliding", NpgsqlDbType.Bigint, (object?)options.SlidingExpiration?.TotalMilliseconds is double ms ? (long)ms : DBNull.Value);
        cmd.Parameters.AddWithValue("absolute", NpgsqlDbType.TimestampTz, (object?)absolute ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(token);
    }
}
