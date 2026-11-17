# SpectraCollector

A small Windows tool for service engineers. It collects the spectrum files of a LIBS
analyzer for given sample time windows and packs them into one zip, ready to import
into A.spect. This is step 1 of building an A.spect project: the analyzer writes
spectra continuously, and the project needs only the files that belong to the samples.

## Install

- **Installer:** run `SpectraCollector-Setup.exe`. It installs per user into
  `%LOCALAPPDATA%\Programs\SpectraCollector`, needs no administrator rights, adds a
  Start menu shortcut, and offers a "Desktop shortcut" checkbox (checked by default).
  Uninstall from Windows "Apps". Close SpectraCollector before reinstalling or uninstalling.
- **Standalone:** copy `SpectraCollector.exe` anywhere and run it. It is the same file
  the installer copies; nothing else is needed.

## Use

Pick the data folder (default `D:\LyncisAC_DATA`), the belt (default `Main\Belt_1`) and an
output folder, then choose one of two modes and press "Collect spectra".

- **From CSV file:** choose a CSV with the sample times. Every row is one sample. The file
  goes into the zip unchanged, under its own name.
- **Single interval:** type Begin and End. One sample named `Sample_1` is used, and a
  `samples.csv` is generated for the zip.

The window is a collection slip. At the top, the form: bordered cells for the mode, the CSV
file or Begin and End, the output folder and the analyzer data folder ("Change" adds cells
for the data folder and the belt). Below it, the report sheet, which becomes
`collect-report.txt`:

- Before a run it lists every sample with its rounded window ("Files from", "to") and a blank
  Files column, or, while the form has problems, the problems instead.
- While collecting, the spectrum band on top of the sheet fills, the Files column splits into
  one column per spectrometer, and each row fills in as its files are written ("Collecting…"
  with a thin bar on the row being written). The footer counts hours, then files.
- After success: counts per spectrometer, a check or a numbered warning mark in Note, a Total
  row with the unique files in the zip, the warnings as numbered footnotes, and the zip
  contents. The footer shows the saved path and "Open folder", which selects the zip in
  Explorer.
- Cancel stops a run and leaves nothing behind. After a cancel, a failure or no spectra found,
  the sheet shows the blank rows again with the reason underneath.

The sheet keeps the last result until a field changes, or until the CSV on disk or the data
folder no longer gives the same samples when you come back to the window. The fields are
saved after each successful run, in `%LOCALAPPDATA%\SpectraCollector\settings.json`.

### CSV format

Same columns as A.spect's Exposition Timetable. The header row is required
(see `samples.example.csv`):

```
Sample;Exposition;Begin;End;Material
S01;;2026-11-06 13:05:00;2026-11-06 13:12:00;Feldspar
```

- Delimiter `;`, `,` or tab, detected from the header. Double-quoted fields are allowed.
- Columns are matched by name, case-insensitive. `Begin` and `End` are required.
- `Sample` is optional. A sample with an empty name, or every sample when the column is
  missing, is named by its times as written, e.g. `2026-11-06 13:05:30–13:12:01`.
- Times: `yyyy-MM-dd HH:mm[:ss]`, `dd.MM.yyyy HH:mm[:ss]`, `dd/MM/yyyy HH:mm[:ss]`.
  This is the analyzer's local time; no time-zone conversion is done.

### Selection rule

Each sample's window is `[floor(Begin) to the minute, ceil(End) to the minute]`, both ends
inclusive. Example: 13:30:30 to 13:40:01 selects files from 13:30:00 to 13:41:00.

### Where it looks for files

```
<data>\<yyyy_MM>\<belt>\Tech_Spectra\Spectra_<n>_Belt_1_2026.11.06_13.11.10.csv
<data>\<yyyy_MM>\<belt>\Packed_Spectra\spect_data_2026-11-06_13.zip   (spectra 13:00-13:59)
```

- The file time comes from the name (`yyyy.MM.dd_HH.mm.ss`); `<n>` is the spectrometer.
- For every hour a window touches, it reads that hour's archive if present and also scans
  `Tech_Spectra` for the same hour. A file found in both is taken once, from the archive.
- The month folder is chosen per hour, so windows crossing a month boundary work.
- It never writes, moves or deletes anything under the data folder.

### Output

`<output>\spectra_<yyyy-MM-dd_HHmm of the earliest Begin>.zip` (suffix `_2`, `_3` if the
name is taken):

```
spectra_2026-11-06_1305/
  Tech_Spectra/          <- spectra only; import this folder in A.spect
  samples.csv            <- the input CSV under its own name, or the generated samples.csv
  collect-report.txt     <- the same report as shown in the window
```

The CSV and the report sit outside `Tech_Spectra`, so the A.spect import never sees them.
If no spectra are found, no zip is written. The report also warns when a spectrometer starts
later than Begin or stops earlier than End by more than three typical intervals (at least a
minute).

## Build

Needs only podman; no .NET SDK or NSIS on the host. Containers are
`mcr.microsoft.com/dotnet/sdk:10.0` and `docker.io/library/debian:trixie-slim` (NSIS is
installed in it on each run).

```
build/build-all.sh
```

It runs the tests, publishes the single-file Windows exe, builds the installer, and writes
`publish/SpectraCollector.exe` and `publish/SpectraCollector-Setup.exe`. The version is
read from `<Version>` in `src/SpectraCollector.App/SpectraCollector.App.csproj`.
Run other .NET commands through `build/dotnet.sh`.

## Unconfirmed assumptions

To be checked on a real analyzer:

- Spectrum files end in `.csv`.
- Archive hour names (`spect_data_yyyy-MM-dd_HH.zip`) use the same local time as the
  file names.

## Check on Windows

The window could only be built here, never run. On first start check:

1. The app starts at all, and looks like the reference mockup (state A ready): form cells in
   one frame with 1 px lines, the sheet below with the faint spectrum band as its top edge.
2. Typed text and the Begin/End placeholder start at the same x as the cell label above them.
3. Keyboard focus: Tab walks the cells in reading order; the focused cell shows a 2 px Ink
   frame inside it (rounded at the form corners, never poking out); buttons and the mode
   toggle also show their own focus outline. "Change" moves focus to the Data folder cell.
4. The mode toggle has square inner and rounded outer corners. CSV mode shows two columns,
   Single interval three; "Change" adds Data folder (two columns wide) and Belt folder.
5. A run: the form greys out; the stamp reads "Searching…", then "Collecting…"; the footer
   reads "Searching hour n of m", then "n of m files · p %". The band fills while searching
   and again while writing. The Files column splits into Spectra_0, Spectra_1 (one column per
   spectrometer, headers over their numbers); finished rows show counts, the row being written
   shows "Collecting…" with a bar, rows not reached stay dotted. No stutter on a long run
   (an 8-hour window writes about 6000 files).
6. After success: stamp "Saved" with a check, band full, Note shows a check or "⚠ n";
   footnotes are numbered in report order and match the marks; warnings without a sample
   (empty hours) have a footnote but no mark; Total sits under a double rule with the same
   column alignment; "Zip contents" lists the folder, `Tech_Spectra/` with the file count, the
   CSV name and `collect-report.txt`, and matches the zip opened in Explorer. With overlapping
   samples a shared spectrum counts in both rows but once in Total.
7. Cancel, a failure, and a window with no spectra: the rows go blank again (never a
   half-filled table), the stamp reads "Cancelled" (Graphite) or "Not saved" (error colour;
   warning colour when nothing was found), and the reason shows under the table. Esc cancels,
   Enter collects.
8. Closing the window mid-run cancels, closes, and leaves no `.partial` file.
9. "Open folder" selects the zip in Explorer. The icon shows in the title bar, taskbar and
   Explorer.
10. A CSV with 20+ samples: the table body scrolls while the sheet title, the column headers
    and the footer stay; the scroll bar does not shift the columns. A long sample name such
    as `2026-11-06 13:05:30–13:12:01` is cut with an ellipsis and shows in full as a tooltip.
    A sample crossing midnight shows dates in both time cells.
11. The 680x560 minimum size in Single interval mode with "Change" open and several problems
    listed, and at 125 % and 150 % scaling: nothing overlaps or is cut except with an
    ellipsis.
12. Bahnschrift semibold (sheet title, sample names, stamp) renders as a real semibold, not a
    smeared synthetic bold; `collect-report.txt` and the zip contents use Cascadia Mono, or
    Consolas where it is missing.
13. At 680x560 with three spectrometers, or a sample crossing midnight: the Note column
    (the ⚠ marks) stays visible.
14. After a run, save the CSV with a bad time in Excel and switch back: the sheet shows the
    problem and Collect is disabled. After a run, go to "Open folder" and back: the result
    stays.

In the field, on a real analyzer:

1. A run while LyncisAC is running and writing new spectra: nothing is disturbed and the
   new files of the window are collected.
2. A run while the spectra archiver packs an hour: the hour is still collected, or the run
   fails with the hint to run it again.
3. The CSV still open in Excel: it is read without an error.
4. A sample name with non-ASCII letters (e.g. Lithuanian) from a CSV that Excel saved as plain
   "CSV": the name shows correctly and the CSV in the zip is byte-for-byte the original.
5. Reinstalling over a running copy: the installer cannot replace the running exe; closing
   SpectraCollector and choosing Retry must finish the install.
6. Cancel during a long search (a month of loose files with the archiver off): the button
   reads "Cancelling…" and the run stops promptly.

For the installer: it installs without a UAC prompt, the desktop checkbox works, "Run
SpectraCollector" starts the app, and uninstall removes the shortcuts, the exe and the
folder but keeps `settings.json`.
