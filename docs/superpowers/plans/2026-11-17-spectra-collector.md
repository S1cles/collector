# SpectraCollector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A small Windows app (plus installer) that collects LyncisAC spectrum files for given sample windows and packs them into one zip for A.spect import.

**Architecture:** All logic lives in a `net10.0` core library tested on Linux: parsers (sample times, file names, windows) feed `SpectraSource`, which reads hourly archives and loose `Tech_Spectra` files via `DataLayout`; `Collector` orchestrates search, report and `OutputArchive` with progress and cancellation; `FormValidator` turns window input into a request. A thin WPF shell renders it; NSIS packs the published exe into a per-user installer.

**Tech Stack:** C# / .NET 10, BCL only in core (`System.IO.Compression`, `System.Text.Json`), xUnit, WPF (`net10.0-windows`, `EnableWindowsTargeting`), NSIS 3 (`makensis` on Debian), podman containers for every build step.

**Spec:** `docs/superpowers/specs/2026-11-17-spectra-collector-design.md`

## Global Constraints

- Run .NET only through `build/dotnet.sh` (container `mcr.microsoft.com/dotnet/sdk:10.0`); never install an SDK on the host. lyncis-dev has ~4.6 GB free — no new large images.
- Never write, move or delete under the data root; open every input with `FileShare.ReadWrite | FileShare.Delete`.
- Window = `[floor(Begin) to minute, ceil(End) to minute]`, both inclusive (13:30:30–13:40:01 → 13:30:00–13:41:00).
- Spectrum name `Spectra_<n>_<anything>_yyyy.MM.dd_HH.mm.ss[.<ext>]`; hour archive `<data>\<yyyy_MM>\<belt>\Packed_Spectra\spect_data_yyyy-MM-dd_HH.zip`; loose files `<data>\<yyyy_MM>\<belt>\Tech_Spectra\`.
- Zip `spectra_yyyy-MM-dd_HHmm[_N].zip` (earliest Begin) with `<base>/Tech_Spectra/<spectra>`, `<base>/samples.csv` (or the input CSV's own name), `<base>/collect-report.txt`.
- Time formats: `yyyy-MM-dd HH:mm[:ss]`, `dd.MM.yyyy HH:mm[:ss]`, `dd/MM/yyyy HH:mm[:ss]`; invariant culture everywhere.
- Code, UI text, comments and docs in English. `Nullable` enabled, `TreatWarningsAsErrors` on.
- Visual tokens and copy exactly as in the spec's "Look" section.

## Review Focus

- CSV saved by Excel: UTF-8 BOM, `;`, CRLF, trailing empty lines → parses (test in Task 2).
- LyncisAC holds the newest file open for writing → it is still collected (test in Task 3).
- Overlapping samples, or one file both in an hour archive and in `Tech_Spectra` → one copy (test in Task 3).
- Cancel pressed mid-write → no `.zip` and no `.partial` left (test in Task 5).
- Begin typed after End, or an unparseable time → Collect disabled with a plain message, no exception (test in Task 6).

---

### Task 1: Solution, build wrapper, time windows, spectrum names

**Files:**
- Create: `build/dotnet.sh`, `SpectraCollector.slnx`, `Directory.Build.props`, `src/SpectraCollector.Core/SpectraCollector.Core.csproj`, `src/SpectraCollector.Core/TimeWindow.cs`, `src/SpectraCollector.Core/SpectrumName.cs`, `src/SpectraCollector.Core/CollectorException.cs`
- Test: `tests/SpectraCollector.Core.Tests/SpectraCollector.Core.Tests.csproj`, `TimeWindowTests.cs`, `SpectrumNameTests.cs`

**Interfaces:**
- Produces: `build/dotnet.sh <args>` = `podman run --rm -v "$repo":/src:Z -v "$repo/.cache/nuget":/root/.nuget/packages:Z -w /src -e DOTNET_NOLOGO=1 mcr.microsoft.com/dotnet/sdk:10.0 dotnet <args>`.
- Produces: `readonly record struct TimeWindow(DateTime From, DateTime To)`; `static TimeWindow Rounded(DateTime begin, DateTime end)`; `bool Contains(DateTime t)`; `IEnumerable<DateTime> Hours()`.
- Produces: `sealed record SpectrumName(string FileName, int Spectrometer, DateTime Timestamp)`; `static bool TryParse(string fileName, [NotNullWhen(true)] out SpectrumName? name)`.
- Produces: `abstract class CollectorException : Exception` (message ctor); `sealed class SampleTimesException(string message)`; `sealed class NothingFoundException(string message)`.

- [ ] **Step 1: Tests** — `Rounded(13:30:30, 13:40:01)` → `13:30:00..13:41:00`; `Rounded(13:30:00, 13:40:00)` unchanged; `Contains` true at both ends, false at 13:41:01; `Hours()` for 12:59:30–14:00:10 → 12:00, 13:00, 14:00. `TryParse("Spectra_0_Belt_1_2026.11.06_13.11.10.csv")` → 0, 2026-11-06 13:11:10; same without extension; `Spectra_12_Belt_3_...` → 12; false for `spect_data_2026-11-06_13.zip`, `Spectra_x_Belt_1_2026.11.06_13.11.10.csv`, month 13.
- [ ] **Step 2:** `build/dotnet.sh test` → FAIL (types missing).
- [ ] **Step 3:** Implement. `Directory.Build.props`: `Nullable`, `TreatWarningsAsErrors`, `LangVersion latest`, `ImplicitUsings`. xUnit packages pinned.
- [ ] **Step 4:** `build/dotnet.sh test` → PASS.
- [ ] **Step 5:** Commit `feat(core): add time windows and spectrum name parsing`.

### Task 2: Sample times (CSV and single interval)

**Files:**
- Create: `src/SpectraCollector.Core/Sample.cs`, `SampleTime.cs`, `SampleTable.cs`
- Test: `tests/SpectraCollector.Core.Tests/SampleTableTests.cs`, `SampleTimeTests.cs`

**Interfaces:**
- Consumes: `TimeWindow.Rounded`, `SampleTimesException`.
- Produces: `sealed record Sample(string Name, DateTime Begin, DateTime End)` with `TimeWindow Window`.
- Produces: `static bool SampleTime.TryParse(string text, out DateTime value)` (trimmed input, the three formats, with and without seconds).
- Produces: `static IReadOnlyList<Sample> SampleTable.Parse(TextReader reader)` (errors as `SampleTimesException` starting `Line {n}: `); `static string SampleTable.ToCsv(IReadOnlyList<Sample> samples)` (header `Sample;Exposition;Begin;End;Material`, times `yyyy-MM-dd HH:mm:ss`, CRLF).

- [ ] **Step 1: Tests** — header names any case/order; `;` `,` tab; quoted field containing the delimiter; all formats; BOM + CRLF + blank trailing lines; `Line n:` errors for missing `Begin` column, bad time, End before Begin, empty name, no data rows; `ToCsv` round-trips through `Parse`.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement. **Step 4:** run → PASS.
- [ ] **Step 5:** Commit `feat(core): parse and write sample time tables`.

### Task 3: Finding spectra on the analyzer

**Files:**
- Create: `src/SpectraCollector.Core/DataLayout.cs`, `FoundSpectrum.cs`, `SpectraSource.cs`
- Test: `tests/SpectraCollector.Core.Tests/SpectraSourceTests.cs`, `FakeAnalyzer.cs` (builds a data tree in a per-test temp dir, disposed after)

**Interfaces:**
- Consumes: `Sample`, `TimeWindow`, `SpectrumName`.
- Produces: `sealed record DataLayout(string DataRoot, string Belt)`; `string TechSpectraDir(DateTime hour)`; `string HourArchive(DateTime hour)`.
- Produces: `sealed record FoundSpectrum(SpectrumName Name, string SourcePath, string? EntryName)`; `Stream Open()` — loose file when `EntryName` is null, else that entry of the zip; read-only, full sharing; the returned stream owns and disposes the archive.
- Produces: `sealed record SourceScan(IReadOnlyList<FoundSpectrum> Files, IReadOnlyList<DateTime> EmptyHours)`; `static SourceScan SpectraSource.Find(DataLayout layout, IReadOnlyList<Sample> samples, CancellationToken ct)` — sorted by timestamp then spectrometer, unique by file name; `EmptyHours` = hours with neither archive nor loose files.

- [ ] **Step 1: Tests** — loose only; archive only; hour 13 archived + hour 14 loose in one window; window 2026-10-31 23:58 → 2026-11-01 00:02 across two month folders; edges 13:30:00 and 13:41:00 in, 13:29:50 and 13:41:10 out; two spectrometers; unrelated names ignored; overlapping samples and archive+loose duplicate → one copy; archive entry under a subfolder matched by file name; loose file held open by a writer with `FileShare.ReadWrite` still collected; missing month folder → hour in `EmptyHours`; cancelled token → `OperationCanceledException`.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement — per hour, archive entries filtered by window, then `Directory.EnumerateFiles(dir, "Spectra_*_" + hour.ToString("yyyy.MM.dd_HH") + "*")` parsed with `SpectrumName.TryParse`. **Step 4:** run → PASS.
- [ ] **Step 5:** Commit `feat(core): find spectra in hour archives and Tech_Spectra`.

### Task 4: Collection report

**Files:**
- Create: `src/SpectraCollector.Core/CollectionReport.cs`
- Test: `tests/SpectraCollector.Core.Tests/CollectionReportTests.cs`

**Interfaces:**
- Consumes: `Sample`, `SourceScan`.
- Produces: `enum WarningKind { NoFiles, UnevenSpectrometers, Gap, EmptyHour }`; `sealed record ReportWarning(WarningKind Kind, string? Sample, string Message)`; `sealed record SampleSummary(Sample Sample, IReadOnlyDictionary<int, int> CountBySpectrometer)`; `sealed class CollectionReport` with `static Build(IReadOnlyList<Sample>, SourceScan)`, `Samples`, `Warnings`, `int TotalFiles`, `string ToText()`.

- [ ] **Step 1: Tests** — counts per sample and spectrometer; `NoFiles`; `UnevenSpectrometers` (43 vs 41); `Gap` for a 40 s hole in 10 s cadence (> 3× median) but not for a 20 s one; `EmptyHour` per hour; `ToText()` contains each sample's rounded window `HH:mm:ss–HH:mm:ss` and every warning message.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement. **Step 4:** run → PASS.
- [ ] **Step 5:** Commit `feat(core): report per-sample coverage and gaps`.

### Task 5: Output archive and the collector

**Files:**
- Create: `src/SpectraCollector.Core/OutputArchive.cs`, `Collector.cs`, `AppSettings.cs`
- Test: `tests/SpectraCollector.Core.Tests/OutputArchiveTests.cs`, `CollectorTests.cs`, `AppSettingsTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces: `static string OutputArchive.Write(string outputDir, string baseName, IReadOnlyList<FoundSpectrum> files, string csvName, string csvText, string reportText, IProgress<int>? filesWritten, CancellationToken ct)` → zip path.
- Produces: `sealed record CollectRequest(IReadOnlyList<Sample> Samples, string CsvName, string CsvText, DataLayout Layout, string OutputDir)`; `sealed record CollectProgress(int Done, int Total)`; `sealed record CollectResult(string ZipPath, CollectionReport Report)`; `static Task<CollectResult> Collector.RunAsync(CollectRequest request, IProgress<CollectProgress>? progress, CancellationToken ct)` (runs on the thread pool; throws `NothingFoundException` when no files, `OperationCanceledException` on cancel).
- Produces: `sealed record AppSettings(string? CsvPath, string? OutputDir, string DataRoot, string Belt, bool SingleInterval)` with defaults `D:\LyncisAC_DATA`, `Main\Belt_1`; `static AppSettings Load(string path)` (missing/corrupt file → defaults); `void Save(string path)`; `static string DefaultPath` = `%LOCALAPPDATA%\SpectraCollector\settings.json`.

- [ ] **Step 1: Tests** — zip has exactly the three kinds of entries with byte-identical spectra; missing output dir created; taken name → `_2`; success leaves no `.partial`; failing `Open()` or cancel mid-write → neither `.zip` nor `.partial`, exception propagates; `RunAsync` end-to-end on `FakeAnalyzer` reports progress up to `Total`, nothing found → `NothingFoundException` and no zip; settings round-trip, corrupt JSON → defaults.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement — write `<name>.zip.partial`, `CompressionLevel.Optimal`, `File.Move` on success, delete in `catch` and rethrow. **Step 4:** run → PASS.
- [ ] **Step 5:** Commit `feat(core): write the zip and orchestrate a collection`.

### Task 6: Form logic

**Files:**
- Create: `src/SpectraCollector.Core/FormValidator.cs`
- Test: `tests/SpectraCollector.Core.Tests/FormValidatorTests.cs`

**Interfaces:**
- Consumes: `SampleTime`, `SampleTable`, `Sample`, `DataLayout`, `CollectRequest`.
- Produces: `sealed record FormInput(bool SingleInterval, string? CsvPath, string BeginText, string EndText, string OutputDir, string DataRoot, string Belt)`; `sealed record FormCheck(CollectRequest? Request, IReadOnlyList<string> Problems, string? WindowHint)`; `static FormCheck FormValidator.Check(FormInput input)`.

- [ ] **Step 1: Tests** — single interval valid → one `Sample_1`, `CsvName` `samples.csv`, `CsvText` from `SampleTable.ToCsv`, hint `Files from 13:30:00 to 13:41:00` (date added when Begin and End are on different days); End before Begin → problem `End is earlier than Begin.`, no request; bad time → `Begin is not a time. Use 2026-11-06 13:30:00.`; CSV mode with missing file → `CSV file not found.`; CSV parse error → its `Line n:` message; empty output dir → `Choose where to save the zip.`; CSV mode valid → request with the CSV's own name and text.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement. **Step 4:** run → PASS.
- [ ] **Step 5:** Commit `feat(core): validate window input into a collect request`.

### Task 7: WPF window

**Files:**
- Create: `src/SpectraCollector.App/SpectraCollector.App.csproj`, `App.xaml(.cs)`, `Theme.xaml`, `MainWindow.xaml(.cs)`, `MainViewModel.cs`, `RelayCommand.cs`, `Assets/SpectraCollector.ico`, `build/make-icon.py`
- Modify: `SpectraCollector.slnx`

**Interfaces:**
- Consumes: `FormValidator`, `Collector.RunAsync`, `AppSettings`, `CollectionReport`.
- Produces: published `SpectraCollector.exe` via `build/dotnet.sh publish src/SpectraCollector.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish/app`.

- [ ] **Step 1:** `Theme.xaml` — the spec's colour tokens as `SolidColorBrush` resources, the band as a `LinearGradientBrush` with the six stops, Bahnschrift/Segoe UI `FontFamily` resources, styles for `TextBox`, `Button` (primary Ink / secondary outline), segmented `RadioButton` toggle, focus visual (2 px Ink).
- [ ] **Step 2:** `MainWindow.xaml` per the spec's window sketch; `MainViewModel` re-runs `FormValidator.Check` on every input change (Collect enabled only with a request; problems listed in warning colour), Browse via `Microsoft.Win32.OpenFileDialog` / `OpenFolderDialog`, Collect runs `Collector.RunAsync` with a `CancellationTokenSource`, band fills from `CollectProgress`, Cancel cancels, `Closing` during a run cancels and awaits the task before closing; settings loaded on start and saved after each successful run; "Open folder" starts `explorer.exe /select,"<zip>"`.
- [ ] **Step 3:** `build/make-icon.py` (stdlib only) writes a 16/32/48/256 px ICO: Ink rounded square with six vertical bars in the band colours; app and window use it.
- [ ] **Step 4:** publish command above → one `SpectraCollector.exe`, build has zero warnings; `build/dotnet.sh test` still PASS.
- [ ] **Step 5:** Commit `feat(app): add the collector window`.

### Task 8: Installer, build script, README

**Files:**
- Create: `installer/SpectraCollector.nsi`, `build/build-all.sh`, `README.md`, `samples.example.csv`

**Interfaces:**
- Consumes: `publish/app/SpectraCollector.exe`, `src/SpectraCollector.App/Assets/SpectraCollector.ico`.
- Produces: `publish/SpectraCollector.exe` and `publish/SpectraCollector-Setup.exe` from one `build/build-all.sh` (tests → publish → `makensis` in `docker.io/library/debian:trixie-slim` with `apt-get install nsis` per run).

- [ ] **Step 1:** `.nsi` — Unicode, MUI2, `RequestExecutionLevel user`, `InstallDir $LOCALAPPDATA\Programs\SpectraCollector`, pages Welcome/Components/Directory/InstFiles/Finish (run app checkbox), section "SpectraCollector" (read-only) with Start menu shortcut + HKCU uninstall key (DisplayName, DisplayVersion, DisplayIcon, UninstallString, Publisher `Lyncis`), section "Desktop shortcut" checked by default, uninstaller removes both shortcuts and the folder; version from `/DVERSION`.
- [ ] **Step 2:** `build-all.sh` (`set -euo pipefail`, version read from the App csproj `<Version>`) → both files in `publish/`; run it, list sizes.
- [ ] **Step 3:** README: what it does, install, how to use both modes, CSV format with `samples.example.csv`, zip layout, rounding rule, build instructions, the two unconfirmed assumptions.
- [ ] **Step 4:** Commit `feat(installer): add NSIS installer and build script`.
