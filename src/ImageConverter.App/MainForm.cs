using System.Diagnostics;
using ImageConverter.Core;

namespace ImageConverter.App;

/// <summary>
/// Drag JPG/PNG files onto the window. Depending on the mode the result is
///  - TSP art: one continuous line (preview PNG + G-code), or
///  - Sobel:   an edge image.
/// Everything goes to Desktop\ImageConverter Output. Built in code (no designer file).
/// </summary>
public sealed class MainForm : Form
{
    private const string DropText = "Drop JPG / PNG images here";

    private readonly ImageProcessor _processor = new();
    private AppSettings _settings = AppSettings.Load();

    private readonly Label _dropLabel;
    private readonly PictureBox _inputBox;
    private readonly PictureBox _outputBox;
    private readonly Label _outputCaption;
    private readonly ComboBox _mode;
    private readonly PropertyGrid _grid;
    private readonly SplitContainer _split;
    private readonly ListBox _log;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private readonly Button _cancel;
    private readonly PointsUpDown _points;
    private Size? _lastImageSize;   // size of the last dropped image, for the automatic point count
    private readonly CheckBox _autoPoints;
    private bool _syncing;

    private CancellationTokenSource? _cts;
    private bool _busy;

    public MainForm()
    {
        Text = "ImageConverter";
        MinimumSize = new Size(900, 600);
        Size = new Size(1250, 780);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        // --- drop zone ------------------------------------------------------
        _dropLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = DropText,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            ForeColor = Color.DimGray,
            BackColor = Color.WhiteSmoke,
            BorderStyle = BorderStyle.FixedSingle,
            AllowDrop = true
        };

        // --- previews -------------------------------------------------------
        _inputBox = CreatePreviewBox();
        _outputBox = CreatePreviewBox();
        _outputCaption = new Label { AutoSize = true };

        var previews = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previews.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previews.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previews.Controls.Add(new Label { Text = "Input", AutoSize = true }, 0, 0);
        previews.Controls.Add(_outputCaption, 1, 0);
        previews.Controls.Add(_inputBox, 0, 1);
        previews.Controls.Add(_outputBox, 1, 1);

        // --- settings -------------------------------------------------------
        _grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = _settings,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false,
            HelpVisible = true
        };
        _grid.PropertyValueChanged += (_, _) =>
        {
            _settings.Save();
            SyncPointControls();   // point count may have been edited in the grid
        };

        var resetButton = new Button { Text = "Reset settings to defaults", Dock = DockStyle.Bottom, Height = 28 };
        resetButton.Click += (_, _) => ResetSettings();

        var settingsPanel = new Panel { Dock = DockStyle.Fill };
        settingsPanel.Controls.Add(_grid);
        settingsPanel.Controls.Add(resetButton);

        _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        _split.Panel1.Controls.Add(previews);
        _split.Panel2.Controls.Add(settingsPanel);

        // --- toolbar --------------------------------------------------------
        _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        _mode.Items.AddRange(["TSP art – single line + G-code", "Sobel edges"]);
        _mode.SelectedIndex = _settings.Mode == ConversionMode.Sobel ? 1 : 0;
        _mode.SelectedIndexChanged += (_, _) =>
        {
            _settings.Mode = _mode.SelectedIndex == 1 ? ConversionMode.Sobel : ConversionMode.TspArt;
            _settings.Save();
            UpdateCaptions();
            SyncPointControls();
        };

        // Point count: manual value or automatic (derived from the input image's pixel count).
        _points = new PointsUpDown
        {
            Minimum = 100,
            Maximum = 10_000_000,
            Increment = 10_000,
            ThousandsSeparator = true,
            Width = 110,
            Margin = new Padding(3, 5, 3, 3)
        };
        _points.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            _settings.Tsp.PointCount = (int)_points.Value;
            SettingsChangedFromToolbar();
        };

        _autoPoints = new CheckBox { Text = "Automatic", AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        _autoPoints.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _settings.Tsp.AutoPointCount = _autoPoints.Checked;
            SettingsChangedFromToolbar();
        };
        new ToolTip().SetToolTip(_autoPoints,
            "Derive the number of points from the input image size (pixels ÷ 'Auto: pixels per point', " +
            "clamped to min/max – adjustable in the settings panel).");

        var openFolder = new Button { Text = "Open output folder", AutoSize = true };
        openFolder.Click += (_, _) => OpenOutputFolder();

        _cancel = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        _cancel.Click += (_, _) => _cts?.Cancel();

        _progress = new ProgressBar { Width = 160, Style = ProgressBarStyle.Continuous, Margin = new Padding(3, 6, 3, 3) };
        _status = new Label { AutoSize = true, Margin = new Padding(6, 8, 3, 3), ForeColor = Color.DimGray };

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 4)
        };
        toolbar.Controls.Add(new Label { Text = "Mode:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        toolbar.Controls.Add(_mode);
        toolbar.Controls.Add(new Label { Text = "Points:", AutoSize = true, Margin = new Padding(12, 8, 3, 3) });
        toolbar.Controls.Add(_points);
        toolbar.Controls.Add(_autoPoints);
        toolbar.Controls.Add(openFolder);
        toolbar.Controls.Add(_cancel);
        toolbar.Controls.Add(_progress);
        toolbar.Controls.Add(_status);

        // --- log ------------------------------------------------------------
        _log = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        _log.DoubleClick += (_, _) => OpenSelectedLogEntry();

        // --- root layout ----------------------------------------------------
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        root.Controls.Add(_dropLabel, 0, 0);
        root.Controls.Add(_split, 0, 1);
        root.Controls.Add(toolbar, 0, 2);
        root.Controls.Add(_log, 0, 3);
        Controls.Add(root);

        // Drag & drop works on the whole window and on the drop zone.
        foreach (Control c in new Control[] { this, _dropLabel })
        {
            c.DragEnter += OnDragEnter;
            c.DragLeave += (_, _) => _dropLabel.BackColor = Color.WhiteSmoke;
            c.DragDrop += OnDragDrop;
        }

        UpdateCaptions();
        SyncPointControls();
        Log($"Output folder: {_processor.OutputDirectory}");
        Log($"Settings file: {AppSettings.FilePath}");
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Settings panel ~340 px wide on the right.
        _split.SplitterDistance = Math.Max(200, _split.Width - 340);
    }

    private static PictureBox CreatePreviewBox() => new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(40, 40, 40)
    };

    private void UpdateCaptions() =>
        _outputCaption.Text = _settings.Mode == ConversionMode.Sobel
            ? "Sobel output"
            : "Single-line preview (green = start, red = end)";

    /// <summary>Toolbar -> settings grid.</summary>
    private void SettingsChangedFromToolbar()
    {
        _settings.Save();
        _grid.Refresh();
        SyncPointControls();
    }

    /// <summary>Settings -> toolbar controls.</summary>
    private void SyncPointControls()
    {
        _syncing = true;
        try
        {
            var tsp = _settings.Tsp;
            if (!tsp.AutoPointCount)
            {
                _points.Value = ClampPoints(tsp.PointCount);
                _points.Reformat();
            }
            else if (_lastImageSize is { } size)
            {
                // display only: the count automatic mode uses for the last image (manual value is kept)
                _points.Value = ClampPoints(tsp.ResolvePointCount(size.Width, size.Height));
                _points.Reformat();
            }
            else
            {
                _points.ShowText("auto");
            }
            _autoPoints.Checked = tsp.AutoPointCount;

            bool tspMode = _settings.Mode == ConversionMode.TspArt && !_busy;
            _autoPoints.Enabled = tspMode;
            _points.Enabled = tspMode && !tsp.AutoPointCount;
        }
        finally
        {
            _syncing = false;
        }
    }

    private decimal ClampPoints(int n) => Math.Clamp(n, (int)_points.Minimum, (int)_points.Maximum);

    private void ResetSettings()
    {
        if (MessageBox.Show(this, "Reset all settings to their defaults?", "ImageConverter",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        var mode = _settings.Mode;
        _settings = new AppSettings { Mode = mode };
        _settings.Save();
        _grid.SelectedObject = _settings;
        SyncPointControls();
    }

    // ---------------------------------------------------------------------
    // Drag & drop
    // ---------------------------------------------------------------------

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!_busy && GetSupportedFiles(e).Length > 0)
        {
            e.Effect = DragDropEffects.Copy;
            _dropLabel.BackColor = Color.LightSteelBlue;
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        _dropLabel.BackColor = Color.WhiteSmoke;
        string[] files = GetSupportedFiles(e);
        if (files.Length == 0 || _busy) return;

        await ProcessFilesAsync(files);
    }

    private static string[] GetSupportedFiles(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return [];

        // Allow dropping folders too: take their jpg/png files (top level only).
        return paths
            .SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p) : new[] { p })
            .Where(ImageLoader.IsSupported)
            .ToArray();
    }

    // ---------------------------------------------------------------------
    // Processing
    // ---------------------------------------------------------------------

    private async Task ProcessFilesAsync(string[] files)
    {
        _busy = true;
        _cts = new CancellationTokenSource();
        _cancel.Enabled = true;
        _mode.Enabled = false;
        SyncPointControls();
        _dropLabel.Text = "Processing…";
        _progress.Maximum = files.Length;
        _progress.Value = 0;

        AppSettings s = _settings.Clone();   // snapshot: edits during the run don't affect it
        var progress = new Progress<string>(msg => _status.Text = msg);
        int ok = 0;

        foreach (string file in files)
        {
            if (_cts.IsCancellationRequested) break;
            try
            {
                SetPreview(_inputBox, file);
                _lastImageSize = _inputBox.Image?.Size;
                SyncPointControls();
                _status.Text = $"Processing {Path.GetFileName(file)}…";
                CancellationToken ct = _cts.Token;

                ProcessResult result = await Task.Run(() => s.Mode == ConversionMode.Sobel
                    ? _processor.Process(file, s.Sobel.Threshold, s.Sobel.Invert)
                    : _processor.ProcessTspArt(file, s.Tsp, s.GCode, progress, ct));

                SetPreview(_outputBox, result.OutputPath);

                string outputs = result.GCodePath is null
                    ? Path.GetFileName(result.OutputPath)
                    : $"{Path.GetFileName(result.OutputPath)} + {Path.GetFileName(result.GCodePath)}";
                Log($"✔ {Path.GetFileName(file)} ({result.Width}x{result.Height}) → {outputs}  " +
                    $"[{result.Duration.TotalSeconds:F1} s]" + (result.Info is null ? "" : $"  {result.Info}"),
                    result.OutputPath);
                ok++;
            }
            catch (OperationCanceledException)
            {
                Log($"⏹ {Path.GetFileName(file)}: cancelled");
            }
            catch (Exception ex)
            {
                Log($"✘ {Path.GetFileName(file)}: {ex.Message}");
            }
            _progress.Value++;
        }

        Log($"Done: {ok}/{files.Length} image(s) processed.");
        _status.Text = "";
        _dropLabel.Text = DropText;
        _cancel.Enabled = false;
        _mode.Enabled = true;
        _cts.Dispose();
        _cts = null;
        _busy = false;
        SyncPointControls();
    }

    /// <summary>Loads a preview without locking the file on disk.</summary>
    private static void SetPreview(PictureBox box, string path)
    {
        Image? old = box.Image;
        try
        {
            box.Image = ImageLoader.LoadAsBitmap(path);
        }
        catch
        {
            box.Image = null;
        }
        old?.Dispose();
    }

    // ---------------------------------------------------------------------
    // Log / helpers
    // ---------------------------------------------------------------------

    private sealed record LogEntry(string Text, string? Path)
    {
        public override string ToString() => Text;
    }

    private void Log(string message, string? path = null)
    {
        _log.Items.Add(new LogEntry($"{DateTime.Now:HH:mm:ss}  {message}", path));
        _log.TopIndex = _log.Items.Count - 1;
    }

    private void OpenSelectedLogEntry()
    {
        if (_log.SelectedItem is LogEntry { Path: { } path } && File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenOutputFolder()
    {
        Directory.CreateDirectory(_processor.OutputDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_processor.OutputDirectory}\"") { UseShellExecute = true });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        _settings.Save();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _inputBox.Image?.Dispose();
        _outputBox.Image?.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>NumericUpDown that can show a placeholder text ("auto") and be re-formatted on demand.</summary>
internal sealed class PointsUpDown : NumericUpDown
{
    public void ShowText(string text) => Text = text;

    public void Reformat() => UpdateEditText();
}
