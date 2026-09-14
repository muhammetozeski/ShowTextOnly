using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShowTextOnly;

/// <summary>
/// A borderless window that stays above every other window and shows one editable text box with no margin or
/// padding. Ctrl+S switches between black-on-white and white-on-black, Ctrl with the mouse wheel changes the font
/// size, a left click selects text, dragging with the middle button moves the window, dragging the box in the
/// bottom right corner resizes it, and a right click opens editing commands and settings.
/// </summary>
sealed partial class TextWindow : Form
{
    #region Settings
    const string Title = "Text";
    const int ResizeBoxSize = 7;
    const int MinimumWidth = 60;
    const int MinimumHeight = 40;
    const float MinimumFontSize = 6f;
    const float MaximumFontSize = 96f;
    #endregion

    readonly TextBox Editor = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        BorderStyle = BorderStyle.None,
        WordWrap = true,
        AcceptsTab = true,
        ScrollBars = ScrollBars.None,
        Margin = Padding.Empty,
    };
    readonly Panel ResizeBox = new() { BackColor = Palette.ResizeBox, Size = new(ResizeBoxSize, ResizeBoxSize) };
    readonly Settings Settings;
    readonly NotifyIcon TrayIcon;
    readonly string[] FilePaths;
    string? FilePath;

    #region drag window variables
    bool IsDragging;
    Point LastCursorPosition;
    #endregion

    const int EM_SETMARGINS = 0xD3;
    const int EM_SETRECT = 0xB3;
    const int EC_LEFTMARGIN = 0x1;
    const int EC_RIGHTMARGIN = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessageRect(IntPtr handle, int message, IntPtr wParam, ref Rect lParam);

    /// <summary>
    /// Creates the window, loading the text of <paramref name="filePath"/> when it is given and exists, otherwise
    /// the note file kept next to the executable.
    /// </summary>
    /// <param name="filePaths">Paths of text files to open, concatenated in order. When empty, the program's own note file is used.</param>
    public TextWindow(string[] filePaths)
    {
        FilePaths = filePaths;
        FilePath = filePaths is [var singlePath] ? singlePath : null;
        Settings = Settings.Load();

        // Every window style is set before the handle is created. Changing Text between empty and non-empty while
        // ControlBox is false, or changing ShowInTaskbar, makes WinForms destroy and recreate the window.
        Text = Title;
        ControlBox = false;
        ShowIcon = false;
        TopMost = Settings.TopMost;
        ShowInTaskbar = Settings.ShowInTaskbar;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Padding = Padding.Empty;
        AllowDrop = true;
        Opacity = Settings.Opacity;
        Size = new(Settings.WindowWidth, Settings.WindowHeight);
        Location = Settings.WindowX >= 0 ? new(Settings.WindowX, Settings.WindowY) : DefaultLocation();

        ApplyTheme();
        ApplyFont();
        Editor.Text = ReadInitialText();
        Editor.ContextMenuStrip = BuildContextMenu();
        TrayIcon = CreateTrayIcon();

        // Controls earlier in the collection are drawn on top, so the resize box stays above the text box.
        Controls.AddRange([ResizeBox, Editor]);

        Control[] dragTargets = [this, Editor, ResizeBox];
        foreach (Control target in dragTargets)
        {
            target.MouseDown += HandleMouseDown;
            target.MouseMove += HandleMouseMove;
            target.MouseUp += HandleMouseUp;
        }
        Editor.MouseWheel += HandleMouseWheel;
        Editor.KeyDown += HandleKeyDown;
        DragEnter += HandleDragEnter;
        DragDrop += HandleDragDrop;
    }

    static Point DefaultLocation()
    {
        Rectangle screen = Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;
        return new(Math.Max(screen.Left, screen.Right - 420), screen.Top + screen.Height / 2 - 130);
    }

    string TargetFilePath() => FilePath ?? Path.Combine(AppContext.BaseDirectory, "ShowTextOnly.txt");

    string ReadInitialText()
    {
        try
        {
            if (FilePaths.Length > 0)
                return string.Join(Environment.NewLine, FilePaths.Where(File.Exists).Select(File.ReadAllText));
            return File.Exists(TargetFilePath()) ? File.ReadAllText(TargetFilePath()) : string.Empty;
        }
        catch (Exception exception)
        {
            ErrorLog.Write("Failed to read the text file", exception);
            return string.Empty;
        }
    }

    void ApplyTheme()
    {
        Color background = Settings.CustomBackground is int backArgb ? Color.FromArgb(backArgb)
            : Settings.IsDarkTheme ? Palette.DarkBackground : Palette.LightBackground;
        Color foreground = Settings.CustomForeground is int foreArgb ? Color.FromArgb(foreArgb)
            : Settings.IsDarkTheme ? Palette.LightBackground : Palette.DarkBackground;
        BackColor = background;
        Editor.BackColor = background;
        Editor.ForeColor = foreground;
    }

    string PrepareForSave(string text) => Settings.UseLfLineEndings ? text.Replace("\r\n", "\n") : text;

    void ApplyFont()
    {
        FontStyle style = FontStyle.Regular;
        if (Settings.FontBold) style |= FontStyle.Bold;
        if (Settings.FontItalic) style |= FontStyle.Italic;
        Editor.Font = new(Settings.FontFamily, Settings.FontSize, style);
    }

    NotifyIcon CreateTrayIcon()
    {
        ContextMenuStrip trayMenu = new();
        trayMenu.Items.Add("New Note", null, (_, _) => Process.Start(Application.ExecutablePath));
        trayMenu.Items.Add("Exit", null, (_, _) => Application.Exit());
        NotifyIcon trayIcon = new() { Text = Title, Icon = SystemIcons.Application, ContextMenuStrip = trayMenu, Visible = true };
        trayIcon.MouseClick += (_, mouse) => { if (mouse.Button == MouseButtons.Left) Visible = !Visible; };
        return trayIcon;
    }

    ContextMenuStrip BuildContextMenu()
    {
        ContextMenuStrip menu = new();
        menu.Items.Add("Undo", null, (_, _) => Editor.Undo());
        menu.Items.Add("Cut", null, (_, _) => Editor.Cut());
        menu.Items.Add("Copy", null, (_, _) => Editor.Copy());
        menu.Items.Add("Paste", null, (_, _) => Editor.Paste());
        menu.Items.Add("Select All", null, (_, _) => Editor.SelectAll());
        menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem readOnlyItem = new("Read Only", null, (_, _) => Editor.ReadOnly = !Editor.ReadOnly);
        menu.Items.Add(readOnlyItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("UPPERCASE", null, (_, _) => ConvertCase(upper: true));
        menu.Items.Add("lowercase", null, (_, _) => ConvertCase(upper: false));
        menu.Items.Add("Sort Lines", null, (_, _) => SortLines());
        menu.Items.Add("Remove Duplicate Lines", null, (_, _) => RemoveDuplicateLines());
        menu.Items.Add("Remove Blank Lines", null, (_, _) => RemoveBlankLines());
        menu.Items.Add("Trim Lines", null, (_, _) => TrimLines());
        menu.Items.Add("Insert Timestamp", null, (_, _) => InsertTimestamp());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open...", null, (_, _) => OpenFile());
        menu.Items.Add("Save As...", null, (_, _) => SaveFileAs());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Word Count...", null, (_, _) => ShowWordCount());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Font...", null, (_, _) => PickFont());
        menu.Items.Add("Text Color...", null, (_, _) => PickColor(isForeground: true));
        menu.Items.Add("Background Color...", null, (_, _) => PickColor(isForeground: false));
        ToolStripMenuItem wordWrapItem = new("Word Wrap", null, (_, _) => Editor.WordWrap = !Editor.WordWrap);
        menu.Items.Add(wordWrapItem);
        ToolStripMenuItem lineEndingItem = new("Use LF Line Endings", null, (_, _) => Settings.UseLfLineEndings = !Settings.UseLfLineEndings);
        menu.Items.Add(lineEndingItem);
        ToolStripMenuItem topMostItem = new("Always on Top", null, (_, _) => ToggleTopMost());
        ToolStripMenuItem taskbarItem = new("Show in Taskbar", null, (_, _) => ToggleTaskbar());
        menu.Items.Add(topMostItem);
        menu.Items.Add(taskbarItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Close", null, (_, _) => Close());
        menu.Opening += (_, _) =>
        {
            readOnlyItem.Checked = Editor.ReadOnly;
            wordWrapItem.Checked = Editor.WordWrap;
            lineEndingItem.Checked = Settings.UseLfLineEndings;
            topMostItem.Checked = TopMost;
            taskbarItem.Checked = ShowInTaskbar;
        };
        return menu;
    }

    #region Editing commands
    void ConvertCase(bool upper) => Editor.SelectedText = upper ? Editor.SelectedText.ToUpperInvariant() : Editor.SelectedText.ToLowerInvariant();

    void SortLines() => Editor.Lines = Editor.Lines.OrderBy(line => line, StringComparer.OrdinalIgnoreCase).ToArray();

    void RemoveDuplicateLines()
    {
        HashSet<string> seen = new();
        Editor.Lines = Editor.Lines.Where(seen.Add).ToArray();
    }

    void RemoveBlankLines() => Editor.Lines = Editor.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

    void TrimLines() => Editor.Lines = Editor.Lines.Select(line => line.Trim()).ToArray();

    void InsertTimestamp() => Editor.SelectedText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    int CurrentLineIndex() => Editor.GetLineFromCharIndex(Editor.SelectionStart);

    void DeleteCurrentLine()
    {
        List<string> lines = Editor.Lines.ToList();
        int lineIndex = CurrentLineIndex();
        if (lineIndex >= lines.Count) return;
        lines.RemoveAt(lineIndex);
        Editor.Lines = lines.ToArray();
    }

    void DuplicateCurrentLine()
    {
        List<string> lines = Editor.Lines.ToList();
        int lineIndex = CurrentLineIndex();
        if (lineIndex >= lines.Count) return;
        lines.Insert(lineIndex + 1, lines[lineIndex]);
        Editor.Lines = lines.ToArray();
    }

    void MoveLine(int direction)
    {
        List<string> lines = Editor.Lines.ToList();
        int lineIndex = CurrentLineIndex();
        int targetIndex = lineIndex + direction;
        if (targetIndex < 0 || targetIndex >= lines.Count) return;
        (lines[lineIndex], lines[targetIndex]) = (lines[targetIndex], lines[lineIndex]);
        Editor.Lines = lines.ToArray();
    }

    void OpenFile()
    {
        using OpenFileDialog dialog = new();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Editor.Text = File.ReadAllText(dialog.FileName);
            FilePath = dialog.FileName;
        }
        catch (Exception exception)
        {
            ErrorLog.Write("Failed to open the file", exception);
        }
    }

    void SaveFileAs()
    {
        using SaveFileDialog dialog = new() { FileName = FilePath is null ? "Untitled.txt" : Path.GetFileName(FilePath) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, PrepareForSave(Editor.Text));
            FilePath = dialog.FileName;
        }
        catch (Exception exception)
        {
            ErrorLog.Write("Failed to save the file", exception);
        }
    }

    void ShowWordCount()
    {
        int words = Editor.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        MessageBox.Show(this, $"Words: {words}{Environment.NewLine}Characters: {Editor.Text.Length}", "Word Count");
    }
    #endregion

    void PickFont()
    {
        using FontDialog dialog = new() { Font = Editor.Font, ShowColor = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Settings.FontFamily = dialog.Font.FontFamily.Name;
        Settings.FontSize = dialog.Font.Size;
        Settings.FontBold = dialog.Font.Bold;
        Settings.FontItalic = dialog.Font.Italic;
        ApplyFont();
    }

    void PickColor(bool isForeground)
    {
        using ColorDialog dialog = new() { Color = isForeground ? Editor.ForeColor : Editor.BackColor };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (isForeground) Settings.CustomForeground = dialog.Color.ToArgb();
        else Settings.CustomBackground = dialog.Color.ToArgb();
        ApplyTheme();
    }

    void ToggleTopMost() => TopMost = Settings.TopMost = !TopMost;

    void ToggleTaskbar() => ShowInTaskbar = Settings.ShowInTaskbar = !ShowInTaskbar;

    void HandleKeyDown(object? sender, KeyEventArgs key)
    {
        if (key.KeyCode == Keys.Escape)
        {
            Close();
        }
        else if (key.Control && key.Shift && key.KeyCode == Keys.S)
        {
            SaveFileAs();
            key.SuppressKeyPress = true;
        }
        else if (key.Control && key.KeyCode == Keys.S)
        {
            Settings.IsDarkTheme = !Settings.IsDarkTheme;
            ApplyTheme();
            key.SuppressKeyPress = true;
        }
        else if (key.Control && key.KeyCode == Keys.O)
        {
            OpenFile();
            key.SuppressKeyPress = true;
        }
        else if (key.Control && key.Shift && key.KeyCode == Keys.K)
        {
            DeleteCurrentLine();
            key.SuppressKeyPress = true;
        }
        else if (key.Control && key.Shift && key.KeyCode == Keys.T)
        {
            InsertTimestamp();
            key.SuppressKeyPress = true;
        }
        else if (key.Control && key.KeyCode == Keys.T)
        {
            ToggleTopMost();
            key.SuppressKeyPress = true;
        }
        else if (key.Control && key.KeyCode == Keys.D)
        {
            DuplicateCurrentLine();
            key.SuppressKeyPress = true;
        }
        else if (key.Alt && key.KeyCode == Keys.Up)
        {
            MoveLine(-1);
            key.SuppressKeyPress = true;
        }
        else if (key.Alt && key.KeyCode == Keys.Down)
        {
            MoveLine(1);
            key.SuppressKeyPress = true;
        }
    }

    void HandleMouseWheel(object? sender, MouseEventArgs mouse)
    {
        if (ModifierKeys.HasFlag(Keys.Alt))
        {
            Settings.Opacity = Opacity = Math.Clamp(Opacity + Math.Sign(mouse.Delta) * 0.05, 0.2, 1.0);
        }
        else if (ModifierKeys.HasFlag(Keys.Control))
        {
            Settings.FontSize = Math.Clamp(Settings.FontSize + Math.Sign(mouse.Delta), MinimumFontSize, MaximumFontSize);
            ApplyFont();
        }
    }

    void HandleDragEnter(object? sender, DragEventArgs drag) =>
        drag.Effect = drag.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    void HandleDragDrop(object? sender, DragEventArgs drag)
    {
        if (drag.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        try
        {
            Editor.Text = string.Join(Environment.NewLine, paths.Where(File.Exists).Select(File.ReadAllText));
            FilePath = paths is [var singlePath] ? singlePath : null;
        }
        catch (Exception exception)
        {
            ErrorLog.Write("Failed to open the dropped file", exception);
        }
    }

    #region Mouse
    bool IsCursorOverResizeBox() => ResizeBox.ClientRectangle.Contains(ResizeBox.PointToClient(MousePosition));

    void HandleMouseDown(object? sender, MouseEventArgs mouse)
    {
        if (mouse.Button != MouseButtons.Middle) return;
        if (ModifierKeys.HasFlag(Keys.Control))
        {
            Close();
            return;
        }
        IsDragging = true;
        LastCursorPosition = MousePosition;
    }

    void HandleMouseUp(object? sender, MouseEventArgs mouse)
    {
        if (mouse.Button == MouseButtons.Middle) IsDragging = false;
    }

    /// <summary>
    /// Moves the window while the middle button drags it, resizes it while the resize box is being dragged with the
    /// left button, and otherwise shows the resize cursor over the resize box.
    /// </summary>
    void HandleMouseMove(object? sender, MouseEventArgs mouse)
    {
        if (IsDragging && LastCursorPosition != MousePosition)
        {
            Point cursorPosition = MousePosition;
            Location += new Size(cursorPosition.X - LastCursorPosition.X, cursorPosition.Y - LastCursorPosition.Y);
            LastCursorPosition = cursorPosition;
        }
        else if (Cursor == Cursors.SizeNWSE && mouse.Button == MouseButtons.Left)
            ResizeToCursor();
        else
            Cursor = IsCursorOverResizeBox() ? Cursors.SizeNWSE : Cursors.Default;
    }

    void ResizeToCursor()
    {
        Point cursor = PointToClient(MousePosition);
        Size = new(Math.Max(MinimumWidth, cursor.X + ResizeBoxSize), Math.Max(MinimumHeight, cursor.Y + ResizeBoxSize));
    }
    #endregion

    /// <summary>
    /// Removes the text box's built-in inset on every side, which .NET properties cannot reach: EM_SETMARGINS
    /// clears the left/right inset, EM_SETRECT clears the top/bottom one.
    /// </summary>
    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        RemoveEditorInset();
    }

    void RemoveEditorInset()
    {
        if (!Editor.IsHandleCreated) return;
        SendMessage(Editor.Handle, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN), IntPtr.Zero);
        Rect rect = new() { Left = 0, Top = 0, Right = Editor.ClientSize.Width, Bottom = Editor.ClientSize.Height };
        SendMessageRect(Editor.Handle, EM_SETRECT, IntPtr.Zero, ref rect);
    }

    /// <summary>
    /// Keeps the resize box in the bottom right corner and the text box's format rectangle full-sized whenever the
    /// window size changes.
    /// </summary>
    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        ResizeBox.Location = new(ClientSize.Width - ResizeBoxSize, ClientSize.Height - ResizeBoxSize);
        RemoveEditorInset();
    }

    /// <summary>
    /// Removes the notification area icon when the window closes.
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        TrayIcon.Dispose();
        base.OnFormClosed(eventArgs);
    }

    /// <summary>
    /// Saves the window position, the settings and the text before the window closes.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        Settings.WindowX = Location.X;
        Settings.WindowY = Location.Y;
        Settings.WindowWidth = Size.Width;
        Settings.WindowHeight = Size.Height;
        try
        {
            File.WriteAllText(TargetFilePath(), PrepareForSave(Editor.Text));
        }
        catch (Exception exception)
        {
            ErrorLog.Write("Failed to save the text file", exception);
        }
        Settings.Save();
        base.OnFormClosing(eventArgs);
    }
}
