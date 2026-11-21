# Window Island

Notch / Dynamic Island, a Mac-style dock and desktop widgets for Windows 10 & 11, built native with .NET 8 WPF (no Electron).

## Contents

| Folder | What |
|---|---|
| `index.html`, `assets/` | Landing page (open directly or host on GitHub Pages) |
| `app/WindowIsland/` | The Windows app source code (C# / WPF) |
| `installer/` | Inno Setup script (per-user install, no admin needed) |
| `.github/workflows/build.yml` | Builds the app on Windows, runs a smoke test, publishes the installer |

## App features (v1.0)

- **Notch / Dynamic Island**: time, music controls (any app via Windows media), notifications, prayer times, CPU/RAM/internet, mic/camera privacy dot, charging alerts
- **Dock**: smooth magnification, running-app dots, click to switch/minimize, right-click menu, drag files to pin, smart hide, optional Windows taskbar replacement
- **Widgets**: Today & Notes, Weather (Open-Meteo), Analog clock, Prayer times (offline, JAKIM method), Timed reminders (`Meeting @3pm`), System stats
- **4 layouts**: Notch + Dock, Full bar + Dock, Notch only, Dock only
- **6 themes**, autostart, update check, auto-hides in fullscreen games
- The taskbar is always restored on quit/uninstall (`--quit`, `--uninstall`)

Data lives in `%AppData%\WindowIsland` (config.json, log.txt).

## Publishing a new version

1. Bump `<Version>` in `app/WindowIsland/WindowIsland.csproj`
2. Add an entry to `CHANGELOG` in `assets/app.js`
3. Push a tag: `git tag v1.0.1 && git push origin v1.0.1`

GitHub Actions builds `WindowIsland-Setup.exe` and attaches it to a Release. The website's download button always points to
`https://github.com/akmalhakim-96/WINDOW-ISLAND/releases/latest/download/WindowIsland-Setup.exe`.

## Local build (Windows)

```
dotnet publish app/WindowIsland/WindowIsland.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```
