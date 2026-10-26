# ScreenTime – Lokale Bildschirmzeit & Nutzungsanalyse für Windows

Eine an Apples **Bildschirmzeit** orientierte, lokal arbeitende Windows-Desktop-App auf Basis von **C# / .NET 8 / WinUI 3 / SQLite**.

## Hauptfunktionen

1. **Verständliche Programmnamen & App-Symbole**
   - Zeigt saubere Anzeigenamen (z. B. `Google Chrome`, `Adobe Acrobat Reader`, `Visual Studio Code`) statt roher Dateipfade.
   - Extrahiert Programmsymbole automatisch aus den Windows-Metadaten und speichert sie im lokalen Icon-Cache (`%LocalAppData%\ScreenTime\icons`).
   - Gruppiert zusammengehörige Prozesse derselben Anwendung über stabile App-Identifikatoren.

2. **Intelligente Leerlauf-Erkennung (inkl. Lernen & PDF-Lesen)**
   - Überwacht Maus- und Tastaturaktivität über `GetLastInputInfo` sowie Sperrbildschirm und Standby über Windows-Systemereignisse.
   - **Kategorie- & App-abhängige Idle-Schwellwerte:**
     - Eigene Kategorie **„Lernen & Lesen“** (Standard-Schwellwert: **15 Minuten / 900 Sek.**) für PDF-Reader (Adobe Acrobat, SumatraPDF, Foxit, PDF24, Drawboard), Notiz-Apps (Obsidian, Notion, OneNote) und Lern-Tools (Anki, Zotero, Calibre).
     - Zeit bis zum Schwellwert zählt voll als aktive Nutzung; erst wenn über den Schwellwert hinaus keine Maus-/Tastatureingabe erfolgt, pausiert die Zählung bis zur nächsten Eingabe.
     - Alle Schwellwerte sind sowohl pro Kategorie als auch individuell pro App frei in den Einstellungen anpassbar.

3. **Zeiträume, 30-Tage-Monatsverlauf & 6-Wochen-Durchschnitt**
   - **Dashboard (Übersicht):** Heutige aktive Nutzungszeit, Vergleich zum 6-Wochen-Durchschnitt, stündlicher Tagesverlauf (00:00–23:00 Uhr), 7-Tage-Verlauf, Top-Apps und Kategorien.
   - **Verlauf:** Wechsel zwischen *Letzte 7 Tage (Woche)*, *Letzte 30 Tage (Monatsverlauf)* und *6-Wochen-Durchschnitt (42 Tage)* sowie interaktive Auswahl jedes einzelnen Tages zur Detailanalyse.
   - **6-Wochen-Durchschnitt:** Berücksichtigt alle Tage ab dem ersten Erfassungstag (Tage vor der ersten Nutzung der App werden ignoriert).

4. **Light Mode, Dark Mode & Systemstandard**
   - Nahtloser Wechsel zwischen *Systemstandard folgen*, *Hell (Light Mode)* und *Dunkel (Dark Mode)* über die Kopfzeile oder die Einstellungen.

## Projektstruktur

- `src/ScreenTime.Core`: Plattformlogik, Win32-Interoperabilität, SQLite-Persistenz (`winsqlite3.dll`), App-/Icon-Auflösung, Idle-Erkennung und Statistikberechnung.
- `src/ScreenTime.App`: WinUI-3-Benutzeroberfläche (MVVM), System-Tray-Integration und Diagramme.
- `tests/ScreenTime.Tests`: Automatisierte Tests für App-Auflösung, PDF-/Lern-Idle-Schwellwerte, 6-Wochen-Durchschnitt und SQLite-Wiederanlauf.

## Starten & Testen

```powershell
# App kompilieren und starten
dotnet run --project src\ScreenTime.App\ScreenTime.App.csproj

# Tests ausführen
dotnet run --project tests\ScreenTime.Tests\ScreenTime.Tests.csproj
```
