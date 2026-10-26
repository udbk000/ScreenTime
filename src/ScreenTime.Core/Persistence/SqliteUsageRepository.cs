using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Models;

namespace ScreenTime.Core.Persistence;

/// <summary>
/// Thread-sicheres SQLite-Repository für Apps, Kategorien, Nutzungsintervalle und Einstellungen.
/// </summary>
public sealed class SqliteUsageRepository : IUsageRepository
{
    private readonly string _databasePath;
    private readonly IAppLogger _logger;
    private readonly object _syncRoot = new();
    private nint _db = nint.Zero;
    private bool _disposed;

    public SqliteUsageRepository(string databasePath, IAppLogger logger)
    {
        _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Initialize()
    {
        lock (_syncRoot)
        {
            if (_db != nint.Zero)
            {
                return;
            }

            string? dir = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            int rc = WinSqliteInterop.Open16(_databasePath, out _db);
            if (rc != WinSqliteInterop.SQLITE_OK)
            {
                string msg = WinSqliteInterop.GetErrorMessage(_db);
                throw new InvalidOperationException($"Datenbank konnte nicht geöffnet werden ({rc}): {msg}");
            }

            WinSqliteInterop.BusyTimeout(_db, 5000);
            WinSqliteInterop.ExecuteNonQuery(_db, "PRAGMA journal_mode = WAL;");
            WinSqliteInterop.ExecuteNonQuery(_db, "PRAGMA synchronous = NORMAL;");
            WinSqliteInterop.ExecuteNonQuery(_db, "PRAGMA foreign_keys = ON;");

            CreateSchemaAndMigrate();
            SeedDefaultCategories();
            SeedDefaultSettings();
        }
    }

    private void CreateSchemaAndMigrate()
    {
        WinSqliteInterop.ExecuteNonQuery(_db, @"
            CREATE TABLE IF NOT EXISTS schema_info (
                version INTEGER NOT NULL
            );");

        WinSqliteInterop.ExecuteNonQuery(_db, @"
            CREATE TABLE IF NOT EXISTS categories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                key TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                color_hex TEXT NOT NULL,
                icon_glyph TEXT NOT NULL,
                default_idle_seconds INTEGER NOT NULL,
                sort_order INTEGER NOT NULL
            );");

        WinSqliteInterop.ExecuteNonQuery(_db, @"
            CREATE TABLE IF NOT EXISTS applications (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                app_identifier TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL,
                process_name TEXT NOT NULL,
                executable_path TEXT NULL,
                icon_cache_path TEXT NULL,
                category_id INTEGER NOT NULL,
                custom_idle_seconds INTEGER NULL,
                is_user_customized INTEGER NOT NULL DEFAULT 0,
                first_seen_utc INTEGER NOT NULL,
                last_seen_utc INTEGER NOT NULL,
                FOREIGN KEY (category_id) REFERENCES categories(id)
            );");

        WinSqliteInterop.ExecuteNonQuery(_db, @"
            CREATE TABLE IF NOT EXISTS usage_intervals (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                application_id INTEGER NOT NULL,
                start_utc INTEGER NOT NULL,
                end_utc INTEGER NOT NULL,
                duration_seconds INTEGER NOT NULL,
                end_reason INTEGER NOT NULL,
                FOREIGN KEY (application_id) REFERENCES applications(id) ON DELETE CASCADE
            );");

        WinSqliteInterop.ExecuteNonQuery(_db, @"
            CREATE INDEX IF NOT EXISTS idx_intervals_time ON usage_intervals(start_utc, end_utc);");

        WinSqliteInterop.ExecuteNonQuery(_db, @"
            CREATE INDEX IF NOT EXISTS idx_intervals_app_time ON usage_intervals(application_id, start_utc, end_utc);");

        WinSqliteInterop.ExecuteNonQuery(_db, @"
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );");
    }

    private void SeedDefaultCategories()
    {
        var defaults = new (string Key, string Name, string ColorHex, string Glyph, int IdleSeconds, int Sort)[]
        {
            ("learning", "Lernen & Lesen", "#5856D6", "\uE736", 900, 1),
            ("productivity", "Produktivität", "#007AFF", "\uE8A5", 300, 2),
            ("communication", "Kommunikation", "#34C759", "\uE8BD", 180, 3),
            ("creativity", "Kreativität", "#AF52DE", "\uE790", 300, 4),
            ("entertainment", "Unterhaltung", "#FF2D55", "\uE714", 300, 5),
            ("games", "Spiele", "#FF9500", "\uE7FC", 180, 6),
            ("utilities", "Dienstprogramme", "#5AC8FA", "\uE90F", 120, 7),
            ("other", "Sonstiges", "#8E8E93", "\uE71D", 60, 8)
        };

        foreach (var item in defaults)
        {
            WinSqliteInterop.ExecuteNonQuery(_db, @"
                INSERT OR IGNORE INTO categories (key, name, color_hex, icon_glyph, default_idle_seconds, sort_order)
                VALUES (?, ?, ?, ?, ?, ?);",
                stmt =>
                {
                    WinSqliteInterop.BindStringOrNull(stmt, 1, item.Key);
                    WinSqliteInterop.BindStringOrNull(stmt, 2, item.Name);
                    WinSqliteInterop.BindStringOrNull(stmt, 3, item.ColorHex);
                    WinSqliteInterop.BindStringOrNull(stmt, 4, item.Glyph);
                    WinSqliteInterop.BindInt64(stmt, 5, item.IdleSeconds);
                    WinSqliteInterop.BindInt64(stmt, 6, item.Sort);
                });
        }
    }

    private void SeedDefaultSettings()
    {
        var defaults = new AppSettings();
        InsertSettingIfMissing("GlobalIdleThresholdSeconds", defaults.GlobalIdleThresholdSeconds.ToString());
        InsertSettingIfMissing("Theme", ((int)defaults.Theme).ToString());
        InsertSettingIfMissing("LaunchAtStartup", defaults.LaunchAtStartup ? "1" : "0");
        InsertSettingIfMissing("MinimizeToTrayOnClose", defaults.MinimizeToTrayOnClose ? "1" : "0");
        InsertSettingIfMissing("DataRetentionDays", defaults.DataRetentionDays.ToString());
        InsertSettingIfMissing("PollingIntervalMs", defaults.PollingIntervalMs.ToString());
    }

    private void InsertSettingIfMissing(string key, string value)
    {
        WinSqliteInterop.ExecuteNonQuery(_db,
            "INSERT OR IGNORE INTO settings (key, value) VALUES (?, ?);",
            stmt =>
            {
                WinSqliteInterop.BindStringOrNull(stmt, 1, key);
                WinSqliteInterop.BindStringOrNull(stmt, 2, value);
            });
    }

    public IReadOnlyList<AppCategory> GetCategories()
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            var list = new List<AppCategory>();
            const string sql = @"
                SELECT id, key, name, color_hex, icon_glyph, default_idle_seconds, sort_order
                FROM categories
                ORDER BY sort_order ASC, id ASC;";

            int rc = WinSqliteInterop.Prepare16V2(_db, sql, -1, out nint stmt, out _);
            if (rc != WinSqliteInterop.SQLITE_OK)
            {
                throw new InvalidOperationException(WinSqliteInterop.GetErrorMessage(_db));
            }

            try
            {
                while (WinSqliteInterop.Step(stmt) == WinSqliteInterop.SQLITE_ROW)
                {
                    list.Add(ReadCategory(stmt));
                }
            }
            finally
            {
                WinSqliteInterop.Finalize(stmt);
            }

            return list;
        }
    }

    public AppCategory? GetCategoryById(long id)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            const string sql = @"
                SELECT id, key, name, color_hex, icon_glyph, default_idle_seconds, sort_order
                FROM categories
                WHERE id = ?;";

            int rc = WinSqliteInterop.Prepare16V2(_db, sql, -1, out nint stmt, out _);
            if (rc != WinSqliteInterop.SQLITE_OK)
            {
                throw new InvalidOperationException(WinSqliteInterop.GetErrorMessage(_db));
            }

            try
            {
                WinSqliteInterop.BindInt64(stmt, 1, id);
                if (WinSqliteInterop.Step(stmt) == WinSqliteInterop.SQLITE_ROW)
                {
                    return ReadCategory(stmt);
                }
                return null;
            }
            finally
            {
                WinSqliteInterop.Finalize(stmt);
            }
        }
    }

    public AppCategory CreateCategory(string name, string colorHex, string iconGlyph, int defaultIdleSeconds)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            string cleanName = string.IsNullOrWhiteSpace(name) ? "Neue Kategorie" : name.Trim();
            string key = $"custom_{Guid.NewGuid():N}";
            string hex = string.IsNullOrWhiteSpace(colorHex) ? "#007AFF" : colorHex.Trim();
            string glyph = string.IsNullOrWhiteSpace(iconGlyph) ? "\uE8FD" : iconGlyph;
            int idleSecs = Math.Clamp(defaultIdleSeconds, 15, 7200);
            int sortOrder = GetCategories().Count + 1;

            WinSqliteInterop.ExecuteNonQuery(_db, @"
                INSERT INTO categories (key, name, color_hex, icon_glyph, default_idle_seconds, sort_order)
                VALUES (?, ?, ?, ?, ?, ?);",
                stmt =>
                {
                    WinSqliteInterop.BindStringOrNull(stmt, 1, key);
                    WinSqliteInterop.BindStringOrNull(stmt, 2, cleanName);
                    WinSqliteInterop.BindStringOrNull(stmt, 3, hex);
                    WinSqliteInterop.BindStringOrNull(stmt, 4, glyph);
                    WinSqliteInterop.BindInt64(stmt, 5, idleSecs);
                    WinSqliteInterop.BindInt64(stmt, 6, sortOrder);
                });

            long newId = WinSqliteInterop.LastInsertRowId(_db);
            return new AppCategory
            {
                Id = newId,
                Key = key,
                Name = cleanName,
                ColorHex = hex,
                IconGlyph = glyph,
                DefaultIdleThresholdSeconds = idleSecs,
                SortOrder = sortOrder
            };
        }
    }

    public void UpdateCategory(AppCategory category)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            WinSqliteInterop.ExecuteNonQuery(_db, @"
                UPDATE categories
                SET name = ?, color_hex = ?, icon_glyph = ?, default_idle_seconds = ?
                WHERE id = ?;",
                stmt =>
                {
                    WinSqliteInterop.BindStringOrNull(stmt, 1, category.Name);
                    WinSqliteInterop.BindStringOrNull(stmt, 2, category.ColorHex);
                    WinSqliteInterop.BindStringOrNull(stmt, 3, category.IconGlyph);
                    WinSqliteInterop.BindInt64(stmt, 4, Math.Max(15, category.DefaultIdleThresholdSeconds));
                    WinSqliteInterop.BindInt64(stmt, 5, category.Id);
                });
        }
    }

    public void DeleteCategory(long categoryId)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            var categories = GetCategories();
            var target = categories.FirstOrDefault(c => c.Id == categoryId);
            if (target is null || !target.IsCustom)
            {
                return;
            }

            long fallbackCatId = categories.FirstOrDefault(c => c.Key.Equals("other", StringComparison.OrdinalIgnoreCase))?.Id
                              ?? categories.First(c => c.Id != categoryId).Id;

            WinSqliteInterop.ExecuteNonQuery(_db,
                "UPDATE applications SET category_id = ? WHERE category_id = ?;",
                stmt =>
                {
                    WinSqliteInterop.BindInt64(stmt, 1, fallbackCatId);
                    WinSqliteInterop.BindInt64(stmt, 2, categoryId);
                });

            WinSqliteInterop.ExecuteNonQuery(_db,
                "DELETE FROM categories WHERE id = ?;",
                stmt => WinSqliteInterop.BindInt64(stmt, 1, categoryId));
        }
    }

    public TrackedApplication? GetApplicationByIdentifier(string appIdentifier)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            const string sql = @"
                SELECT id, app_identifier, display_name, process_name, executable_path, icon_cache_path,
                       category_id, custom_idle_seconds, is_user_customized, first_seen_utc, last_seen_utc
                FROM applications
                WHERE app_identifier = ?;";

            int rc = WinSqliteInterop.Prepare16V2(_db, sql, -1, out nint stmt, out _);
            if (rc != WinSqliteInterop.SQLITE_OK)
            {
                throw new InvalidOperationException(WinSqliteInterop.GetErrorMessage(_db));
            }

            try
            {
                WinSqliteInterop.BindStringOrNull(stmt, 1, appIdentifier);
                if (WinSqliteInterop.Step(stmt) == WinSqliteInterop.SQLITE_ROW)
                {
                    return ReadApplication(stmt);
                }
                return null;
            }
            finally
            {
                WinSqliteInterop.Finalize(stmt);
            }
        }
    }

    public TrackedApplication? GetApplicationById(long id)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            const string sql = @"
                SELECT id, app_identifier, display_name, process_name, executable_path, icon_cache_path,
                       category_id, custom_idle_seconds, is_user_customized, first_seen_utc, last_seen_utc
                FROM applications
                WHERE id = ?;";

            int rc = WinSqliteInterop.Prepare16V2(_db, sql, -1, out nint stmt, out _);
            if (rc != WinSqliteInterop.SQLITE_OK)
            {
                throw new InvalidOperationException(WinSqliteInterop.GetErrorMessage(_db));
            }

            try
            {
                WinSqliteInterop.BindInt64(stmt, 1, id);
                if (WinSqliteInterop.Step(stmt) == WinSqliteInterop.SQLITE_ROW)
                {
                    return ReadApplication(stmt);
                }
                return null;
            }
            finally
            {
                WinSqliteInterop.Finalize(stmt);
            }
        }
    }

    public IReadOnlyList<TrackedApplication> GetAllApplications()
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            var list = new List<TrackedApplication>();
            const string sql = @"
                SELECT id, app_identifier, display_name, process_name, executable_path, icon_cache_path,
                       category_id, custom_idle_seconds, is_user_customized, first_seen_utc, last_seen_utc
                FROM applications
                ORDER BY display_name COLLATE NOCASE ASC;";

            int rc = WinSqliteInterop.Prepare16V2(_db, sql, -1, out nint stmt, out _);
            if (rc != WinSqliteInterop.SQLITE_OK)
            {
                throw new InvalidOperationException(WinSqliteInterop.GetErrorMessage(_db));
            }

            try
            {
                while (WinSqliteInterop.Step(stmt) == WinSqliteInterop.SQLITE_ROW)
                {
                    list.Add(ReadApplication(stmt));
                }
            }
            finally
            {
                WinSqliteInterop.Finalize(stmt);
            }

            return list;
        }
    }

    public TrackedApplication UpsertApplication(TrackedApplication app)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            var existing = GetApplicationByIdentifier(app.AppIdentifier);
            long nowEpoch = app.LastSeenUtc.ToUnixTimeSeconds();

            if (existing is null)
            {
                long firstEpoch = app.FirstSeenUtc.ToUnixTimeSeconds();
                WinSqliteInterop.ExecuteNonQuery(_db, @"
                    INSERT INTO applications (
                        app_identifier, display_name, process_name, executable_path, icon_cache_path,
                        category_id, custom_idle_seconds, is_user_customized, first_seen_utc, last_seen_utc
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);",
                    stmt =>
                    {
                        WinSqliteInterop.BindStringOrNull(stmt, 1, app.AppIdentifier);
                        WinSqliteInterop.BindStringOrNull(stmt, 2, app.DisplayName);
                        WinSqliteInterop.BindStringOrNull(stmt, 3, app.ProcessName);
                        WinSqliteInterop.BindStringOrNull(stmt, 4, app.ExecutablePath);
                        WinSqliteInterop.BindStringOrNull(stmt, 5, app.IconCachePath);
                        WinSqliteInterop.BindInt64(stmt, 6, app.CategoryId);
                        WinSqliteInterop.BindNullableLong(stmt, 7, app.CustomIdleThresholdSeconds);
                        WinSqliteInterop.BindInt64(stmt, 8, app.IsUserCustomized ? 1 : 0);
                        WinSqliteInterop.BindInt64(stmt, 9, firstEpoch);
                        WinSqliteInterop.BindInt64(stmt, 10, nowEpoch);
                    });

                app.Id = WinSqliteInterop.LastInsertRowId(_db);
                return app;
            }
            else
            {
                string displayName = existing.IsUserCustomized ? existing.DisplayName : app.DisplayName;
                long categoryId = existing.IsUserCustomized ? existing.CategoryId : existing.CategoryId;
                string? exePath = !string.IsNullOrWhiteSpace(app.ExecutablePath) ? app.ExecutablePath : existing.ExecutablePath;
                string? iconPath = !string.IsNullOrWhiteSpace(app.IconCachePath) ? app.IconCachePath : existing.IconCachePath;

                WinSqliteInterop.ExecuteNonQuery(_db, @"
                    UPDATE applications
                    SET display_name = ?,
                        executable_path = ?,
                        icon_cache_path = ?,
                        last_seen_utc = ?
                    WHERE id = ?;",
                    stmt =>
                    {
                        WinSqliteInterop.BindStringOrNull(stmt, 1, displayName);
                        WinSqliteInterop.BindStringOrNull(stmt, 2, exePath);
                        WinSqliteInterop.BindStringOrNull(stmt, 3, iconPath);
                        WinSqliteInterop.BindInt64(stmt, 4, nowEpoch);
                        WinSqliteInterop.BindInt64(stmt, 5, existing.Id);
                    });

                existing.DisplayName = displayName;
                existing.CategoryId = categoryId;
                existing.ExecutablePath = exePath;
                existing.IconCachePath = iconPath;
                existing.LastSeenUtc = app.LastSeenUtc;
                return existing;
            }
        }
    }

    public void UpdateApplicationCustomization(long appId, string displayName, long categoryId, int? customIdleThresholdSeconds)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            WinSqliteInterop.ExecuteNonQuery(_db, @"
                UPDATE applications
                SET display_name = ?,
                    category_id = ?,
                    custom_idle_seconds = ?,
                    is_user_customized = 1
                WHERE id = ?;",
                stmt =>
                {
                    WinSqliteInterop.BindStringOrNull(stmt, 1, displayName.Trim());
                    WinSqliteInterop.BindInt64(stmt, 2, categoryId);
                    WinSqliteInterop.BindNullableLong(stmt, 3, customIdleThresholdSeconds);
                    WinSqliteInterop.BindInt64(stmt, 4, appId);
                });
        }
    }

    public TrackedApplication AddOrUpdateProgramEntry(
        string displayName,
        string executableOrProcessName,
        long categoryId,
        int? customIdleSeconds,
        string? iconCachePath)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            string raw = executableOrProcessName.Trim().Trim('"');
            string procName = Path.GetFileNameWithoutExtension(raw);
            if (string.IsNullOrWhiteSpace(procName))
            {
                procName = raw;
            }

            string appIdentifier = Services.ApplicationResolver.GetCanonicalAppIdentifier(procName);
            string cleanDisplay = string.IsNullOrWhiteSpace(displayName)
                ? Services.ApplicationResolver.FormatFallbackName(procName)
                : displayName.Trim();

            var existing = GetApplicationByIdentifier(appIdentifier);
            if (existing is not null)
            {
                UpdateApplicationCustomization(existing.Id, cleanDisplay, categoryId, customIdleSeconds);
                if (!string.IsNullOrWhiteSpace(iconCachePath))
                {
                    WinSqliteInterop.ExecuteNonQuery(_db,
                        "UPDATE applications SET icon_cache_path = ?, executable_path = COALESCE(?, executable_path) WHERE id = ?;",
                        stmt =>
                        {
                            WinSqliteInterop.BindStringOrNull(stmt, 1, iconCachePath);
                            WinSqliteInterop.BindStringOrNull(stmt, 2, raw.Contains('\\') || raw.Contains('/') ? raw : null);
                            WinSqliteInterop.BindInt64(stmt, 3, existing.Id);
                        });
                }
                return GetApplicationById(existing.Id)!;
            }

            return UpsertApplication(new TrackedApplication
            {
                AppIdentifier = appIdentifier,
                DisplayName = cleanDisplay,
                ProcessName = procName,
                ExecutablePath = (raw.Contains('\\') || raw.Contains('/')) ? raw : null,
                IconCachePath = iconCachePath,
                CategoryId = categoryId,
                CustomIdleThresholdSeconds = customIdleSeconds,
                IsUserCustomized = true,
                FirstSeenUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow
            });
        }
    }

    public TrackedApplication AddOrUpdateWebsiteEntry(
        string displayName,
        string websiteUrlOrDomain,
        long categoryId,
        int? customIdleSeconds)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            string normalizedDomain = Services.ApplicationResolver.NormalizeWebsiteDomain(websiteUrlOrDomain);
            string appIdentifier = $"web:{normalizedDomain}";
            string cleanDisplay = string.IsNullOrWhiteSpace(displayName)
                ? Services.ApplicationResolver.FormatWebsiteDisplayName(normalizedDomain)
                : displayName.Trim();

            var existing = GetApplicationByIdentifier(appIdentifier);
            if (existing is not null)
            {
                UpdateApplicationCustomization(existing.Id, cleanDisplay, categoryId, customIdleSeconds);
                WinSqliteInterop.ExecuteNonQuery(_db,
                    "UPDATE applications SET executable_path = ? WHERE id = ?;",
                    stmt =>
                    {
                        WinSqliteInterop.BindStringOrNull(stmt, 1, websiteUrlOrDomain.Trim());
                        WinSqliteInterop.BindInt64(stmt, 2, existing.Id);
                    });
                return GetApplicationById(existing.Id)!;
            }

            return UpsertApplication(new TrackedApplication
            {
                AppIdentifier = appIdentifier,
                DisplayName = cleanDisplay,
                ProcessName = $"Web: {normalizedDomain}",
                ExecutablePath = websiteUrlOrDomain.Trim(),
                CategoryId = categoryId,
                CustomIdleThresholdSeconds = customIdleSeconds,
                IsUserCustomized = true,
                FirstSeenUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow
            });
        }
    }

    public long InsertUsageInterval(UsageInterval interval)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            long startEpoch = interval.StartTimeUtc.ToUnixTimeSeconds();
            long endEpoch = interval.EndTimeUtc.ToUnixTimeSeconds();
            int duration = Math.Max(0, (int)(endEpoch - startEpoch));

            WinSqliteInterop.ExecuteNonQuery(_db, @"
                INSERT INTO usage_intervals (application_id, start_utc, end_utc, duration_seconds, end_reason)
                VALUES (?, ?, ?, ?, ?);",
                stmt =>
                {
                    WinSqliteInterop.BindInt64(stmt, 1, interval.ApplicationId);
                    WinSqliteInterop.BindInt64(stmt, 2, startEpoch);
                    WinSqliteInterop.BindInt64(stmt, 3, endEpoch);
                    WinSqliteInterop.BindInt64(stmt, 4, duration);
                    WinSqliteInterop.BindInt64(stmt, 5, (long)interval.EndReason);
                });

            interval.Id = WinSqliteInterop.LastInsertRowId(_db);
            interval.DurationSeconds = duration;
            return interval.Id;
        }
    }

    public void UpdateUsageInterval(UsageInterval interval)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            long startEpoch = interval.StartTimeUtc.ToUnixTimeSeconds();
            long endEpoch = interval.EndTimeUtc.ToUnixTimeSeconds();
            int duration = Math.Max(0, (int)(endEpoch - startEpoch));

            WinSqliteInterop.ExecuteNonQuery(_db, @"
                UPDATE usage_intervals
                SET start_utc = ?,
                    end_utc = ?,
                    duration_seconds = ?,
                    end_reason = ?
                WHERE id = ?;",
                stmt =>
                {
                    WinSqliteInterop.BindInt64(stmt, 1, startEpoch);
                    WinSqliteInterop.BindInt64(stmt, 2, endEpoch);
                    WinSqliteInterop.BindInt64(stmt, 3, duration);
                    WinSqliteInterop.BindInt64(stmt, 4, (long)interval.EndReason);
                    WinSqliteInterop.BindInt64(stmt, 5, interval.Id);
                });

            interval.DurationSeconds = duration;
        }
    }

    public IReadOnlyList<UsageInterval> GetIntervalsInRange(DateTimeOffset rangeStartUtc, DateTimeOffset rangeEndUtc)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            long startEpoch = rangeStartUtc.ToUnixTimeSeconds();
            long endEpoch = rangeEndUtc.ToUnixTimeSeconds();

            var list = new List<UsageInterval>();
            const string sql = @"
                SELECT id, application_id, start_utc, end_utc, duration_seconds, end_reason
                FROM usage_intervals
                WHERE end_utc > ? AND start_utc < ? AND end_utc > start_utc
                ORDER BY start_utc ASC;";

            int rc = WinSqliteInterop.Prepare16V2(_db, sql, -1, out nint stmt, out _);
            if (rc != WinSqliteInterop.SQLITE_OK)
            {
                throw new InvalidOperationException(WinSqliteInterop.GetErrorMessage(_db));
            }

            try
            {
                WinSqliteInterop.BindInt64(stmt, 1, startEpoch);
                WinSqliteInterop.BindInt64(stmt, 2, endEpoch);
                while (WinSqliteInterop.Step(stmt) == WinSqliteInterop.SQLITE_ROW)
                {
                    list.Add(ReadInterval(stmt));
                }
            }
            finally
            {
                WinSqliteInterop.Finalize(stmt);
            }

            return list;
        }
    }

    public IReadOnlyList<UsageInterval> GetIntervalsForAppInRange(long applicationId, DateTimeOffset rangeStartUtc, DateTimeOffset rangeEndUtc)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            long startEpoch = rangeStartUtc.ToUnixTimeSeconds();
            long endEpoch = rangeEndUtc.ToUnixTimeSeconds();

            var list = new List<UsageInterval>();
            const string sql = @"
                SELECT id, application_id, start_utc, end_utc, duration_seconds, end_reason
                FROM usage_intervals
                WHERE application_id = ? AND end_utc > ? AND start_utc < ? AND end_utc > start_utc
                ORDER BY start_utc ASC;";

            int rc = WinSqliteInterop.Prepare16V2(_db, sql, -1, out nint stmt, out _);
            if (rc != WinSqliteInterop.SQLITE_OK)
            {
                throw new InvalidOperationException(WinSqliteInterop.GetErrorMessage(_db));
            }

            try
            {
                WinSqliteInterop.BindInt64(stmt, 1, applicationId);
                WinSqliteInterop.BindInt64(stmt, 2, startEpoch);
                WinSqliteInterop.BindInt64(stmt, 3, endEpoch);
                while (WinSqliteInterop.Step(stmt) == WinSqliteInterop.SQLITE_ROW)
                {
                    list.Add(ReadInterval(stmt));
                }
            }
            finally
            {
                WinSqliteInterop.Finalize(stmt);
            }

            return list;
        }
    }

    public DateTimeOffset? GetFirstRecordedTimestampUtc()
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            const string sql = "SELECT MIN(start_utc) FROM usage_intervals WHERE duration_seconds > 0;";

            int rc = WinSqliteInterop.Prepare16V2(_db, sql, -1, out nint stmt, out _);
            if (rc != WinSqliteInterop.SQLITE_OK)
            {
                throw new InvalidOperationException(WinSqliteInterop.GetErrorMessage(_db));
            }

            try
            {
                if (WinSqliteInterop.Step(stmt) == WinSqliteInterop.SQLITE_ROW)
                {
                    long? minStart = WinSqliteInterop.GetNullableLong(stmt, 0);
                    if (minStart.HasValue && minStart.Value > 0)
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(minStart.Value);
                    }
                }
                return null;
            }
            finally
            {
                WinSqliteInterop.Finalize(stmt);
            }
        }
    }

    public void RecoverUnclosedIntervals()
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            WinSqliteInterop.ExecuteNonQuery(_db, @"
                UPDATE usage_intervals
                SET end_reason = ?,
                    duration_seconds = MAX(0, end_utc - start_utc)
                WHERE end_reason = ?;",
                stmt =>
                {
                    WinSqliteInterop.BindInt64(stmt, 1, (long)InterruptionReason.RecoveredAfterRestart);
                    WinSqliteInterop.BindInt64(stmt, 2, (long)InterruptionReason.Active);
                });
        }
    }

    public void PurgeOldIntervals(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            EnsureInitialized();
            long cutoffEpoch = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToUnixTimeSeconds();
            WinSqliteInterop.ExecuteNonQuery(_db,
                "DELETE FROM usage_intervals WHERE end_utc < ?;",
                stmt => WinSqliteInterop.BindInt64(stmt, 1, cutoffEpoch));
        }
    }

    public void ClearAllUsageData()
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            WinSqliteInterop.ExecuteNonQuery(_db, "DELETE FROM usage_intervals;");
        }
    }

    public AppSettings GetSettings()
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            var settings = new AppSettings();
            const string sql = "SELECT key, value FROM settings;";

            int rc = WinSqliteInterop.Prepare16V2(_db, sql, -1, out nint stmt, out _);
            if (rc != WinSqliteInterop.SQLITE_OK)
            {
                throw new InvalidOperationException(WinSqliteInterop.GetErrorMessage(_db));
            }

            try
            {
                while (WinSqliteInterop.Step(stmt) == WinSqliteInterop.SQLITE_ROW)
                {
                    string key = WinSqliteInterop.GetString(stmt, 0);
                    string val = WinSqliteInterop.GetString(stmt, 1);

                    switch (key)
                    {
                        case "GlobalIdleThresholdSeconds" when int.TryParse(val, out int idle):
                            settings.GlobalIdleThresholdSeconds = Math.Clamp(idle, 15, 7200);
                            break;
                        case "Theme" when int.TryParse(val, out int theme) && Enum.IsDefined(typeof(ThemePreference), theme):
                            settings.Theme = (ThemePreference)theme;
                            break;
                        case "LaunchAtStartup":
                            settings.LaunchAtStartup = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "MinimizeToTrayOnClose":
                            settings.MinimizeToTrayOnClose = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "DataRetentionDays" when int.TryParse(val, out int retention):
                            settings.DataRetentionDays = Math.Max(0, retention);
                            break;
                        case "PollingIntervalMs" when int.TryParse(val, out int poll):
                            settings.PollingIntervalMs = Math.Clamp(poll, 250, 5000);
                            break;
                    }
                }
            }
            finally
            {
                WinSqliteInterop.Finalize(stmt);
            }

            return settings;
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            UpsertSetting("GlobalIdleThresholdSeconds", settings.GlobalIdleThresholdSeconds.ToString());
            UpsertSetting("Theme", ((int)settings.Theme).ToString());
            UpsertSetting("LaunchAtStartup", settings.LaunchAtStartup ? "1" : "0");
            UpsertSetting("MinimizeToTrayOnClose", settings.MinimizeToTrayOnClose ? "1" : "0");
            UpsertSetting("DataRetentionDays", settings.DataRetentionDays.ToString());
            UpsertSetting("PollingIntervalMs", settings.PollingIntervalMs.ToString());
        }
    }

    private void UpsertSetting(string key, string value)
    {
        WinSqliteInterop.ExecuteNonQuery(_db, @"
            INSERT INTO settings (key, value) VALUES (?, ?)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            stmt =>
            {
                WinSqliteInterop.BindStringOrNull(stmt, 1, key);
                WinSqliteInterop.BindStringOrNull(stmt, 2, value);
            });
    }

    private static AppCategory ReadCategory(nint stmt)
    {
        return new AppCategory
        {
            Id = WinSqliteInterop.ColumnInt64(stmt, 0),
            Key = WinSqliteInterop.GetString(stmt, 1),
            Name = WinSqliteInterop.GetString(stmt, 2),
            ColorHex = WinSqliteInterop.GetString(stmt, 3),
            IconGlyph = WinSqliteInterop.GetString(stmt, 4),
            DefaultIdleThresholdSeconds = (int)WinSqliteInterop.ColumnInt64(stmt, 5),
            SortOrder = (int)WinSqliteInterop.ColumnInt64(stmt, 6)
        };
    }

    private static TrackedApplication ReadApplication(nint stmt)
    {
        long? customIdle = WinSqliteInterop.GetNullableLong(stmt, 7);
        return new TrackedApplication
        {
            Id = WinSqliteInterop.ColumnInt64(stmt, 0),
            AppIdentifier = WinSqliteInterop.GetString(stmt, 1),
            DisplayName = WinSqliteInterop.GetString(stmt, 2),
            ProcessName = WinSqliteInterop.GetString(stmt, 3),
            ExecutablePath = WinSqliteInterop.GetNullableString(stmt, 4),
            IconCachePath = WinSqliteInterop.GetNullableString(stmt, 5),
            CategoryId = WinSqliteInterop.ColumnInt64(stmt, 6),
            CustomIdleThresholdSeconds = customIdle.HasValue ? (int)customIdle.Value : null,
            IsUserCustomized = WinSqliteInterop.ColumnInt64(stmt, 8) != 0,
            FirstSeenUtc = DateTimeOffset.FromUnixTimeSeconds(WinSqliteInterop.ColumnInt64(stmt, 9)),
            LastSeenUtc = DateTimeOffset.FromUnixTimeSeconds(WinSqliteInterop.ColumnInt64(stmt, 10))
        };
    }

    private static UsageInterval ReadInterval(nint stmt)
    {
        return new UsageInterval
        {
            Id = WinSqliteInterop.ColumnInt64(stmt, 0),
            ApplicationId = WinSqliteInterop.ColumnInt64(stmt, 1),
            StartTimeUtc = DateTimeOffset.FromUnixTimeSeconds(WinSqliteInterop.ColumnInt64(stmt, 2)),
            EndTimeUtc = DateTimeOffset.FromUnixTimeSeconds(WinSqliteInterop.ColumnInt64(stmt, 3)),
            DurationSeconds = (int)WinSqliteInterop.ColumnInt64(stmt, 4),
            EndReason = (InterruptionReason)WinSqliteInterop.ColumnInt64(stmt, 5)
        };
    }

    private void EnsureInitialized()
    {
        if (_db == nint.Zero)
        {
            Initialize();
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_db != nint.Zero)
            {
                WinSqliteInterop.CloseV2(_db);
                _db = nint.Zero;
            }
        }
    }
}
