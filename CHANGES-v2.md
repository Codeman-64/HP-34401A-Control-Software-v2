# HP 34401A (RS-232 Serial Port) - v2: stage 1 changes

## Item 9 - RS-232 framing
- The COM window offers only what the 34401A supports: 300-9600 baud, parity None/Even/Odd.
- Data bits follow parity (None = 8, Even/Odd = 7); stop bits are always 2 (8N2 / 7E2 / 7O2).
- Old saved settings (8N1, >9600 baud, Mark/Space) are converted on first load, with a warning in the log.
- Flow control fallback is None (the old fallback was Xon/Xoff).
- SYST:REM is now sent before *RST on connect.

## Bug fixes
1. Stale replies: the input buffer is flushed before each READ? and command, and after a timeout
   the late reply is waited for and discarded, so readings no longer drift one behind.
2. Exit: the meter is returned to local (SYST:LOC) and the port is closed after any reading in progress
   finishes. The Closing handler was never wired up in the original XAML; it is now.
3. Adapter unplugged: the app retries opening the port every 2 s and logs "Reconnected".
   A failure opening the port at connect no longer crashes the app.
4. The COM open check now really fails when the port can't open; error messages show the port name.
5. Output-log file writing is thread-safe and no longer touches UI controls from the data thread.
6. Measurement files roll over at local midnight (was UTC = 4 pm Pacific); DIODE/CONT error text fixed.
7. Graph: data is inserted on the graph window's UI thread (no race with rendering);
   overload/open readings are counted as invalid instead of vanishing.
8. All threads parse numbers with en-US culture.

## Setting up the git history (once)
Run these in this folder (Developer PowerShell in Visual Studio, or Git Bash):

    git init -b main
    git fetch v2-history.bundle main
    git reset FETCH_HEAD

`git log` then shows the baseline (your original code) plus one commit per group of changes.
`git diff HEAD~3` shows everything changed in stage 1. You can delete the bundle file afterwards.

# Stage 2 changes

- **Faster commands:** the fixed 1.2 s pause after every command and the 0.1 s pause before every
  query reply are gone. After each command the app sends `*OPC?` and continues as soon as the meter
  answers "1" (i.e. when it has really finished).
- **Timestamps:** everywhere (output log, measurement files, table, graph and its exports) now
  `yyyy-MM-dd HH:mm:ss.fff`, e.g. `2026-11-16 13:05:03.123`: 24-hour, millisecond resolution,
  sorts correctly in Excel. Files written by earlier versions keep their old format.
- **Output log:** keeps the latest lines and trims the oldest, instead of wiping everything.
  Main window: 500 / 2000 (default) / 5000 lines. Graph and Math windows: 250 / 1000 (default) /
  2000 / 5000. Menu renamed "Output Log Length".

## Updating the git history after stage 2 (once)

    git fetch v2-stage2.bundle main
    git reset FETCH_HEAD

# Stage 3 changes

- **New icon:** an original bench-multimeter icon (dark front, green readout, input terminals),
  with hand-tuned 16 and 24 px versions for the title bar and taskbar.
- **Meter error checking:** after every command the app reads `SYST:ERR?` until the queue is
  empty and logs each meter error in red (e.g. `-113,"Undefined header"`).
- **Meter Tools window** (new **Meter Tools** menu, enabled after connecting):
  - *Math:* Null (offset or next reading), dB (reference in dBm or next reading), dBm
    (reference resistance 50-8000 Ω), and limit test (lower/upper). dB/dBm readings are shown
    without SI prefixes and labelled dB/dBm. With the limit test, the unit label turns into a red
    HI or LO when a reading is out of limits, and each change is logged. Changing the
    measurement function turns math off (the meter does this too).
  - *Burst capture:* 2-512 readings at full meter speed into the meter's memory
    (INIT / FETC?), then one transfer. Shows acquisition time, readings per second, min, max,
    mean and standard deviation. Readings also go to the display, graph, table and log files,
    and each burst is saved as a CSV in `<log folder>\Burst\`. The trigger source is restored afterwards.
- **Saved settings:** the last function, range, NPLC/resolution, auto zero, input impedance,
  AC bandwidth, gate time and update speed are saved to `%LocalAppData%\HP 34401A\Session.json`
  and re-applied after connecting. Turn off with Config -> Restore Last Settings on Connect.

## Updating the git history after stage 3 (once)

    git fetch v2-stage3.bundle main
    git reset FETCH_HEAD

# Stage 3a fixes

- Command timeouts are always logged (they were hidden unless "Show COM Errors" was ticked),
  and the meter error check now runs even when a command fails, so meter errors appear at once.
- Error-queue replies that aren't error codes are logged and skipped instead of ending the check.
- Fixed (original bug): with an update speed above 1 s, a button press could start a second
  communication cycle while one was running, causing "-410 Query INTERRUPTED" errors.
- New option Display Panel -> Log Serial Traffic (diagnostics): logs every command sent (>>)
  and reply (<<) with timings.

## Updating the git history after stage 3a (once)

    git fetch v2-stage3a.bundle main
    git reset FETCH_HEAD

# Stage 3b fix

- `*OPC?` is no longer used. With Null or dB enabled and no offset/reference given, the meter
  treats "take the next reading as the offset" as a pending operation, so `*OPC?` never answers
  until a reading is taken; the app's next query then caused `-410 "Query INTERRUPTED"`.
  The error-queue check after each command now also serves as the "command processed"
  confirmation (the meter answers queries strictly in order).

## Updating the git history after stage 3b (once)

    git fetch v2-stage3b.bundle main
    git reset FETCH_HEAD

# Stage 3c fix

- After `CALC:STAT ON` the meter answers no queries until it has taken a reading (Null and dB
  use that first reading as their reference). Turning math on now sends `CALC:STAT ON` followed
  immediately by `READ?`, then any offset/reference/limits, so the error check no longer waits 9 s.

## Updating the git history after stage 3c (once)

    git fetch v2-stage3c.bundle main
    git reset FETCH_HEAD
