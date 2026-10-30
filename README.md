# DropMe

A floating shelf for Windows: drag a file, shake the mouse, and a small window pops up to hold it
while you switch to another folder or app.

## How it works

- **Shake the mouse** while dragging (left button held) to open a shelf next to the cursor.
- **Drop** files, folders, images from websites, text or links onto it.
- **Drag the stack** to a folder or app to move everything; **click** it to expand the list and
  drag files out one by one.
- **Click the tray icon** to open the DropMe window with your open shelves and the most recently
  closed ones, which reopen with a click. How many closed shelves are kept (3 to 50, 5 by default)
  is set with ⚙ or from the tray icon menu.

Shelves only keep paths to your files. Images, text and links dropped from other apps are saved to
`%TEMP%\DropMe`; history and settings live in `%APPDATA%\DropMe`.

## Development

Requires the .NET 8 SDK.

```
dotnet build -c Release
```

## Installer

Requires the .NET 8 SDK and Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Produces `dist\DropMe-Setup-<version>.exe` (self-contained, no admin rights needed). The version
comes from `<Version>` in `DropMe.csproj`.
