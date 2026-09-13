# ShowTextOnly

A borderless, always-on-top window that shows one editable text box with no margin or padding. It sits on top of
everything else, like a piece of paper pinned above your other windows.

## Controls

- **Left click** selects text; type to edit it.
- **Middle button drag** moves the window. **Ctrl + middle click** closes it.
- **Bottom right corner drag** resizes the window.
- **Ctrl + S** switches between black-on-white and white-on-black.
- **Ctrl + mouse wheel** changes the font size. **Alt + mouse wheel** changes the window's opacity.
- **Ctrl + O** opens a file, **Ctrl + Shift + S** saves as a file, dragging a file onto the window opens it, and
  passing file paths as command line arguments opens them concatenated.
- **Ctrl + D** duplicates the current line, **Ctrl + Shift + K** deletes it, **Alt + Up/Down** moves it,
  **Ctrl + Shift + T** inserts a timestamp.
- **Esc** closes the window.
- **Right click** opens a menu with cut/copy/paste, read-only mode, case conversion, line sorting/cleanup
  commands, font and color pickers, word wrap, line ending, always-on-top and taskbar toggles, and a word count.

The window remembers its position, size and all these settings between runs, and keeps a tray icon so it can be
shown again after being sent to the taskbar or closed accidentally by another window's shortcut.

## Persistence

With no file given on the command line, the text is kept in `ShowTextOnly.txt` next to the executable and loaded
again on the next start. Settings are kept in `ShowTextOnly.settings.json`, and errors are appended to
`ShowTextOnly.log`, both next to the executable.

## Installation and building

Requires Windows and, for the framework-dependent build, the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
The portable build needs nothing installed.

```powershell
dotnet build -c Release
.\Publish.ps1
```

`Publish.ps1` produces both release executables in `.\publish`: `ShowTextOnly.exe` (portable, Native AOT) and
`ShowTextOnly-FrameworkDependent-RequiresNET10.exe` (single file, needs the .NET 10 Desktop Runtime).

The executables are digitally signed. To let Windows verify the signature, run `Install-Certificate.cmd` from
`SignatureTrust.zip` (attached to the release) once. The programs run without it; only the signature stays
unverified.
