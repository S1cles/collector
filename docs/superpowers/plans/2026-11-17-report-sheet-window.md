# Report Sheet Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the SpectraCollector window with design variant 05 "Report sheet", where the sheet fills in per sample while collecting.

**Architecture:** Core gains per-sample write progress and unique totals; the WPF window is rebuilt from the
spec's "Window" and "Look" sections over the same view-model seams.

**Tech Stack:** .NET 10, WPF (`net10.0-windows`), xUnit; builds only through `build/dotnet.sh` (container).

**Spec:** `docs/superpowers/specs/2026-11-17-spectra-collector-design.md` (sections "Window" and "Look" were
rewritten for variant 05). Reference mockup: `~/notes/lyncis/spectra-collector-design-2026-11-17/mockups/v05-report-sheet.html`.

## Global Constraints

- English only in code, strings, comments, docs (no Cyrillic).
- Warnings are errors; `build/dotnet.sh build` and `build/dotnet.sh test` clean before every commit.
- Anti-goals unchanged: never write under the data root, no half-written zip, no silent incomplete result,
  never freeze the window.
- Colours, fonts, sizes exactly as the spec "Look" section; Lyncis red only for the title mark and band end.
- WPF cannot run on the build host: every `StaticResource`/`DynamicResource` key and binding path in XAML
  must be checked by hand or script and listed in the task report.

## Review Focus

- Overlapping samples: one file counted for both samples, written once; totals count it once.
- 20+ samples and long names (`2026-11-06 13:05:30–13:12:01`): table scrolls, names cut with tooltip.
- Three or more spectrometers: one column each, header and Total aligned.
- Progress events on the UI thread at ~6000 files: no stalls, per-row state matches the overall bar.
- Failure, cancel and nothing-found after rows started filling: the sheet returns to the pre-run table plus
  the outcome line, never a half-filled table that looks finished.

---

### Task 1: Per-sample write progress and unique totals (Core)

**Files:**
- Modify: `src/SpectraCollector.Core/Collector.cs`, `src/SpectraCollector.Core/CollectionReport.cs`
- Test: `tests/SpectraCollector.Core.Tests/CollectorTests.cs`, `tests/SpectraCollector.Core.Tests/CollectionReportTests.cs`

**Interfaces:**
- Produces: `CollectProgress(CollectPhase Phase, int Done, int Total, CollectionReport? Report = null,
  IReadOnlyList<int>? WrittenPerSample = null)`. Searching events unchanged (both null). The first Writing
  event is `(Writing, 0, total, report, zeros)`; every later Writing event carries `Report = null` and a fresh
  snapshot of written files per sample, index-aligned with `CollectRequest.Samples`. A file inside two
  sample windows counts for both.
- Produces: `CollectionReport.FileCount` (unique spectra in the scan) and
  `CollectionReport.TotalBySpectrometer` (`IReadOnlyDictionary<int, int>`, unique files per spectrometer,
  keys in ascending order). `ToText()` unchanged.

- [ ] **Step 1: Tests**
  - `Writing_progress_starts_with_the_report_and_zero_counts`: first Writing event has the same report the
    result carries and `WrittenPerSample` all zeros, length = samples.
  - `Writing_progress_counts_written_files_per_sample`: last Writing event's `WrittenPerSample[i]` equals the
    sum of `CountBySpectrometer` of sample i; values never decrease between events.
  - `Overlapping_samples_count_a_shared_file_for_both`: two samples sharing a minute; the shared files raise
    both counters; zip holds each file once.
  - `Report_totals_count_each_spectrum_once`: with overlapping samples, `FileCount` and
    `TotalBySpectrometer` count unique files.
- [ ] **Step 2:** `build/dotnet.sh test --filter ...` → FAIL.
- [ ] **Step 3:** Implement. Collector maps each written file (OutputArchive reports done count over the
  sorted file list) to the sample indices whose window contains its timestamp; precompute once.
- [ ] **Step 4:** Full suite PASS, build 0 warnings.
- [ ] **Step 5:** Commit `feat(core): report write progress per sample`.

### Task 2: Report sheet window (App + README)

**Files:**
- Modify: `src/SpectraCollector.App/MainWindow.xaml`, `MainWindow.xaml.cs`, `Theme.xaml`, `MainViewModel.cs`,
  `RunView.cs` (split or replace as needed; keep files under ~500 lines), `README.md`
- Consumes: Task 1 `CollectProgress` fields, `CollectionReport.FileCount` / `TotalBySpectrometer`,
  `CollectResult(ZipPath, Report)`, `FormCheck.Request.Samples` for the pre-run rows.

- [ ] **Step 1:** Build the window exactly as the spec "Window" section and the mockup states A–D: form header
  cells (2 or 3 columns, "Change" row), sheet with band top edge, header with title/subtitle/
  `collect-report.txt`/stamp, table (pre-run blank Files column; per-spectrometer columns once writing starts;
  per-row Collecting… bar; Note check or ⚠ n), Total row, numbered footnotes, Zip contents, outcome line,
  fixed footer with status text, Saved path, Open folder, Cancel, primary button. Problems replace the table.
- [ ] **Step 2:** View-model logic that can be unit-tested lives in plain classes (no WPF types) and gets tests in
  `tests/SpectraCollector.Core.Tests` only if it moves to Core; otherwise keep it small and list manual checks.
- [ ] **Step 3:** Static check of every XAML resource key and binding path; result in the report.
- [ ] **Step 4:** README: window description and "Check on Windows" list updated for the sheet (per-row fill,
  footnotes, Zip contents, 20+ samples scroll, minimum size 680×560, keyboard focus in cells).
- [ ] **Step 5:** `build/build-all.sh` passes; commit `feat(app): rebuild the window as a report sheet`.
