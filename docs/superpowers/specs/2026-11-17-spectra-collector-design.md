# SpectraCollector — design

Date: 2026-11-17. Status: approved by Vlad in chat. Revised the same day: a window with
Begin/End fields instead of a command line, an installer with a desktop-shortcut
checkbox, a pleasant look, minute rounding, output as one packed folder.

## Intent

Building an A.spect project starts with step 1 of the "Creating a project in A.spect" instruction: a
service engineer finds the spectrum files for the sampling period on the analyzer PC
(LyncisAC) and packs them. When the Start/Stop Collection button was not used this is
manual and slow — thousands of files in `Tech_Spectra`, or hourly zip archives to pick
and unpack, with Explorer resetting the selection while LyncisAC keeps writing.

SpectraCollector does that step: the engineer gives sample times (a CSV, or one
Begin/End pair typed in), picks a folder, presses one button and gets one zip. They copy
the zip to their own computer and continue with step 2 (import into A.spect).

## Goals

- Sample times from a CSV file, or from Begin/End fields for a single interval.
- Find every spectrum file of every spectrometer inside each sample's window, whether
  the spectra archiver is on (hourly zips), off (loose files) or the hour is not
  archived yet (archiver packs ~2 h after the hour).
- Output one zip with one folder ready for A.spect import.
- A small Windows app that is pleasant to look at, installed by a `Setup.exe` with a
  "Create a desktop shortcut" checkbox; also a standalone exe that needs no install.

## Non-goals

- Checking that the sample times are right — the engineer still downloads the tech
  analysis file and verifies the times by hand.
- Creating the A.spect project or any step after import.
- The Start/Stop Collection case (`Collected_Samples`) — that data is already gathered.
- A command-line mode (dropped once the window was asked for).

## Anti-goals

- Never modify, move or delete anything under the LyncisAC data folder; never disturb
  the running LyncisAC (read with full sharing, no locks).
- Never leave a half-written zip that looks complete (also on Cancel).
- Never silently produce an incomplete result: missing data is reported per sample.
- Never freeze the window while collecting.

## Sample times

CSV — the same columns as A.spect's Exposition Timetable, header row required:

```
Sample;Exposition;Begin;End;Material
S01;;2026-11-06 13:05:00;2026-11-06 13:12:00;
```

- Delimiter `;`, `,` or tab, detected from the header. Double-quoted fields allowed.
- Columns matched by name, case-insensitive; `Begin`, `End` required. An empty or missing
  `Sample` is named by the row's times (Vlad, 2026-11-17): `2026-11-06 13:05:30–13:12:01`.
- Times: `yyyy-MM-dd HH:mm[:ss]`, `dd.MM.yyyy HH:mm[:ss]`, `dd/MM/yyyy HH:mm[:ss]`.
  Analyzer local time, no time-zone conversion.

Single interval — Begin and End text fields accepting the same time formats, one sample
named `Sample_1`. A `samples.csv` in the format above is generated for the zip.

## Selection rule (decided by Vlad)

Each sample's window is `[floor(Begin) to the minute, ceil(End) to the minute]`, both ends
inclusive. Example: 13:30:30–13:40:01 → files from 13:30:00 to 13:41:00. The window shows
the rounded range live under the Begin/End fields.

## Data layout (from the instruction and a screenshot)

```
<data>\<yyyy_MM>\<belt>\Tech_Spectra\Spectra_<n>_Belt_1_2026.11.06_13.11.10.csv
<data>\<yyyy_MM>\<belt>\Packed_Spectra\spect_data_2026-11-06_13.zip   (spectra 13:00–13:59)
```

- Defaults: data `D:\LyncisAC_DATA`, belt `Main\Belt_1`; both editable, remembered.
- File time comes from the name (`yyyy.MM.dd_HH.mm.ss`); `<n>` is the spectrometer.
- For every hour a window touches: read the hour's archive if present, and also scan
  `Tech_Spectra` for that hour. A file seen in both is taken once.
- The month folder is chosen per hour, so windows crossing a month boundary work.

## Output

`<output>\spectra_<yyyy-MM-dd_HHmm of the earliest Begin>.zip` (suffix `_2`, `_3`… if
the name is taken), containing:

```
spectra_2026-11-06_1305/
  Tech_Spectra/          <- spectra only; this is the folder to import in A.spect
  samples.csv            <- the input CSV under its own name, or the generated samples.csv
  collect-report.txt     <- the same report as shown in the window
```

The CSV and report sit outside `Tech_Spectra` so A.spect's import never sees them.
The zip is written as `*.partial` and renamed on success; Cancel or an error removes it.
No spectra found → no zip.

## Report

Per sample: rounded window, file count per spectrometer. Warnings for: a sample with no
files, spectrometers with different counts in one sample, a spectrometer starting late or
ending early in a sample, a gap between consecutive files longer than 3× the typical
interval, an hour with neither an archive nor loose files. After success: the zip path and
an "Open folder" button.

## Window (variant 05 "Report sheet", chosen by Vlad 2026-11-17)

Reference mockup (states A ready, B running, C done, D minimum size):
`~/notes/lyncis/spectra-collector-design-2026-11-17/mockups/v05-report-sheet.html`.
The window is a collection slip: a compact form header of bordered cells, and below it a
sheet that is the report and fills in while collecting.

```
 ▌SpectraCollector
 ┌ Samples ──────────────────┬ CSV file ──────────────────────────────┐
 │ [From CSV file|Single int]│ D:\Samples\2026-11-06 belt 1.csv Browse…│
 ├ Save zip to ──────────────┼ Analyzer data ─────────────────────────┤
 │ D:\Out            Browse… │ D:\LyncisAC_DATA\<month>\Main\…  Change │
 └───────────────────────────┴────────────────────────────────────────┘
 ┌▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔ spectrum band = progress ▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔┐
 │ 2026-11-06 belt 1.csv                        collect-report.txt   │
 │ 3 samples, files from 2026-11-06 13:05:00 to 13:51:00   [✓ Saved] │
 │ Sample   Files from   to         Spectra_0   Spectra_1     Note   │
 │ S01      13:05:00     13:12:00          43          43        ✓   │
 │ S02      13:20:00     13:30:00          61          58      ⚠ 1   │
 │ Total    327 files                     165         162            │
 │ 1  S02: Spectra_1 ends at 13:27:40, 2 min before End.             │
 │ Zip contents: spectra_…/  Tech_Spectra/ 327 files, CSV, report    │
 ├───────────────────────────────────────────────────────────────────┤
 │ Saved D:\Out\spectra_2026-11-06_1305.zip  [Open folder] [Collect] │
 └───────────────────────────────────────────────────────────────────┘
```

- **Form header.** Cells share one frame with 1 px lines; in each cell a small label on top,
  the value, a button on the right. CSV mode, two columns: Samples | CSV file, then Save zip
  to | Analyzer data. Single interval, three columns: Samples | Begin | End, then Save zip
  to | Analyzer data (two columns wide). "Change" adds a row: Data folder (two columns wide) |
  Belt folder. Keyboard focus is a 2 px Ink frame inside the cell. While a run is going the
  form is disabled.
- **Sheet header.** Title: the CSV file name, or "Single interval". Subtitle: the window hint.
  Right: `collect-report.txt` and a stamp: "Searching…", "Collecting…", "Cancelling…" (Ink),
  "Saved" with a check (success), "Cancelled" (Graphite), "Not saved" (error colour; warning
  colour when nothing was found).
- **Table before a run.** One row per parsed sample as soon as the form is valid: Sample,
  Files from, to (the rounded window, date added when it crosses midnight), one blank Files
  column (dotted), empty Note. If the form has problems the problems list replaces the table,
  in warning colour.
- **While collecting.** Searching: rows as before. Writing: the Files column splits into one
  column per spectrometer; finished rows show their counts, the row being written shows
  "Collecting…" with a thin bar, rows not reached stay blank.
- **After success.** Counts per spectrometer; Note = check (success) or "⚠ n" footnote
  numbers (warning). A Total row under a double rule: "N files" (unique spectra in the zip) and
  per-spectrometer totals. Footnotes: every report warning numbered in report order (warnings
  not tied to a sample, such as empty hours, have no mark in Note). "Zip contents" block in
  monospace: `<zip folder>/`, `Tech_Spectra/  N files`, the CSV name, `collect-report.txt`.
- **Failure or cancel.** The pre-run table stays; the outcome message takes the footnote place
  (error colour; warning colour for nothing found; Graphite for "Cancelled. Nothing was saved.").
- **Sheet footer** (fixed): left, while running "196 of 327 files · 60 %" (searching:
  "Searching hour 3 of 8"), after success "Saved <zip path>" and "Open folder"; right, "Cancel"
  while running and the primary button ("Collect spectra" / "Searching…" / "Collecting…" /
  "Cancelling…"). The sheet body scrolls for long tables; header and footer stay.
- Long sample names are cut with an ellipsis and show in full as a tooltip. More than two
  spectrometers get one column each.

## Look

Subject: a field tool on an analyzer PC at a mine or cement plant; one job, done a few
times a month. Quiet, industrial, legible — one memorable element.

- **The memorable element:** the 4 px spectrum band, now the top edge of the sheet —
  violet → blue → green → yellow → orange → Lyncis red. Idle it is faint (30 % opacity);
  while collecting it fills left to right as the progress bar; full after success.
- **Colour:** Ink `#1C2630` (text, primary button, table header rule), Graphite `#556270`
  (secondary text), window `#E9EDF1`, Surface `#FFFFFF` (cells, sheet), cell lines and input
  borders `#B8C2CC`, sheet border `#C3CBD3`, row rules `#E1E6EA`, sheet footer `#F7F9FA`, zip
  block `#F4F6F8` with text `#3B4754`, Lyncis red `#DA291C` (title mark and band end only —
  never a button). Status: warning `#9A5B00` (was `#B26A00`, 4.2:1 on white, below AA),
  error `#B42318`, success `#1F7A4D`. Band stops: `#6A3FB5 #2D6CDF #1E9E6A #E0B000 #E0661B
  #DA291C`.
- **Type:** Bahnschrift for the title (16), the sheet title (19, semibold), sample names,
  times and counts (13, tabular figures); Segoe UI for labels (cell labels 11.5, Graphite) and
  text; Cascadia Mono, then Consolas, for `collect-report.txt` and the zip contents (12).
  Sentence case, no all-caps labels.
- **Layout:** 20 px window margin, 4 px corner radius on buttons and the form frame, no
  shadows. Window 780×700, resizable, minimum 680×560.
- **Copy:** plain verbs — "Collect spectra", "Collecting…", "Saved", "Open folder";
  errors say what is wrong and what to do ("End is earlier than Begin.").

## Installer

- NSIS `SpectraCollector-Setup.exe`, per-user install into
  `%LOCALAPPDATA%\Programs\SpectraCollector` — no administrator rights needed.
- Components page: "Desktop shortcut" (checked by default); Start menu shortcut always;
  uninstaller listed in Windows "Apps".
- Also shipped: the standalone `SpectraCollector.exe` (same file the installer copies).

## Approaches considered

- **WPF app + NSIS installer, built in containers on lyncis-dev (chosen).** WPF
  publishes for Windows from Linux (checked: single exe ≈ 65 MB); NSIS builds natively
  on Linux (checked: makensis 3.11) and gives the checkbox.
- MSI via `wixl` — builds on Linux but has no installer dialogs, so no checkbox.
- Inno Setup under Wine — needs a ~2 GB image; lyncis-dev has 4.6 GB free.
- WinForms — has a ready date-time picker but looks dated and resists styling.

## Builder's calls (Claude)

- .NET 10. Core logic in a `net10.0` library, tested with xUnit on Linux; the WPF app
  (`net10.0-windows`) is a thin shell over it. The window can only be checked on Windows.
- Collection runs on a background task with progress and cancellation; closing the
  window during a run cancels it and waits for it to finish.
- Settings (last CSV, output folder, data folder, belt, mode) in
  `%LOCALAPPDATA%\SpectraCollector\settings.json`.
- Code, UI text and docs in English (Lyncis code-language rule).
- Assumptions to confirm on a real analyzer: spectra end in `.csv`; archive hour names
  use the same local time as file names.
