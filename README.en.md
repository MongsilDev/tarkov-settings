# tarkov-settings

[![한국어](https://img.shields.io/badge/README-한국어-555555?style=flat-square)](README.md)
[![Hits](https://hits.sh/github.com/MongsilDev/tarkov-settings.svg?style=flat-square&label=hits&color=8c8c8c&labelColor=555555)](https://hits.sh)

![screenshot](./1.png)

## [->**DOWNLOAD Latest**<-](https://github.com/MongsilDev/tarkov-settings/releases/latest)

Changes display colors only while Escape from Tarkov or Arena is focused.
Fork of [incheon-kim/tarkov-settings](https://github.com/incheon-kim/tarkov-settings) with additional features and fixes.

## Features
- Brightness, Contrast, Gamma and Digital Vibrance applied only while the game window is focused, so no flash when alt-tabbing
- Escape from Tarkov and Arena, with a checkbox for Arena
- Three hotkeys: toggle gamma between two values, toggle game volume between two values, kill the game. Active only while the game is focused
- Follows the game window to whichever monitor it is on
- Start with Windows, start minimized to the tray

## How to use
1. Download the exe, right-click > Properties > Unblock, then run
2. Set colors with the sliders. Recommended applies the suggested values, Default the Windows values, double-clicking a label resets that one
3. Check Apply to Arena to use the same colors in Arena
4. In Hotkeys, set the two values and a key. Click the Key box and press a key, Esc cancels, Backspace clears. Defaults: Gamma PageUp, Game volume PageDown. Kill game needs Ctrl/Alt/Shift and asks once when you bind it
5. Check Start with Windows and Start minimized if you want them

Settings file: `%LOCALAPPDATA%\tarkov-settings\settings.json`, saved when the app closes.

## Notes
1. The screen may blink once or twice when the game window activates
2. Whether BSG objects to this tool is unknown
3. Borderless mode only
4. GPU: NVIDIA fully supported, AMD without saturation, Intel not supported
5. Unsigned build, so allow it or add an exclusion if Defender or SmartScreen complains
