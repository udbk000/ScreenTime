using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Models;

namespace ScreenTime.Core.Services;

/// <summary>
/// Optionaler Hilfsdienst in den Einstellungen/Diagnose, um realistische Verlaufsdaten über 6 Wochen
/// zum Testen der Diagramme zu erzeugen (wird niemals automatisch ohne Benutzerwunsch ausgeführt).
/// </summary>
public static class DemoDataSeeder
{
    public static void SeedRealisticHistory(IUsageRepository repository, int daysBack = 42)
    {
        var categories = repository.GetCategories().ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        long learningId = categories.GetValueOrDefault("learning")?.Id ?? 1;
        long prodId = categories.GetValueOrDefault("productivity")?.Id ?? 2;
        long commId = categories.GetValueOrDefault("communication")?.Id ?? 3;
        long entId = categories.GetValueOrDefault("entertainment")?.Id ?? 5;

        var sampleApps = new[]
        {
            new TrackedApplication { AppIdentifier = "adobe-acrobat", DisplayName = "Adobe Acrobat Reader", ProcessName = "AcroRd32", CategoryId = learningId },
            new TrackedApplication { AppIdentifier = "obsidian", DisplayName = "Obsidian", ProcessName = "Obsidian", CategoryId = learningId },
            new TrackedApplication { AppIdentifier = "anki", DisplayName = "Anki", ProcessName = "anki", CategoryId = learningId },
            new TrackedApplication { AppIdentifier = "vscode", DisplayName = "Visual Studio Code", ProcessName = "Code", CategoryId = prodId },
            new TrackedApplication { AppIdentifier = "google-chrome", DisplayName = "Google Chrome", ProcessName = "chrome", CategoryId = prodId },
            new TrackedApplication { AppIdentifier = "discord", DisplayName = "Discord", ProcessName = "Discord", CategoryId = commId },
            new TrackedApplication { AppIdentifier = "spotify", DisplayName = "Spotify", ProcessName = "Spotify", CategoryId = entId }
        };

        var persistedApps = sampleApps.Select(repository.UpsertApplication).ToList();
        var rng = new Random(42);
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);

        for (int i = daysBack - 1; i >= 0; i--)
        {
            DateOnly date = today.AddDays(-i);
            var localMidnight = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));

            // Vormittags: Lernen mit PDF & Obsidian (9:00 - 12:30)
            var cursor = localMidnight.AddHours(9).AddMinutes(rng.Next(0, 30));
            for (int session = 0; session < 4; session++)
            {
                var app = persistedApps[rng.Next(0, 3)]; // Lern-Apps
                int minutes = rng.Next(20, 50);
                var end = cursor.AddMinutes(minutes);
                repository.InsertUsageInterval(new UsageInterval
                {
                    ApplicationId = app.Id,
                    StartTimeUtc = cursor.ToUniversalTime(),
                    EndTimeUtc = end.ToUniversalTime(),
                    DurationSeconds = minutes * 60,
                    EndReason = InterruptionReason.AppSwitch
                });
                cursor = end.AddMinutes(rng.Next(5, 15));
            }

            // Nachmittags: Produktivität & Kommunikation (14:00 - 18:00)
            cursor = localMidnight.AddHours(14).AddMinutes(rng.Next(0, 30));
            for (int session = 0; session < 4; session++)
            {
                var app = persistedApps[rng.Next(3, persistedApps.Count)];
                int minutes = rng.Next(15, 45);
                var end = cursor.AddMinutes(minutes);
                repository.InsertUsageInterval(new UsageInterval
                {
                    ApplicationId = app.Id,
                    StartTimeUtc = cursor.ToUniversalTime(),
                    EndTimeUtc = end.ToUniversalTime(),
                    DurationSeconds = minutes * 60,
                    EndReason = InterruptionReason.AppSwitch
                });
                cursor = end.AddMinutes(rng.Next(5, 20));
            }
        }
    }
}
