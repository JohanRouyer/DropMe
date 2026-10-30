# DropMe

A floating shelf for Windows: drag a file, shake the mouse, and a small window pops up to hold it
while you switch to another folder or app.

[![Latest release](https://img.shields.io/github/v/release/JohanRouyer/DropMe)](https://github.com/JohanRouyer/DropMe/releases/latest)
[![License: PolyForm Noncommercial](https://img.shields.io/badge/license-PolyForm%20Noncommercial%201.0.0-blue)](LICENSE.md)
![Platform: Windows 10/11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-lightgrey)

## Download

Get **`DropMe-Setup-<version>.exe`** from the [latest release](https://github.com/JohanRouyer/DropMe/releases/latest)
and run it. No admin rights or .NET install needed (Windows 10/11, 64-bit).

DropMe checks for new releases at startup and once a day. When one is out, a notification and an
**Update** button appear; one click downloads the installer, verifies its SHA-256 checksum against
the one GitHub publishes, installs it and restarts DropMe. Your shelves and settings are kept.
The check is a single anonymous request to the GitHub API and can be turned off from the tray menu
("Check for updates automatically").

## How it works

- **Shake the mouse** while dragging (left button held) to open a shelf next to the cursor.
- **Drop** files, folders, images from websites, text or links onto it.
- **Drag the stack** to a folder or app to move everything; **click** it to expand the list and
  drag files out one by one, open them, or remove them.
- **Click the tray icon** to open the DropMe window with your open shelves and the most recently
  closed ones, which reopen with a click. How many closed shelves are kept (3 to 50, 5 by default)
  is set with ⚙ or from the tray icon menu.

Shelves only keep paths to your files: nothing is copied or deleted until you drag it somewhere.
Images, text and links dropped from other apps are saved to `%TEMP%\DropMe`; history and settings
live in `%APPDATA%\DropMe`.

### Known limitation

The shake is detected whenever the left mouse button is held, so it can also trigger while
selecting text or moving a window. Windows offers no reliable way to know that a file drag is in
progress.

## Building from source

Requires the .NET 8 SDK.

```
dotnet build -c Release
```

To build the installer you also need Inno Setup 6 (`winget install JRSoftware.InnoSetup`):

```
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

This produces `dist\DropMe-Setup-<version>.exe` (self-contained, no admin rights needed). The
version comes from `<Version>` in `DropMe.csproj`.

## License

DropMe is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE.md).

- You may use, modify and share it for any **noncommercial** purpose.
- When you share it, or anything based on it, you must **credit the author** by including the
  license and this notice:

  > Required Notice: Copyright (c) 2026 JohanRouyer (https://github.com/JohanRouyer/DropMe)

- **Commercial use is not allowed.** For a commercial license, open an issue to get in touch.
