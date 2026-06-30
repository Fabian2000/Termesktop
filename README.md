# Termesktop

A full desktop environment that runs entirely in your terminal.

Built with [TermuiX](https://github.com/Fabian2000/Termui), a .NET terminal UI framework by Fabian Schlüter. Fully usable over SSH — giving headless servers a graphical desktop without exposing anything through a hosted web interface.

> ℹ️ **About this project**
> - **Primarily developed and tested on Linux.** The codebase is cross-platform (builds for Linux, macOS, Windows) but Linux is the main target. Some features — especially the Terminal's PTY handling — rely on Unix-specific APIs and may not be fully functional on macOS or Windows.
> - **This is a small side project**, AI-assisted in development and reviewed/tested by a human. It works for everyday use, but don't expect the polish of a production-grade desktop environment — you may run into inefficiencies, rough edges, or edge cases that weren't anticipated. Issues and pull requests are welcome.

## Features

- **Window Manager** — Drag, resize, minimize, maximize, close. Z-order, cascade positioning, multi-window support.
- **Taskbar** — Pinnable app icons, running instance indicators, window cycling, clock & date.
- **Start Menu** — Searchable app grid with pinned favorites, drag-to-reorder, animated slide-up, shutdown button.
- **Desktop Icons** — Files & folders from a configurable desktop directory with right-click context menus.
- **Wallpaper** — Terminal-rendered background images using half-block Unicode for double vertical resolution.
- **Theming** — Fully customizable colors with live preview. Presets included.
- **Per-User Config** — All settings, pins, and notes saved to `~/.termesktop/`.

## Applications

| App | Description |
|-----|-------------|
| **Files** | File manager with sidebar, breadcrumb navigation, drag & drop, copy/cut/paste, archive support (.zip, .tar, .gz) |
| **Terminal** | Interactive PTY shell with xterm-256color support — full colors, cursor addressing, alt-screen for TUI apps |
| **Editor** | Text editor with File/Edit/View menus, undo/redo, open/save dialogs |
| **Markdown** | Markdown viewer/editor with live preview toggle — renders headings, bold, code blocks with syntax highlighting |
| **Image** | Image viewer with zoom, pan, and fit-to-window |
| **Video** | Video player powered by ffmpeg — play, pause, seek, time display |
| **Download** | HTTP download manager with progress, speed, ETA, and history |
| **Calc** | Calculator with expression chaining and PEMDAS operator precedence |
| **Notes** | Sticky notes with auto-save, rename, and sidebar navigation |
| **Clock** | Stopwatch with lap times + countdown timer |
| **Monitor** | System monitor — CPU, RAM, disk usage with live charts |
| **Tasks** | Process manager — list, sort, and kill processes |
| **Settings** | Color theming, clock format, wallpaper, desktop folder, shell config |

Additional tools: **Properties Viewer** (file metadata, Unix permissions), **Color Picker**, **File Dialogs** with sidebar and folder creation.

## Terminal

The Terminal app runs a real PTY with `xterm-256color` support. Fullscreen TUI apps like `htop`, `vim`, `nano`, `less`, `top` work with full colors and cursor addressing via an integrated VT100/xterm emulator.

- **Normal mode** — Type commands in the input bar, press Enter to run.
- **VT mode (fullscreen apps)** — Automatically activates when an app requests the alternate screen buffer. All keys (arrows, F1-F12, Escape, etc.) go directly to the app.
- **Interrupt running process** — Press `Ctrl+D` in the terminal to terminate the foreground command (e.g. `sleep`, `ping`). Inside fullscreen apps, use their native exit keys (`q`, `F10`, `:q`, etc.).
- **Window resize** — TUI apps receive `SIGWINCH` and reflow their layout.

> ⚠️ **Compatibility note:** The integrated terminal emulator implements a broad subset of xterm/VT100 and runs most shell commands and TUI applications. `htop` has been tested and works with full colors. Other common tools should work thanks to the emulator's VT100/xterm coverage, but full xterm compatibility is not guaranteed — some advanced features (complex mouse protocols, rare escape sequences, certain reporting queries) may behave differently or not at all. For heavy interactive use, a native terminal emulator is still recommended.

## Requirements

- **Linux** (primary target) with `script` and `setsid` utilities (standard on most distros)
- Also builds for macOS and Windows (Terminal has full PTY support on Linux only)
- A terminal with TrueColor and mouse support (most modern terminals)
- Optional: `ffmpeg` for video playback

## Build

```bash
# Debug
dotnet build

# AOT single binary (no .NET runtime needed)
dotnet publish -c Release -r linux-x64
# Output: bin/Release/net10.0/linux-x64/publish/Termesktop
```

For other platforms:

```bash
dotnet publish -c Release -r osx-arm64    # macOS Apple Silicon
dotnet publish -c Release -r osx-x64      # macOS Intel
dotnet publish -c Release -r win-x64      # Windows
```

## Run

```bash
# From published binary
./Termesktop

# Or with dotnet
dotnet run
```

Works locally or over SSH:

```bash
ssh user@server ./Termesktop
```

**Shutdown** — Click the ⏻ button in the Start Menu. `Ctrl+C` is ignored at the OS level so the desktop won't die from terminal interrupts.

## Config

All user data is stored in `~/.termesktop/`:

```
~/.termesktop/
├── settings.json    # Colors, display, paths
├── taskbar.json     # Pinned taskbar apps
├── pinned.json      # Pinned start menu apps
└── notes/           # Saved notes
```

## Tech Stack

- **C# / .NET 10** — Native AOT compiled, single binary, no runtime dependencies
- **TermuiX** — Declarative XML-based terminal UI with TrueColor, mouse input, and widget system
- **SixLabors.ImageSharp** — Image loading and scaling
- **Half-block rendering** — `▀▄█` characters for 2x vertical pixel resolution (images, video, wallpaper, clock)
- **VT100/xterm emulator** — Built-in terminal emulator with 256-color + 24-bit RGB, alt-screen buffer, scroll regions, cursor positioning — renders TUI apps like htop with proper colors and layout
- **PTY via `script`** — Cross-compatible pseudo-terminal using `setsid -w script` for session isolation

## License

MIT
