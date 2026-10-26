using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Models;

namespace ScreenTime.Core.Services;

/// <summary>
/// Ermittelt verständliche Programmnamen, gruppiert zusammengehörige Prozesse,
/// ordnet initiale Kategorien (inkl. „Lernen &amp; Lesen“) zu und extrahiert App-Symbole.
/// </summary>
public sealed class ApplicationResolver : IApplicationResolver
{
    private readonly string _iconCacheDirectory;
    private readonly IAppLogger _logger;

    private sealed record KnownAppRule(string CanonicalId, string FriendlyName, string CategoryKey);

    private static readonly Dictionary<string, KnownAppRule> KnownProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Lernen & Lesen (PDF-Reader, Notizen, Karteikarten, Literatur)
        ["acrord32"] = new("adobe-acrobat", "Adobe Acrobat Reader", "learning"),
        ["acrobat"] = new("adobe-acrobat", "Adobe Acrobat", "learning"),
        ["sumatrapdf"] = new("sumatrapdf", "SumatraPDF", "learning"),
        ["foxitpdfreader"] = new("foxit-pdf", "Foxit PDF Reader", "learning"),
        ["foxitreader"] = new("foxit-pdf", "Foxit PDF Reader", "learning"),
        ["foxitpdfeditor"] = new("foxit-pdf", "Foxit PDF Editor", "learning"),
        ["pdf24"] = new("pdf24", "PDF24 Reader", "learning"),
        ["pdf24-reader"] = new("pdf24", "PDF24 Reader", "learning"),
        ["drawboardpdf"] = new("drawboard-pdf", "Drawboard PDF", "learning"),
        ["xodo"] = new("xodo-pdf", "Xodo PDF Reader", "learning"),
        ["okular"] = new("okular", "Okular", "learning"),
        ["anki"] = new("anki", "Anki", "learning"),
        ["obsidian"] = new("obsidian", "Obsidian", "learning"),
        ["notion"] = new("notion", "Notion", "learning"),
        ["onenote"] = new("microsoft-onenote", "Microsoft OneNote", "learning"),
        ["onenoteim"] = new("microsoft-onenote", "Microsoft OneNote", "learning"),
        ["zotero"] = new("zotero", "Zotero", "learning"),
        ["calibre"] = new("calibre", "Calibre E-Book", "learning"),
        ["ebook-viewer"] = new("calibre", "Calibre E-Book Viewer", "learning"),
        ["logseq"] = new("logseq", "Logseq", "learning"),
        ["joplin"] = new("joplin", "Joplin", "learning"),
        ["remnote"] = new("remnote", "RemNote", "learning"),
        ["goodnotes"] = new("goodnotes", "Goodnotes", "learning"),

        // Produktivität (Browser, Office, Entwicklung)
        ["chrome"] = new("google-chrome", "Google Chrome", "productivity"),
        ["msedge"] = new("microsoft-edge", "Microsoft Edge", "productivity"),
        ["firefox"] = new("mozilla-firefox", "Mozilla Firefox", "productivity"),
        ["brave"] = new("brave-browser", "Brave Browser", "productivity"),
        ["opera"] = new("opera-browser", "Opera", "productivity"),
        ["vivaldi"] = new("vivaldi-browser", "Vivaldi", "productivity"),
        ["arc"] = new("arc-browser", "Arc", "productivity"),
        ["code"] = new("vscode", "Visual Studio Code", "productivity"),
        ["devenv"] = new("visual-studio", "Visual Studio", "productivity"),
        ["rider64"] = new("jetbrains-rider", "JetBrains Rider", "productivity"),
        ["idea64"] = new("jetbrains-idea", "IntelliJ IDEA", "productivity"),
        ["pycharm64"] = new("jetbrains-pycharm", "PyCharm", "productivity"),
        ["webstorm64"] = new("jetbrains-webstorm", "WebStorm", "productivity"),
        ["clion64"] = new("jetbrains-clion", "CLion", "productivity"),
        ["winword"] = new("microsoft-word", "Microsoft Word", "productivity"),
        ["excel"] = new("microsoft-excel", "Microsoft Excel", "productivity"),
        ["powerpnt"] = new("microsoft-powerpoint", "Microsoft PowerPoint", "productivity"),
        ["soffice"] = new("libreoffice", "LibreOffice", "productivity"),
        ["soffice.bin"] = new("libreoffice", "LibreOffice", "productivity"),
        ["notepad"] = new("windows-notepad", "Editor (Notepad)", "productivity"),
        ["notepad++"] = new("notepad-plusplus", "Notepad++", "productivity"),
        ["sublime_text"] = new("sublime-text", "Sublime Text", "productivity"),
        ["windowsterminal"] = new("windows-terminal", "Windows Terminal", "productivity"),
        ["wt"] = new("windows-terminal", "Windows Terminal", "productivity"),
        ["powershell"] = new("powershell", "Windows PowerShell", "productivity"),
        ["pwsh"] = new("powershell", "PowerShell", "productivity"),
        ["cmd"] = new("cmd", "Eingabeaufforderung", "productivity"),

        // Kommunikation
        ["teams"] = new("microsoft-teams", "Microsoft Teams", "communication"),
        ["ms-teams"] = new("microsoft-teams", "Microsoft Teams", "communication"),
        ["olk"] = new("microsoft-outlook", "Microsoft Outlook", "communication"),
        ["outlook"] = new("microsoft-outlook", "Microsoft Outlook", "communication"),
        ["thunderbird"] = new("mozilla-thunderbird", "Mozilla Thunderbird", "communication"),
        ["discord"] = new("discord", "Discord", "communication"),
        ["slack"] = new("slack", "Slack", "communication"),
        ["zoom"] = new("zoom", "Zoom", "communication"),
        ["telegram"] = new("telegram", "Telegram", "communication"),
        ["whatsapp"] = new("whatsapp", "WhatsApp", "communication"),
        ["signal"] = new("signal", "Signal", "communication"),

        // Kreativität
        ["photoshop"] = new("adobe-photoshop", "Adobe Photoshop", "creativity"),
        ["illustrator"] = new("adobe-illustrator", "Adobe Illustrator", "creativity"),
        ["indesign"] = new("adobe-indesign", "Adobe InDesign", "creativity"),
        ["premiere pro"] = new("adobe-premiere", "Adobe Premiere Pro", "creativity"),
        ["afterfx"] = new("adobe-aftereffects", "Adobe After Effects", "creativity"),
        ["figma"] = new("figma", "Figma", "creativity"),
        ["blender"] = new("blender", "Blender", "creativity"),
        ["gimp-2.10"] = new("gimp", "GIMP", "creativity"),
        ["mspaint"] = new("windows-paint", "Paint", "creativity"),
        ["obs64"] = new("obs-studio", "OBS Studio", "creativity"),
        ["audacity"] = new("audacity", "Audacity", "creativity"),
        ["resolve"] = new("davinci-resolve", "DaVinci Resolve", "creativity"),

        // Unterhaltung
        ["spotify"] = new("spotify", "Spotify", "entertainment"),
        ["vlc"] = new("vlc", "VLC Media Player", "entertainment"),
        ["mpv"] = new("mpv", "mpv Media Player", "entertainment"),
        ["wmplayer"] = new("windows-media-player", "Windows Media Player", "entertainment"),
        ["video.ui"] = new("windows-media-player", "Medienwiedergabe", "entertainment"),
        ["music.ui"] = new("windows-media-player", "Medienwiedergabe", "entertainment"),
        ["netflix"] = new("netflix", "Netflix", "entertainment"),
        ["itunes"] = new("apple-itunes", "iTunes", "entertainment"),
        ["applemusic"] = new("apple-music", "Apple Music", "entertainment"),

        // Spiele
        ["steam"] = new("steam", "Steam", "games"),
        ["steamwebhelper"] = new("steam", "Steam", "games"),
        ["epicgameslauncher"] = new("epic-games", "Epic Games Launcher", "games"),
        ["galaxyclient"] = new("gog-galaxy", "GOG Galaxy", "games"),
        ["battle.net"] = new("battlenet", "Battle.net", "games"),
        ["javaw"] = new("minecraft-java", "Minecraft (Java)", "games"),
        ["minecraft.windows"] = new("minecraft", "Minecraft", "games"),

        // Dienstprogramme
        ["explorer"] = new("windows-explorer", "Datei-Explorer", "utilities"),
        ["taskmgr"] = new("task-manager", "Task-Manager", "utilities"),
        ["systemsettings"] = new("windows-settings", "Windows-Einstellungen", "utilities"),
        ["control"] = new("control-panel", "Systemsteuerung", "utilities"),
        ["calc"] = new("windows-calculator", "Rechner", "utilities"),
        ["calculatorapp"] = new("windows-calculator", "Rechner", "utilities"),
        ["7zfm"] = new("7zip", "7-Zip", "utilities"),
        ["winrar"] = new("winrar", "WinRAR", "utilities"),
        ["powertoys"] = new("powertoys", "Microsoft PowerToys", "utilities"),
        ["powertoys.settings"] = new("powertoys", "Microsoft PowerToys", "utilities"),
        ["screentime.app"] = new("screentime", "ScreenTime", "utilities")
    };

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "waterfox", "thorium", "zen"
    };

    public ApplicationResolver(string iconCacheDirectory, IAppLogger logger)
    {
        _iconCacheDirectory = iconCacheDirectory ?? throw new ArgumentNullException(nameof(iconCacheDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Directory.CreateDirectory(_iconCacheDirectory);
    }

    public static bool IsBrowserProcess(string processName)
    {
        string clean = CleanProcessName(processName);
        return BrowserProcesses.Contains(clean);
    }

    public static string GetCanonicalAppIdentifier(string processName, string? packageFullName = null)
    {
        string clean = CleanProcessName(processName);
        if (KnownProcesses.TryGetValue(clean, out var rule))
        {
            return rule.CanonicalId;
        }
        return NormalizeIdentifier(clean, packageFullName);
    }

    public static string NormalizeWebsiteDomain(string urlOrDomain)
    {
        if (string.IsNullOrWhiteSpace(urlOrDomain))
        {
            return "website";
        }

        string input = urlOrDomain.Trim();
        if (!input.Contains("://", StringComparison.Ordinal))
        {
            input = "https://" + input;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            string host = uri.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal))
            {
                host = host[4..];
            }
            return host;
        }

        string fallback = urlOrDomain.Trim().ToLowerInvariant();
        if (fallback.StartsWith("www.", StringComparison.Ordinal))
        {
            fallback = fallback[4..];
        }
        int slashIdx = fallback.IndexOf('/');
        if (slashIdx > 0)
        {
            fallback = fallback[..slashIdx];
        }
        return fallback;
    }

    public static string FormatWebsiteDisplayName(string normalizedDomain)
    {
        if (string.IsNullOrWhiteSpace(normalizedDomain))
        {
            return "Webseite";
        }
        int dotIdx = normalizedDomain.IndexOf('.');
        string mainPart = dotIdx > 0 ? normalizedDomain[..dotIdx] : normalizedDomain;
        return mainPart.Length > 0
            ? char.ToUpperInvariant(mainPart[0]) + mainPart[1..]
            : normalizedDomain;
    }

    public TrackedApplication Resolve(
        ForegroundProcessInfo processInfo,
        IReadOnlyList<AppCategory> categories,
        IReadOnlyList<TrackedApplication>? existingApps = null)
    {
        string rawProcessName = CleanProcessName(processInfo.ProcessName);

        // Falls ein Browser im Vordergrund ist und der Benutzer eigene Webseiten (Links) angelegt hat,
        // prüfen wir, ob der aktuelle Browser-Tab zu einer eingetragenen Webseite gehört.
        if (BrowserProcesses.Contains(rawProcessName) &&
            !string.IsNullOrWhiteSpace(processInfo.TransientWindowTitle) &&
            existingApps is not null)
        {
            var matchedWebsite = TryMatchWebsiteRule(processInfo.TransientWindowTitle, existingApps);
            if (matchedWebsite is not null)
            {
                matchedWebsite.LastSeenUtc = processInfo.TimestampUtc;
                return matchedWebsite;
            }
        }

        string appIdentifier;
        string displayName;
        string categoryKey;

        if (KnownProcesses.TryGetValue(rawProcessName, out var rule))
        {
            appIdentifier = rule.CanonicalId;
            displayName = rule.FriendlyName;
            categoryKey = rule.CategoryKey;
        }
        else
        {
            appIdentifier = NormalizeIdentifier(rawProcessName, processInfo.PackageFullName);
            displayName = ResolveFriendlyDisplayName(rawProcessName, processInfo.ExecutablePath);
            categoryKey = GuessCategoryKey(rawProcessName, displayName, processInfo.ExecutablePath);
        }

        long categoryId = FindCategoryId(categories, categoryKey);
        string? iconPath = EnsureIconCached(appIdentifier, processInfo.ExecutablePath);

        return new TrackedApplication
        {
            AppIdentifier = appIdentifier,
            DisplayName = displayName,
            ProcessName = rawProcessName,
            ExecutablePath = processInfo.ExecutablePath,
            IconCachePath = iconPath,
            CategoryId = categoryId,
            FirstSeenUtc = processInfo.TimestampUtc,
            LastSeenUtc = processInfo.TimestampUtc
        };
    }

    private static TrackedApplication? TryMatchWebsiteRule(string windowTitle, IEnumerable<TrackedApplication> existingApps)
    {
        foreach (var webApp in existingApps.Where(a => a.IsWebsite))
        {
            string domain = webApp.AppIdentifier["web:".Length..];
            int dotIndex = domain.IndexOf('.');
            string hostKeyword = dotIndex > 0 ? domain[..dotIndex] : domain;

            if ((!string.IsNullOrWhiteSpace(domain) && windowTitle.Contains(domain, StringComparison.OrdinalIgnoreCase)) ||
                (hostKeyword.Length >= 3 && windowTitle.Contains(hostKeyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(webApp.DisplayName) && webApp.DisplayName.Length >= 3 && windowTitle.Contains(webApp.DisplayName, StringComparison.OrdinalIgnoreCase)))
            {
                return webApp;
            }
        }

        return null;
    }

    public string? EnsureIconCached(string appIdentifier, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(appIdentifier))
        {
            return null;
        }

        string safeFileName = Regex.Replace(appIdentifier, @"[^a-zA-Z0-9_\-\.]", "_");
        string targetPngPath = Path.Combine(_iconCacheDirectory, $"{safeFileName}.png");

        if (File.Exists(targetPngPath))
        {
            return targetPngPath;
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            // Versuch 1: SHGetFileInfo für hochauflösendes System-Icon
            nint hIcon = ExtractShellIconHandle(executablePath);
            if (hIcon != nint.Zero)
            {
                try
                {
                    using var icon = Icon.FromHandle(hIcon);
                    using var bmp = icon.ToBitmap();
                    bmp.Save(targetPngPath, ImageFormat.Png);
                    return targetPngPath;
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }

            // Versuch 2: Icon.ExtractAssociatedIcon
            using var fallbackIcon = Icon.ExtractAssociatedIcon(executablePath);
            if (fallbackIcon is not null)
            {
                using var bmp = fallbackIcon.ToBitmap();
                bmp.Save(targetPngPath, ImageFormat.Png);
                return targetPngPath;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Icon konnte für '{appIdentifier}' nicht geladen werden: {ex.Message}");
        }

        return null;
    }

    private static string ResolveFriendlyDisplayName(string processName, string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                string? candidate = PickBestMetadataName(versionInfo.FileDescription, versionInfo.ProductName, processName);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignorieren, Fallback nutzen
            }
        }

        return FormatFallbackName(processName);
    }

    private static string? PickBestMetadataName(string? fileDescription, string? productName, string processName)
    {
        string? desc = CleanMetadataString(fileDescription);
        string? prod = CleanMetadataString(productName);

        // Wenn FileDescription sinnvoll ist und nicht generisch wie "Microsoft® Windows® Operating System"
        if (!string.IsNullOrWhiteSpace(desc) && !IsGenericOsTitle(desc))
        {
            return desc;
        }

        if (!string.IsNullOrWhiteSpace(prod) && !IsGenericOsTitle(prod))
        {
            return prod;
        }

        return null;
    }

    private static string? CleanMetadataString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        string cleaned = input.Trim();
        // Falls aus Versehen ein Pfad oder .exe in den Metadaten steht, bereinigen
        if (cleaned.Contains('\\') || cleaned.Contains('/'))
        {
            cleaned = Path.GetFileNameWithoutExtension(cleaned);
        }
        if (cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^4].Trim();
        }
        cleaned = Regex.Replace(cleaned, @"\s+\(\d+-bit\)$", "", RegexOptions.IgnoreCase).Trim();
        return cleaned.Length >= 2 ? cleaned : null;
    }

    private static bool IsGenericOsTitle(string title)
    {
        return title.Contains("Windows® Operating System", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Betriebssystem Microsoft", StringComparison.OrdinalIgnoreCase)
            || title.Equals("Application", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatFallbackName(string rawProcessName)
    {
        if (string.IsNullOrWhiteSpace(rawProcessName))
        {
            return "Unbekannte App";
        }

        string name = CleanProcessName(rawProcessName);
        name = name.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ');
        // CamelCase / PascalCase aufteilen: "MyCoolApp" -> "My Cool App"
        name = Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])", " ");
        name = Regex.Replace(name, @"\s+", " ").Trim();

        if (name.Length == 0)
        {
            return "Unbekannte App";
        }

        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static string CleanProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return "unknown";
        }

        string cleaned = processName.Trim();
        if (cleaned.Contains('\\') || cleaned.Contains('/'))
        {
            cleaned = Path.GetFileNameWithoutExtension(cleaned);
        }
        if (cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^4];
        }
        return cleaned.Trim();
    }

    private static string NormalizeIdentifier(string cleanProcessName, string? packageFullName)
    {
        if (!string.IsNullOrWhiteSpace(packageFullName))
        {
            int underscoreIdx = packageFullName.IndexOf('_');
            string pkgPrefix = underscoreIdx > 0 ? packageFullName[..underscoreIdx] : packageFullName;
            return pkgPrefix.ToLowerInvariant();
        }

        // Entferne typische Architektur-Suffixe wie "64", "_x64" für stabile Gruppierung
        string normalized = Regex.Replace(cleanProcessName.ToLowerInvariant(), @"(_x64|_x86|64|32)$", "");
        return string.IsNullOrWhiteSpace(normalized) ? cleanProcessName.ToLowerInvariant() : normalized;
    }

    private static string GuessCategoryKey(string processName, string displayName, string? exePath)
    {
        string combined = $"{processName} {displayName} {exePath}".ToLowerInvariant();

        if (combined.Contains("pdf") || combined.Contains("reader") || combined.Contains("ebook") ||
            combined.Contains("anki") || combined.Contains("study") || combined.Contains("learn") ||
            combined.Contains("notiz") || combined.Contains("note") || combined.Contains("wiki") ||
            combined.Contains("latex") || combined.Contains("texstudio") || combined.Contains("overleaf"))
        {
            return "learning";
        }

        if (combined.Contains("studio") || combined.Contains("code") || combined.Contains("office") ||
            combined.Contains("word") || combined.Contains("excel") || combined.Contains("browser") ||
            combined.Contains("git") || combined.Contains("terminal") || combined.Contains("docker"))
        {
            return "productivity";
        }

        if (combined.Contains("chat") || combined.Contains("mail") || combined.Contains("messenger") ||
            combined.Contains("meet") || combined.Contains("call"))
        {
            return "communication";
        }

        if (combined.Contains("photo") || combined.Contains("design") || combined.Contains("draw") ||
            combined.Contains("paint") || combined.Contains("audio") || combined.Contains("synth") ||
            combined.Contains("canvas") || combined.Contains("affinity"))
        {
            return "creativity";
        }

        if (combined.Contains("game") || combined.Contains("steamapps") || combined.Contains("xbox") ||
            combined.Contains("play") || combined.Contains("riot") || combined.Contains("ubisoft"))
        {
            return "games";
        }

        if (combined.Contains("music") || combined.Contains("video") || combined.Contains("player") ||
            combined.Contains("stream") || combined.Contains("tv"))
        {
            return "entertainment";
        }

        if (combined.Contains("windows\\system32") || combined.Contains("setup") ||
            combined.Contains("installer") || combined.Contains("manager") || combined.Contains("tool"))
        {
            return "utilities";
        }

        return "other";
    }

    private static long FindCategoryId(IReadOnlyList<AppCategory> categories, string key)
    {
        var match = categories.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                 ?? categories.FirstOrDefault(c => c.Key.Equals("other", StringComparison.OrdinalIgnoreCase))
                 ?? categories.FirstOrDefault();

        return match?.Id ?? 1;
    }

    private static nint ExtractShellIconHandle(string filePath)
    {
        var shinfo = new SHFILEINFO();
        const uint SHGFI_ICON = 0x100;
        const uint SHGFI_LARGEICON = 0x0;
        nint res = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
        return res != nint.Zero ? shinfo.hIcon : nint.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);
}
