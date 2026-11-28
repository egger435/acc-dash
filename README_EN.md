# ACC Telemetry Dashboard

<p align="right"><a href="README.md">中文</a> | <b>English</b></p>

> Assetto Corsa Competizione telemetry panel — live data, lap recording, replay and track maps.

<p align="center"><img src="img/p1.png" width="880"></p>

## Features

- **Live data**: speed, rpm, gear, steering, pedals, g-forces, lap times, position, fuel, tyre temperatures and pressures, track and weather
- **Charts**: throttle/brake and steering/FFB scrolling traces with a configurable time window
- **Instruments**: steering dial, pedal bars, G-G disc, large gear and rpm readouts, session panel
- **Track map**: the outline is drawn while you drive and saved, then loaded from then on; your car is shown on it live
- **Lap video**: recording starts at the start line and ends at the line; the clip is embedded in the lap file and replay follows the charts
- **Lap list**: view, export, import, delete
- **Delta**: compare against any lap of the same track, with a full-lap delta curve and the current gap
- **Replay controls**: play/pause, hold for 8x, scrub, time window
- **Status lamps**: shared memory, physics/graphics pages, session, source, recording state

## Quick start

1. Run `AccDash.exe`
2. Start ACC and drive onto the track; the top lamps turn green once connected

Video needs `ffmpeg` (put it under `ffmpeg\bin\`; the release package already contains it).

## Screenshots

<p align="center"><img src="img/p2.png" width="380"></p>

<p align="center"><img src="img/p3.png" width="880"></p>

## Keyboard

| Key | Action |
| --- | --- |
| `F2` | Freeze / resume data |
| `F3` | Clear the charts |
| `F4` | Save the configuration |
| `F5` | Open the lap list |
| `F6` | Leave replay, back to live |
| `Space` | Replay play / pause |
| `F11` | Toggle full screen |

## Notes

- A new track needs two laps: recording starts after the out-lap, and the map is saved when the next lap crosses the line
- A `.accdash` lap file carries both data and video, so it can be shared as is
