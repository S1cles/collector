# SpectraCollector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows console exe that collects LyncisAC spectrum files for the sample windows in a CSV and packs them into one zip.

**Architecture:** Pure parsers (CSV, file names, time windows) feed a `SpectraSource` that reads hourly archives and loose `Tech_Spectra` files through a `DataLayout`; a report summarises per-sample coverage; `OutputArchive` writes the zip atomically; `App` wires it to the command line and exit codes.

**Tech Stack:** C# / .NET 10 (`net10.0`), BCL only (`System.IO.Compression`), xUnit tests, publish `win-x64` self-contained single file, trimmed.

**Spec:** `docs/superpowers/specs/2026-11-17-spectra-collector-design.md`

## Global Constraints

- Never write, move or delete under the data root; open every input with `FileShare.ReadWrite | FileShare.Delete`.
- Window = `[floor(Begin) to minute, ceil(End) to minute]`, both inclusive (13:30:30–13:40:01 → 13:30:00–13:41:00).
- Spectrum name: `Spectra_<n>_<anything>_yyyy.MM.dd_HH.mm.ss[.<ext>]`; hour archive: `Packed_Spectra\spect_data_yyyy-MM-dd_HH.zip`; month folder `yyyy_MM`.
- Zip layout: `<base>/Tech_Spectra/<spectra>`, `<base>/<input csv name>`, `<base>/collect-report.txt`; `<base>` = `spectra_yyyy-MM-dd_HHmm` of the earliest Begin.
- Exit codes: 0 done, 1 unexpected error, 2 bad arguments or CSV, 3 nothing found.
- All code, messages and comments in English; invariant culture everywhere.

## Review Focus

- CSV saved by Excel: UTF-8 BOM, `;`, CRLF, trailing empty lines → parses (test in Task 2).
- LyncisAC holds the newest file open for writing → it is still collected, no crash (test in Task 3).
- Overlapping samples, or a file present both in an hour archive and in `Tech_Spectra` → one copy (test in Task 3).
- Archive entries stored under a subfolder inside the hour zip → matched by file name (test in Task 3).
- Output folder missing, or the zip name already taken → folder created, `_2` suffix used (test in Task 5).

---

### Task 1: Solution, time windows, spectrum names

**Files:**
- Create: `SpectraCollector.slnx`, `src/SpectraCollector/SpectraCollector.csproj`, `src/SpectraCollector/TimeWindow.cs`, `src/SpectraCollector/SpectrumName.cs`, `src/SpectraCollector/CollectorException.cs`
- Test: `tests/SpectraCollector.Tests/SpectraCollector.Tests.csproj`, `tests/SpectraCollector.Tests/TimeWindowTests.cs`, `tests/SpectraCollector.Tests/SpectrumNameTests.cs`

**Interfaces:**
- Produces: `readonly record struct TimeWindow(DateTime From, DateTime To)` with `static TimeWindow Rounded(DateTime begin, DateTime end)`, `bool Contains(DateTime t)`, `IEnumerable<DateTime> Hours()` (each hour start from `From` to `To`).
- Produces: `sealed record SpectrumName(string FileName, int Spectrometer, DateTime Timestamp)` with `static bool TryParse(string fileName, out SpectrumName? name)`.
- Produces: `abstract class CollectorException(string message) : Exception` with `abstract int ExitCode`; `UsageException` (2), `NothingFoundException` (3).

- [ ] **Step 1: Tests** — `Rounded(13:30:30, 13:40:01)` → `13:30:00..13:41:00`; `Rounded(13:30:00, 13:40:00)` stays; `Contains` true at both ends, false at 13:41:01; `Hours()` for 12:59:30–14:00:10 → 12:00, 13:00, 14:00. `TryParse("Spectra_0_Belt_1_2026.11.06_13.11.10.csv")` → spectrometer 0, 2026-11-06 13:11:10; also without extension; false for `spect_data_2026-11-06_13.zip`, `Spectra_x_...`, `Spectra_0_Belt_1_2026.13.06_13.11.10.csv`.
- [ ] **Step 2:** `dotnet test` → FAIL (types missing).
- [ ] **Step 3:** Implement; `csproj` with `Nullable`, `TreatWarningsAsErrors`, `InvariantGlobalization`, and the publish properties (`RuntimeIdentifier` set only at publish time).
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat: add time windows and spectrum name parsing`.

### Task 2: Sample table (CSV)

**Files:**
- Create: `src/SpectraCollector/Sample.cs`, `src/SpectraCollector/SampleTable.cs`
- Test: `tests/SpectraCollector.Tests/SampleTableTests.cs`

**Interfaces:**
- Consumes: `TimeWindow.Rounded`, `CollectorException`.
- Produces: `sealed record Sample(string Name, DateTime Begin, DateTime End, int Line)` with `TimeWindow Window`; `static IReadOnlyList<Sample> SampleTable.Parse(TextReader reader)`; `SampleTableException(int line, string message)` (exit 2, message starts `Line {line}: `).

- [ ] **Step 1: Tests** — header-only names any case/order; delimiters `;` `,` tab; quoted field with the delimiter inside; formats `2026-11-06 13:05`, `2026-11-06 13:05:00`, `06.11.2026 13:05:00`, `06/11/2026 13:05:00`; Excel text with BOM + CRLF + blank trailing lines; errors with line numbers for: missing `Begin` column, bad date, `End` < `Begin`, empty sample name, no data rows.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement (`ParseExact` with the format list, `CultureInfo.InvariantCulture`). **Step 4:** run → PASS.
- [ ] **Step 5:** Commit `feat: parse the sample table CSV`.

### Task 3: Finding spectra on the analyzer

**Files:**
- Create: `src/SpectraCollector/DataLayout.cs`, `src/SpectraCollector/FoundSpectrum.cs`, `src/SpectraCollector/SpectraSource.cs`
- Test: `tests/SpectraCollector.Tests/SpectraSourceTests.cs`, `tests/SpectraCollector.Tests/FakeAnalyzer.cs` (builds a data tree under a per-test temp dir)

**Interfaces:**
- Consumes: `Sample`, `TimeWindow`, `SpectrumName`.
- Produces: `sealed record DataLayout(string DataRoot, string Belt)` with `string TechSpectraDir(DateTime hour)`, `string HourArchive(DateTime hour)`.
- Produces: `sealed record FoundSpectrum(SpectrumName Name, string SourcePath, string? EntryName)` with `Stream Open()` (loose file when `EntryName` is null, else that zip entry; read-only, full sharing).
- Produces: `sealed record SourceScan(IReadOnlyList<FoundSpectrum> Files, IReadOnlyList<DateTime> EmptyHours)`; `SourceScan SpectraSource.Find(DataLayout layout, IReadOnlyList<Sample> samples)` — files sorted by timestamp then spectrometer, unique by file name; `EmptyHours` = window hours with neither archive nor loose files.

- [ ] **Step 1: Tests** — loose only; archive only; hour 13 archived + hour 14 loose in one window; window crossing 2026-10-31 23:59 → 2026-11-01 (two month folders); edge files 13:30:00 and 13:41:00 included, 13:29:50 and 13:41:10 excluded; two spectrometers both collected; unrelated file names ignored; overlap/duplicate and subfolder-entry cases from Review Focus; a loose file held open by a `FileStream` with `FileShare.ReadWrite` is still collected; missing month folder → hour reported in `EmptyHours`, no exception.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement — per hour: archive entries filtered by `Window.Contains`, then `Directory.EnumerateFiles(dir, "Spectra_*_" + hour.ToString("yyyy.MM.dd_HH") + ".*")` plus the same pattern without extension. **Step 4:** run → PASS.
- [ ] **Step 5:** Commit `feat: find spectra in hour archives and Tech_Spectra`.

### Task 4: Collection report

**Files:**
- Create: `src/SpectraCollector/CollectionReport.cs`
- Test: `tests/SpectraCollector.Tests/CollectionReportTests.cs`

**Interfaces:**
- Consumes: `Sample`, `SourceScan`.
- Produces: `sealed record SampleSummary(Sample Sample, IReadOnlyDictionary<int, int> CountBySpectrometer)`; `sealed class CollectionReport` with `static CollectionReport Build(IReadOnlyList<Sample> samples, SourceScan scan)`, `IReadOnlyList<SampleSummary> Samples`, `IReadOnlyList<string> Warnings`, `string ToText()`.

- [ ] **Step 1: Tests** — counts per sample and spectrometer; warnings: sample with zero files; different counts between spectrometers in one sample; gap > 3× median interval of that spectrometer in that sample (e.g. 10 s cadence with one 40 s hole); each empty hour. `ToText()` lists every sample with window `HH:mm:ss–HH:mm:ss` and counts.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement. **Step 4:** run → PASS.
- [ ] **Step 5:** Commit `feat: report per-sample coverage and gaps`.

### Task 5: Output archive

**Files:**
- Create: `src/SpectraCollector/OutputArchive.cs`
- Test: `tests/SpectraCollector.Tests/OutputArchiveTests.cs`

**Interfaces:**
- Consumes: `FoundSpectrum.Open()`.
- Produces: `static string OutputArchive.Write(string outputDir, string baseName, IReadOnlyList<FoundSpectrum> files, string csvPath, string reportText)` → full path of the written zip.

- [ ] **Step 1: Tests** — zip has exactly `<base>/Tech_Spectra/<each file>`, `<base>/<csv name>`, `<base>/collect-report.txt`, byte-identical content; missing output dir created; existing `<base>.zip` → `<base>_2.zip`; no `*.partial` left after success; on a failing `Open()` the partial file is removed and the exception propagates.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement — write `<name>.zip.partial`, `CompressionLevel.Optimal`, then `File.Move`. **Step 4:** run → PASS.
- [ ] **Step 5:** Commit `feat: write the output zip atomically`.

### Task 6: Command line, end-to-end, publish

**Files:**
- Create: `src/SpectraCollector/CliOptions.cs`, `src/SpectraCollector/App.cs`, `src/SpectraCollector/Program.cs`, `README.md`, `samples.example.csv`
- Test: `tests/SpectraCollector.Tests/CliOptionsTests.cs`, `tests/SpectraCollector.Tests/AppTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `sealed record CliOptions(string CsvPath, string OutputDir, string DataRoot, string Belt)` with `static CliOptions Parse(string[] args)` (defaults `D:\LyncisAC_DATA`, `Main\Belt_1`; `UsageException` with the usage text); `static int App.Run(CliOptions options, TextWriter output)`.

- [ ] **Step 1: Tests** — options: positional pair, `--data`/`--belt`, unknown flag and missing value → `UsageException`. App on `FakeAnalyzer`: exit 0 and a zip; bad CSV → 2 with the line message; no spectra → 3 and no zip; report text printed.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement; `Program.Main` catches `CollectorException` → message + `ExitCode`, anything else → full exception + 1. **Step 4:** run → PASS.
- [ ] **Step 5:** `dotnet publish src/SpectraCollector -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true -o publish/win-x64` → one `SpectraCollector.exe`, no trim warnings.
- [ ] **Step 6:** README (usage, CSV format, zip layout, exit codes) + example CSV. Commit `feat: add command line and Windows build`.
