using System.Globalization;
using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Models;

namespace ScreenTime.Core.Services;

/// <summary>
/// Berechnet alle Tages-, Wochen-, Monats- (30 Tage) und 6-Wochen-Durchschnittsstatistiken
/// sowie Kategorie- und App-Auswertungen konsistent und zeitzonenkorrekt.
/// </summary>
public sealed class StatisticsService : IStatisticsService
{
    private readonly IUsageRepository _repository;
    private static readonly CultureInfo DeCulture = new("de-DE");

    public StatisticsService(IUsageRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public int GetEffectiveIdleThresholdSeconds(TrackedApplication? app, AppCategory? category, AppSettings settings)
        => TrackingService.GetEffectiveIdleThresholdSeconds(app, category, settings);

    public DayDetailStatistics GetDayStatistics(DateOnly date)
    {
        var (dayStartUtc, dayEndUtc) = GetLocalDayBoundsUtc(date);
        var intervals = _repository.GetIntervalsInRange(dayStartUtc, dayEndUtc);
        var categories = _repository.GetCategories();
        var apps = _repository.GetAllApplications().ToDictionary(a => a.Id);
        var settings = _repository.GetSettings();

        var hourlySeconds = new double[24];
        var appSeconds = new Dictionary<long, double>();
        var appSessions = new Dictionary<long, int>();

        foreach (var interval in intervals)
        {
            DateTimeOffset clippedStart = interval.StartTimeUtc < dayStartUtc ? dayStartUtc : interval.StartTimeUtc;
            DateTimeOffset clippedEnd = interval.EndTimeUtc > dayEndUtc ? dayEndUtc : interval.EndTimeUtc;
            double seconds = (clippedEnd - clippedStart).TotalSeconds;
            if (seconds <= 0)
            {
                continue;
            }

            appSeconds[interval.ApplicationId] = appSeconds.GetValueOrDefault(interval.ApplicationId) + seconds;
            appSessions[interval.ApplicationId] = appSessions.GetValueOrDefault(interval.ApplicationId) + 1;

            DistributeIntoHourlyBuckets(clippedStart.ToLocalTime(), clippedEnd.ToLocalTime(), date, hourlySeconds);
        }

        var (categoriesSummary, appsSummary, totalDuration) = BuildCategoriesAndAppsSummary(
            appSeconds, appSessions, apps, categories, settings);

        var hourlyBuckets = Enumerable.Range(0, 24)
            .Select(h => new HourlyUsageBucket
            {
                Hour = h,
                Duration = TimeSpan.FromSeconds(Math.Round(hourlySeconds[h]))
            })
            .ToList();

        var sixWeekStats = GetSixWeekAverageStatistics(date);
        var sixWeekAvg = sixWeekStats.SixWeekAverageDailyDuration;

        double deltaPercent = 0;
        string comparisonText;
        if (sixWeekAvg.TotalSeconds > 0)
        {
            deltaPercent = ((totalDuration.TotalSeconds - sixWeekAvg.TotalSeconds) / sixWeekAvg.TotalSeconds) * 100.0;
            if (Math.Abs(deltaPercent) < 2.0)
            {
                comparisonText = $"Auf Niveau des 6-Wochen-Durchschnitts ({AppUsageSummary.FormatDuration(sixWeekAvg)})";
            }
            else if (deltaPercent > 0)
            {
                comparisonText = $"+{deltaPercent:0}% über dem 6-Wochen-Durchschnitt ({AppUsageSummary.FormatDuration(sixWeekAvg)})";
            }
            else
            {
                comparisonText = $"{deltaPercent:0}% unter dem 6-Wochen-Durchschnitt ({AppUsageSummary.FormatDuration(sixWeekAvg)})";
            }
        }
        else
        {
            comparisonText = "Noch keine Vergleichsdaten für den 6-Wochen-Durchschnitt";
        }

        string dateTitle = date == DateOnly.FromDateTime(DateTime.Now)
            ? $"Heute, {date.ToString("dd. MMMM yyyy", DeCulture)}"
            : date.ToString("dddd, dd. MMMM yyyy", DeCulture);

        return new DayDetailStatistics
        {
            Date = date,
            DateTitle = dateTitle,
            TotalDuration = totalDuration,
            SixWeekAverageDailyDuration = sixWeekAvg,
            DeltaPercentVsSixWeekAverage = deltaPercent,
            ComparisonDescription = comparisonText,
            HourlyBreakdown = hourlyBuckets,
            Categories = categoriesSummary,
            Apps = appsSummary
        };
    }

    public PeriodStatistics GetLast7DaysStatistics(DateOnly? referenceToday = null)
    {
        DateOnly endDate = referenceToday ?? DateOnly.FromDateTime(DateTime.Now);
        DateOnly startDate = endDate.AddDays(-6);
        return BuildPeriodStatistics(startDate, endDate, "Letzte 7 Tage");
    }

    public PeriodStatistics GetLast30DaysStatistics(DateOnly? referenceToday = null)
    {
        DateOnly endDate = referenceToday ?? DateOnly.FromDateTime(DateTime.Now);
        DateOnly startDate = endDate.AddDays(-29);
        return BuildPeriodStatistics(startDate, endDate, "Letzte 30 Tage (Monatsverlauf)");
    }

    public PeriodStatistics GetSixWeekAverageStatistics(DateOnly? referenceToday = null)
    {
        DateOnly endDate = referenceToday ?? DateOnly.FromDateTime(DateTime.Now);
        DateOnly startDate = endDate.AddDays(-41); // 42 Kalendertage = 6 Wochen
        return BuildPeriodStatistics(startDate, endDate, "6-Wochen-Durchschnitt (42 Tage)");
    }

    public AppDetailStatistics? GetAppDetailStatistics(long applicationId, DateOnly? referenceToday = null)
    {
        var app = _repository.GetApplicationById(applicationId);
        if (app is null)
        {
            return null;
        }

        var categories = _repository.GetCategories();
        var category = categories.FirstOrDefault(c => c.Id == app.CategoryId)
                    ?? categories.FirstOrDefault()
                    ?? new AppCategory { Id = 1, Name = "Sonstiges", ColorHex = "#8E8E93" };
        var settings = _repository.GetSettings();

        DateOnly today = referenceToday ?? DateOnly.FromDateTime(DateTime.Now);
        DateOnly start30 = today.AddDays(-29);
        DateOnly start7 = today.AddDays(-6);

        var (rangeStartUtc, _) = GetLocalDayBoundsUtc(start30);
        var (_, rangeEndUtc) = GetLocalDayBoundsUtc(today);
        var intervals = _repository.GetIntervalsForAppInRange(applicationId, rangeStartUtc, rangeEndUtc);

        DateOnly? firstRecordedDate = GetFirstRecordedLocalDate();

        var points30 = new List<DailyUsagePoint>(30);
        for (int i = 0; i < 30; i++)
        {
            DateOnly d = start30.AddDays(i);
            var (dStartUtc, dEndUtc) = GetLocalDayBoundsUtc(d);
            double daySec = 0;
            foreach (var iv in intervals)
            {
                if (iv.EndTimeUtc <= dStartUtc || iv.StartTimeUtc >= dEndUtc)
                {
                    continue;
                }
                var cs = iv.StartTimeUtc < dStartUtc ? dStartUtc : iv.StartTimeUtc;
                var ce = iv.EndTimeUtc > dEndUtc ? dEndUtc : iv.EndTimeUtc;
                if (ce > cs)
                {
                    daySec += (ce - cs).TotalSeconds;
                }
            }

            points30.Add(new DailyUsagePoint
            {
                Date = d,
                ShortDayLabel = d.ToString("dd.MM.", DeCulture),
                FullDateLabel = d.ToString("ddd, dd.MM.yyyy", DeCulture),
                TotalDuration = TimeSpan.FromSeconds(Math.Round(daySec)),
                IsBeforeFirstRecordedDay = firstRecordedDate.HasValue && d < firstRecordedDate.Value
            });
        }

        var points7 = points30.Where(p => p.Date >= start7).ToList();
        var todayPoint = points30.LastOrDefault();

        TimeSpan total7 = TimeSpan.FromSeconds(points7.Sum(p => p.TotalDuration.TotalSeconds));
        TimeSpan total30 = TimeSpan.FromSeconds(points30.Sum(p => p.TotalDuration.TotalSeconds));

        int days7Denom = CalculateEvaluatedDaysDenominator(start7, today, firstRecordedDate);
        int days30Denom = CalculateEvaluatedDaysDenominator(start30, today, firstRecordedDate);

        return new AppDetailStatistics
        {
            Application = app,
            Category = category,
            EffectiveIdleThresholdSeconds = GetEffectiveIdleThresholdSeconds(app, category, settings),
            TodayDuration = todayPoint?.TotalDuration ?? TimeSpan.Zero,
            Last7DaysTotalDuration = total7,
            Last7DaysDailyAverage = days7Denom > 0 ? TimeSpan.FromSeconds(Math.Round(total7.TotalSeconds / days7Denom)) : TimeSpan.Zero,
            Last30DaysTotalDuration = total30,
            Last30DaysDailyAverage = days30Denom > 0 ? TimeSpan.FromSeconds(Math.Round(total30.TotalSeconds / days30Denom)) : TimeSpan.Zero,
            Last7DaysPoints = points7,
            Last30DaysPoints = points30
        };
    }

    private PeriodStatistics BuildPeriodStatistics(DateOnly startDate, DateOnly endDate, string title)
    {
        var categories = _repository.GetCategories();
        var apps = _repository.GetAllApplications().ToDictionary(a => a.Id);
        var settings = _repository.GetSettings();

        DateOnly? firstRecordedDate = GetFirstRecordedLocalDate();

        // Wir laden die Intervalle für das Maximum aus (startDate) und dem 6-Wochen-Fenster (endDate - 41 Tage)
        DateOnly sixWeekStart = endDate.AddDays(-41);
        DateOnly queryStart = startDate < sixWeekStart ? startDate : sixWeekStart;

        var (queryStartUtc, _) = GetLocalDayBoundsUtc(queryStart);
        var (_, queryEndUtc) = GetLocalDayBoundsUtc(endDate);
        var allIntervals = _repository.GetIntervalsInRange(queryStartUtc, queryEndUtc);

        // Berechne 6-Wochen-Gesamtzeit (ab erstem Erfassungstag)
        DateOnly effectiveSixWeekStart = firstRecordedDate.HasValue && firstRecordedDate.Value > sixWeekStart
            ? firstRecordedDate.Value
            : sixWeekStart;

        double sixWeekTotalSeconds = 0;
        int sixWeekDaysCount = 0;

        if (firstRecordedDate.HasValue && firstRecordedDate.Value <= endDate)
        {
            sixWeekDaysCount = Math.Max(1, (endDate.DayNumber - effectiveSixWeekStart.DayNumber) + 1);
            var (swStartUtc, _) = GetLocalDayBoundsUtc(effectiveSixWeekStart);
            foreach (var iv in allIntervals)
            {
                if (iv.EndTimeUtc <= swStartUtc || iv.StartTimeUtc >= queryEndUtc)
                {
                    continue;
                }
                var cs = iv.StartTimeUtc < swStartUtc ? swStartUtc : iv.StartTimeUtc;
                var ce = iv.EndTimeUtc > queryEndUtc ? queryEndUtc : iv.EndTimeUtc;
                if (ce > cs)
                {
                    sixWeekTotalSeconds += (ce - cs).TotalSeconds;
                }
            }
        }

        TimeSpan sixWeekAvg = sixWeekDaysCount > 0
            ? TimeSpan.FromSeconds(Math.Round(sixWeekTotalSeconds / sixWeekDaysCount))
            : TimeSpan.Zero;

        string sixWeekExplanation = sixWeekDaysCount > 0 && firstRecordedDate.HasValue
            ? $"Durchschnittliche tägliche aktive Nutzungszeit über {sixWeekDaysCount} {(sixWeekDaysCount == 1 ? "Tag" : "Tage")} seit Erfassungsbeginn ({effectiveSixWeekStart.ToString("dd.MM.yyyy", DeCulture)} – {endDate.ToString("dd.MM.yyyy", DeCulture)}). Tage vor der ersten Nutzung werden ignoriert."
            : "Noch keine Nutzungsdaten erfasst. Der 6-Wochen-Durchschnitt berücksichtigt alle Tage ab dem ersten Erfassungstag.";

        // Berechne nun die Werte für den angeforderten Zeitraum [startDate .. endDate]
        int totalCalendarDays = (endDate.DayNumber - startDate.DayNumber) + 1;
        var dailyPoints = new List<DailyUsagePoint>(totalCalendarDays);
        var periodAppSeconds = new Dictionary<long, double>();
        var periodAppSessions = new Dictionary<long, int>();

        for (int i = 0; i < totalCalendarDays; i++)
        {
            DateOnly day = startDate.AddDays(i);
            var (dStartUtc, dEndUtc) = GetLocalDayBoundsUtc(day);
            double dayTotalSeconds = 0;
            var dayAppSeconds = new Dictionary<long, double>();

            foreach (var iv in allIntervals)
            {
                if (iv.EndTimeUtc <= dStartUtc || iv.StartTimeUtc >= dEndUtc)
                {
                    continue;
                }
                var cs = iv.StartTimeUtc < dStartUtc ? dStartUtc : iv.StartTimeUtc;
                var ce = iv.EndTimeUtc > dEndUtc ? dEndUtc : iv.EndTimeUtc;
                double secs = (ce - cs).TotalSeconds;
                if (secs <= 0)
                {
                    continue;
                }

                dayTotalSeconds += secs;
                dayAppSeconds[iv.ApplicationId] = dayAppSeconds.GetValueOrDefault(iv.ApplicationId) + secs;
                periodAppSeconds[iv.ApplicationId] = periodAppSeconds.GetValueOrDefault(iv.ApplicationId) + secs;
                periodAppSessions[iv.ApplicationId] = periodAppSessions.GetValueOrDefault(iv.ApplicationId) + 1;
            }

            string shortLabel = totalCalendarDays <= 7
                ? day.ToString("ddd", DeCulture)
                : day.ToString("dd.MM.", DeCulture);

            dailyPoints.Add(new DailyUsagePoint
            {
                Date = day,
                ShortDayLabel = shortLabel,
                FullDateLabel = day.ToString("dddd, dd.MM.yyyy", DeCulture),
                TotalDuration = TimeSpan.FromSeconds(Math.Round(dayTotalSeconds)),
                IsBeforeFirstRecordedDay = firstRecordedDate.HasValue && day < firstRecordedDate.Value
            });
        }

        var (categoriesSummary, appsSummary, periodTotalDuration) = BuildCategoriesAndAppsSummary(
            periodAppSeconds, periodAppSessions, apps, categories, settings);

        int evaluatedPeriodDays = CalculateEvaluatedDaysDenominator(startDate, endDate, firstRecordedDate);
        TimeSpan periodDailyAverage = evaluatedPeriodDays > 0
            ? TimeSpan.FromSeconds(Math.Round(periodTotalDuration.TotalSeconds / evaluatedPeriodDays))
            : TimeSpan.Zero;

        return new PeriodStatistics
        {
            StartDate = startDate,
            EndDate = endDate,
            PeriodTitle = title,
            TotalDuration = periodTotalDuration,
            AverageDailyDuration = periodDailyAverage,
            SixWeekAverageDailyDuration = sixWeekAvg,
            EvaluatedDaysForAverage = evaluatedPeriodDays,
            SixWeekEvaluatedDays = sixWeekDaysCount,
            FirstRecordedDate = firstRecordedDate,
            SixWeekExplanation = sixWeekExplanation,
            DailyPoints = dailyPoints,
            Categories = categoriesSummary,
            Apps = appsSummary
        };
    }

    private (IReadOnlyList<CategoryUsageSummary> Categories, IReadOnlyList<AppUsageSummary> Apps, TimeSpan TotalDuration)
        BuildCategoriesAndAppsSummary(
            Dictionary<long, double> appSeconds,
            Dictionary<long, int> appSessions,
            Dictionary<long, TrackedApplication> appsById,
            IReadOnlyList<AppCategory> categories,
            AppSettings settings)
    {
        var categoryById = categories.ToDictionary(c => c.Id);
        var fallbackCategory = categories.LastOrDefault() ?? new AppCategory
        {
            Id = 1,
            Key = "other",
            Name = "Sonstiges",
            ColorHex = "#8E8E93",
            IconGlyph = "\uE71D",
            DefaultIdleThresholdSeconds = 60
        };

        // Runde zuerst jede App auf ganze Sekunden, damit Summe der Apps == Summe der Kategorien == Gesamtzeit exakt übereinstimmt
        var roundedAppSeconds = new Dictionary<long, long>();
        long totalRoundedSeconds = 0;

        foreach (var kvp in appSeconds)
        {
            long rounded = (long)Math.Round(kvp.Value);
            if (rounded > 0 && appsById.ContainsKey(kvp.Key))
            {
                roundedAppSeconds[kvp.Key] = rounded;
                totalRoundedSeconds += rounded;
            }
        }

        var allAppSummaries = new List<AppUsageSummary>();
        foreach (var kvp in roundedAppSeconds.OrderByDescending(k => k.Value))
        {
            var app = appsById[kvp.Key];
            var cat = categoryById.GetValueOrDefault(app.CategoryId, fallbackCategory);
            double pct = totalRoundedSeconds > 0 ? (kvp.Value * 100.0) / totalRoundedSeconds : 0.0;

            allAppSummaries.Add(new AppUsageSummary
            {
                ApplicationId = app.Id,
                AppIdentifier = app.AppIdentifier,
                DisplayName = app.DisplayName,
                ProcessName = app.ProcessName,
                ExecutablePath = app.ExecutablePath,
                IconCachePath = app.IconCachePath,
                CategoryId = cat.Id,
                CategoryName = cat.Name,
                CategoryColorHex = cat.ColorHex,
                CategoryIconGlyph = cat.IconGlyph,
                EffectiveIdleThresholdSeconds = GetEffectiveIdleThresholdSeconds(app, cat, settings),
                CustomIdleThresholdSeconds = app.CustomIdleThresholdSeconds,
                TotalDuration = TimeSpan.FromSeconds(kvp.Value),
                PercentageOfTotal = pct,
                SessionCount = appSessions.GetValueOrDefault(app.Id, 1)
            });
        }

        var categorySummaries = new List<CategoryUsageSummary>();
        foreach (var cat in categories.OrderBy(c => c.SortOrder))
        {
            var catApps = allAppSummaries.Where(a => a.CategoryId == cat.Id).ToList();
            long catTotalSecs = catApps.Sum(a => (long)a.TotalDuration.TotalSeconds);
            double catPct = totalRoundedSeconds > 0 ? (catTotalSecs * 100.0) / totalRoundedSeconds : 0.0;

            // Ergänze manuell eingetragene Programme/Webseiten dieser Kategorie (mit 0 Sek. im Zeitraum), damit sie in der Kategorie sichtbar sind
            var existingCatAppIds = catApps.Select(a => a.ApplicationId).ToHashSet();
            foreach (var customApp in appsById.Values.Where(a => a.CategoryId == cat.Id && a.IsUserCustomized && !existingCatAppIds.Contains(a.Id)))
            {
                catApps.Add(new AppUsageSummary
                {
                    ApplicationId = customApp.Id,
                    AppIdentifier = customApp.AppIdentifier,
                    DisplayName = customApp.DisplayName,
                    ProcessName = customApp.ProcessName,
                    ExecutablePath = customApp.ExecutablePath,
                    IconCachePath = customApp.IconCachePath,
                    CategoryId = cat.Id,
                    CategoryName = cat.Name,
                    CategoryColorHex = cat.ColorHex,
                    CategoryIconGlyph = cat.IconGlyph,
                    EffectiveIdleThresholdSeconds = GetEffectiveIdleThresholdSeconds(customApp, cat, settings),
                    CustomIdleThresholdSeconds = customApp.CustomIdleThresholdSeconds,
                    TotalDuration = TimeSpan.Zero,
                    PercentageOfTotal = 0,
                    SessionCount = 0
                });
            }

            categorySummaries.Add(new CategoryUsageSummary
            {
                CategoryId = cat.Id,
                CategoryKey = cat.Key,
                CategoryName = cat.Name,
                ColorHex = cat.ColorHex,
                IconGlyph = cat.IconGlyph,
                DefaultIdleThresholdSeconds = cat.DefaultIdleThresholdSeconds,
                TotalDuration = TimeSpan.FromSeconds(catTotalSecs),
                PercentageOfTotal = catPct,
                Apps = catApps
            });
        }

        var sortedCategories = categorySummaries
            .OrderByDescending(c => c.TotalDuration)
            .ThenBy(c => c.CategoryName)
            .ToList();

        return (sortedCategories, allAppSummaries, TimeSpan.FromSeconds(totalRoundedSeconds));
    }

    private DateOnly? GetFirstRecordedLocalDate()
    {
        var firstUtc = _repository.GetFirstRecordedTimestampUtc();
        if (!firstUtc.HasValue)
        {
            return null;
        }
        return DateOnly.FromDateTime(firstUtc.Value.ToLocalTime().DateTime);
    }

    private static int CalculateEvaluatedDaysDenominator(DateOnly startDate, DateOnly endDate, DateOnly? firstRecordedDate)
    {
        if (!firstRecordedDate.HasValue || firstRecordedDate.Value > endDate)
        {
            return 0;
        }
        DateOnly effectiveStart = firstRecordedDate.Value > startDate ? firstRecordedDate.Value : startDate;
        return Math.Max(1, (endDate.DayNumber - effectiveStart.DayNumber) + 1);
    }

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetLocalDayBoundsUtc(DateOnly date)
    {
        var localStartDt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var localEndDt = localStartDt.AddDays(1);
        var startOffset = new DateTimeOffset(localStartDt);
        var endOffset = new DateTimeOffset(localEndDt);
        return (startOffset.ToUniversalTime(), endOffset.ToUniversalTime());
    }

    private static void DistributeIntoHourlyBuckets(
        DateTimeOffset localStart,
        DateTimeOffset localEnd,
        DateOnly targetDate,
        double[] hourlySeconds)
    {
        DateTime dayBase = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        for (int h = 0; h < 24; h++)
        {
            var bucketStart = new DateTimeOffset(dayBase.AddHours(h));
            var bucketEnd = new DateTimeOffset(dayBase.AddHours(h + 1));

            var cs = localStart > bucketStart ? localStart : bucketStart;
            var ce = localEnd < bucketEnd ? localEnd : bucketEnd;
            if (ce > cs)
            {
                hourlySeconds[h] += (ce - cs).TotalSeconds;
            }
        }
    }
}
