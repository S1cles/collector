# SpectraCollector — design

Date: 2026-11-17. Status: approved by Vlad in chat (with the minute-rounding rule and
the "packed folder" output added during review).

## Intent

Building an A.spect project starts with step 1 of "Создание проекта в A.spect": a
service engineer finds the spectrum files for the sampling period on the analyzer PC
(LyncisAC) and packs them. When the Start/Stop Collection button was not used this is
manual and slow — thousands of files in `Tech_Spectra`, or hourly zip archives to pick
and unpack, with Explorer resetting the selection while LyncisAC keeps writing.

SpectraCollector does that step from a CSV of sample times and leaves one zip in a
chosen folder. The engineer copies the zip to their own computer and continues with
step 2 (import into A.spect).

## Goals

- Input: a CSV with sample names and begin/end times; an output folder.
- Find every spectrum file of every spectrometer inside each sample's window, whether
  the spectra archiver is on (hourly zips), off (loose files) or the hour is not
  archived yet (archiver packs ~2 h after the hour).
- Output one zip containing one folder ready for A.spect import.

## Non-goals

- Checking that the sample times are right — the engineer still downloads the tech
  analysis file and verifies the times by hand.
- Creating the A.spect project or any step after import.
- The Start/Stop Collection case (`Collected_Samples`) — that data is already gathered.

## Anti-goals

- Never modify, move or delete anything under the LyncisAC data folder; never disturb
  the running LyncisAC (read with full sharing, no locks).
- Never leave a half-written zip that looks complete.
- Never silently produce an incomplete result: missing data is reported per sample.

## Inputs

Command line:

```
SpectraCollector.exe <samples.csv> <output-folder> [--data D:\LyncisAC_DATA] [--belt Main\Belt_1]
```

CSV — the same columns as A.spect's Exposition Timetable, header row required:

```
Sample;Exposition;Begin;End;Material
S01;;2026-11-06 13:05:00;2026-11-06 13:12:00;
```

- Delimiter `;`, `,` or tab, detected from the header. Double-quoted fields allowed.
- Columns matched by name, case-insensitive; `Sample`, `Begin`, `End` required.
- Times: `yyyy-MM-dd HH:mm[:ss]`, `dd.MM.yyyy HH:mm[:ss]`, `dd/MM/yyyy HH:mm[:ss]`.
  Analyzer local time, no time-zone conversion.

## Selection rule (decided by Vlad)

Each sample's window is `[floor(Begin) to the minute, ceil(End) to the minute]`, both ends
inclusive. Example: 13:30:30–13:40:01 → files from 13:30:00 to 13:41:00.

## Data layout (from the instruction and a screenshot)

```
<data>\<yyyy_MM>\<belt>\Tech_Spectra\Spectra_<n>_Belt_1_2026.11.06_13.11.10.csv
<data>\<yyyy_MM>\<belt>\Packed_Spectra\spect_data_2026-11-06_13.zip   (spectra 13:00–13:59)
```

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
  samples.csv            <- copy of the input CSV
  collect-report.txt     <- the same report as printed on screen
```

The CSV and report sit outside `Tech_Spectra` so A.spect's import never sees them.
The zip is written as `*.partial` and renamed on success. No spectra found → no zip.

## Report

Per sample: window, file count per spectrometer. Warnings for: a sample with no files,
spectrometers with different counts in one sample, a gap between consecutive files
longer than 3× the typical interval, an hour with neither an archive nor loose files.

Exit codes: 0 done (warnings possible), 2 bad arguments or CSV, 3 nothing found,
1 unexpected error.

## Approaches considered

- **C# console exe, self-contained single file (chosen)** — Vlad asked for C#; no .NET
  install on the analyzer PC; parameters fit scripting and a pasted command line.
- PowerShell script — no build step, but not what was asked and harder to test.
- Windows app with a form — more work, and parameters were asked for.

## Builder's calls (Claude)

- .NET 10, `win-x64`, self-contained, single file, trimmed; no runtime dependencies
  beyond the BCL (`System.IO.Compression`); xUnit for tests.
- Code, messages and docs in English (Lyncis code-language rule).
- Assumptions to confirm on a real analyzer: spectra end in `.csv`; archive hour names
  use the same local time as file names.
