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

The window shows the rounded time range under the Begin and End fields. Cancel stops a run
and leaves nothing behind. When it finishes, the report is shown and "Open folder" selects
the zip in Explorer. The fields are saved after each successful run, in
`%LOCALAPPDATA%\SpectraCollector\settings.json`.

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

1. The app starts at all.
2. Typed text and the placeholder start at the same x in the input boxes.
3. The spectrum band is faint when idle, fills left to right while searching ("Searching…")
   and again while writing ("Collecting…"), is full after success, and is faint again after
   cancel or failure.
4. The segmented toggle has square inner and rounded outer corners; the focus outline
   shows with Tab; Esc and Enter behave.
5. Closing the window mid-run shows "Cancelled. Nothing was saved.", closes, and leaves no
   `.partial` file.
6. "Open folder" selects the zip in Explorer. The icon shows in the title bar, taskbar and
   Explorer.
7. 125 % and 150 % scaling, and the 680x560 minimum size with the data editor open and
   several problems listed.

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
