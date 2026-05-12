using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text; // Required for StringBuilder
using System.Runtime.InteropServices; // Required for Win32 API
using AForge.Video;
using AForge.Video.DirectShow;

namespace CringometrMDSPC
{
    public partial class Form1 : Form
    {
        private readonly AppOptions _options;
        private readonly CringePaths _paths;
        private readonly ReferenceDatabase _database;
        private readonly CringeAnalyzer _analyzer;
        private readonly List<Button> _actionButtons = new List<Button>();

        // Kolejka plików dla kreatora nowej bazy
        private readonly List<string> _stagedGood = new List<string>();
        private readonly List<string> _stagedBad = new List<string>();
        private readonly List<string> _stagedCritical = new List<string>();

        private Label _bdpmLabel;
        private Label _verdictLabel;
        private Label _rootLabel;
        private ProgressBar _progress;
        private PictureBox _preview;
        private LiveChartControl _chart;
        private DataGridView _history;
        private ComboBox _languageSelector; // New: Language selection dropdown
        private Button _infoButton;         // New: Info button
        private TextBox _logBox;
        private TextBox _urlBox;
        private SplitContainer _mainSplit;
        private TabControl _mainTabControl;

        private FilterInfoCollection _videoDevices;
        private VideoCaptureDevice _videoSource;
        private Bitmap _lastCameraFrame;

        // Win32 constants and P/Invoke for Placeholder support in .NET Framework
        private const int EM_SETCUEBANNER = 0x1501;
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern Int32 SendMessage(IntPtr hWnd, int msg, int wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

        private void SetPlaceholder(TextBox textBox, string placeholder)
        {
            SendMessage(textBox.Handle, EM_SETCUEBANNER, 0, placeholder);
        }

        private bool _busy;
        private string _currentFilePath;

        public Form1() : this(new AppOptions())
        {
        }

        public Form1(AppOptions options)
        {
            _options = options ?? new AppOptions();
            _paths = new CringePaths(CringePaths.DetectRoot());
            ImageFeatureExtractor.Configure(_paths, Log);
            _database = new ReferenceDatabase(_paths);
            _analyzer = new CringeAnalyzer(_paths, _database, _options, Log);

            InitializeComponent();
            BuildUi(); // UI zostanie zbudowane z domyślnym językiem (polskim)
            _paths.EnsureDirectories();
            Log(LocalizationManager.GetString("LogClipModelPath", _paths.ClipVisionModelPath));

            Shown += async (sender, args) => 
            {
                SetBusy(true, true);
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                // Automatyczne ładowanie wszystkich plików bazy .ced znalezionych obok programu
                var cedFiles = Directory.GetFiles(baseDir, "*.ced");
                Log($"[BOOT] Znaleziono paczek: {cedFiles.Length}");
                foreach (var cedFile in cedFiles)
                {
                    try
                    {
                        Log(LocalizationManager.GetString("LogCedAutoLoading") + ": " + Path.GetFileName(cedFile));
                        await Task.Run(() => CedManager.Import(_paths, cedFile, false));
                    }
                    catch (Exception ex)
                    {
                        Log(LocalizationManager.GetString("LogCedError", ex.Message));
                    }
                }
                await RebuildDatabaseAsync();
                await CheckForUpdatesAsync();
            };
            FormClosing += Form1_FormClosing;
        }

        private void BuildUi()
        {
            Controls.Clear();
            Text = LocalizationManager.GetString("AppTitle");
            BackColor = Color.FromArgb(45, 45, 48); // Szary (Visual Studio style)
            ForeColor = Color.Gainsboro;
            MinimumSize = new Size(1120, 760);
            WindowState = FormWindowState.Maximized;

            AllowDrop = true;
            DragEnter += Form1_DragEnter;
            DragDrop += Form1_DragDrop;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 315,
                BackColor = Color.FromArgb(45, 45, 48),
                FixedPanel = FixedPanel.Panel1
            };
            _mainSplit = split;
            root.Controls.Add(split, 0, 1);

            split.Panel2.Controls.Add(BuildMainArea());

            // Inicjalizacja języka na samym końcu, gdy wszystkie kontenery są gotowe.
            // Ustawienie SelectedIndex wywoła zdarzenie SelectedIndexChanged, 
            // które automatycznie uruchomi RefreshUIStrings().
            _languageSelector.SelectedIndex = 0;
        }

        private Control BuildHeader()
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30), // Ciemniejszy szary
                ColumnCount = 4,
                Padding = new Padding(18, 10, 18, 10)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

            var titlePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

            var title = new Label
            {
                Text = LocalizationManager.GetString("AppTitle"),
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 22, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 200),
                TextAlign = ContentAlignment.BottomLeft
            };
            titlePanel.Controls.Add(title, 0, 0);

            _rootLabel = new Label
            {
                Text = LocalizationManager.GetString("DataPath") + _paths.Root,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(130, 130, 130),
                TextAlign = ContentAlignment.TopLeft
            };
            titlePanel.Controls.Add(_rootLabel, 0, 1);
            header.Controls.Add(titlePanel, 0, 0);

            _bdpmLabel = new Label
            {
                Text = "0", // BDPM is always a number
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 32, FontStyle.Bold),
                ForeColor = CringeRules.GetAcc(0).Color,
                TextAlign = ContentAlignment.MiddleCenter
            };
            header.Controls.Add(_bdpmLabel, 1, 0);

            _verdictLabel = new Label
            {
                Text = CringeRules.GetAcc(0).Label, // Verdict label will be updated by RefreshUIStrings
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = CringeRules.GetAcc(0).Color,
                TextAlign = ContentAlignment.MiddleLeft
            };
            header.Controls.Add(_verdictLabel, 2, 0);

            // Create a new TableLayoutPanel to hold the language selector and progress bar
            var rightPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(0)
            };
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); // For language selector
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); // For progress bar

            _languageSelector = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList, // Make it a dropdown list, not editable
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.Gainsboro,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9),
                Margin = new Padding(0, 0, 0, 2) // Small margin at the bottom
            };
            _languageSelector.Items.AddRange(new object[] { "Polski (pl-PL)", "English (en-US)" }); // Add language options with culture codes
            _languageSelector.SelectedIndexChanged += LanguageSelector_SelectedIndexChanged; // Event handler for language change
            rightPanel.Controls.Add(_languageSelector, 0, 0);

            _progress = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Style = ProgressBarStyle.Blocks,
                Maximum = 100,
                Margin = new Padding(0, 2, 0, 0) // Small margin at the top
            };
            rightPanel.Controls.Add(_progress, 0, 1);

            header.Controls.Add(rightPanel, 3, 0); // Add the new panel to column 3 of the header

            return header;
        }

        private Control BuildSidebar()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(16),
                BackColor = Color.FromArgb(37, 37, 38)
            };

            panel.Controls.Add(Section(LocalizationManager.GetString("SectionScanning")));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonToggleCamera"), (s, e) => ToggleCamera(), Color.FromArgb(48, 120, 180)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonScanCamera"), async (s, e) => await ScanCameraAsync(), Color.FromArgb(44, 140, 100)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonRebuildDatabase"), async (s, e) => await RebuildDatabaseAsync(), Color.FromArgb(122, 78, 194)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonPickFiles"), async (s, e) => await PickFilesAsync(), Color.FromArgb(48, 120, 180)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonScanScanFolder"), async (s, e) => await ScanFolderAsync(), Color.FromArgb(48, 120, 180)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonOpenScanFolder"), (s, e) => OpenFolder(_paths.ScanFolder), Color.FromArgb(58, 58, 58)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonEditTemplate"), (s, e) => EditTemplate(), Color.FromArgb(180, 120, 48)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonOpenReportsFolder"), (s, e) => OpenFolder(_paths.ReportsFolder), Color.FromArgb(58, 58, 58)));

            panel.Controls.Add(Section(LocalizationManager.GetString("SectionURL")));
            _urlBox = new TextBox
            {
                Width = 265,
                Height = 28,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9)
            };
            SetPlaceholder(_urlBox, LocalizationManager.GetString("PlaceholderURL"));
            panel.Controls.Add(_urlBox);
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonDownloadURL"), async (s, e) => await DownloadUrlAsync(), Color.FromArgb(44, 140, 100)));

            panel.Controls.Add(Section(LocalizationManager.GetString("SectionActiveLearning")));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonRateGood"), async (s, e) => await RateCurrentAsync("DOBRE"), Color.FromArgb(50, 150, 75)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonRateBad"), async (s, e) => await RateCurrentAsync("ZLE"), Color.FromArgb(215, 120, 30)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonRateCritical"), async (s, e) => await RateCurrentAsync("KRYTYCZNE"), Color.FromArgb(190, 50, 80)));

            panel.Controls.Add(Section(LocalizationManager.GetString("SectionCed")));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonExportCed"), async (s, e) => await ExportCedAsync(), Color.FromArgb(70, 70, 70)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonLoadCed"), async (s, e) => await LoadCedAsync(), Color.FromArgb(70, 70, 70)));

             panel.Controls.Add(Section(LocalizationManager.GetString("SectionCedCreator")));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonStageGood"), (s, e) => StageFiles(_stagedGood), Color.FromArgb(44, 140, 100)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonStageBad"), (s, e) => StageFiles(_stagedBad), Color.FromArgb(180, 120, 48)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonStageCritical"), (s, e) => StageFiles(_stagedCritical), Color.FromArgb(190, 50, 80)));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonBuildCed"), async (s, e) => await BuildAndLoadStagedCedAsync(), Color.FromArgb(122, 78, 194)));

            panel.Controls.Add(Section(LocalizationManager.GetString("SectionExport")));
            panel.Controls.Add(ActionButton(LocalizationManager.GetString("ButtonExportZip"), async (s, e) => await ExportZipAsync(), Color.FromArgb(122, 78, 194)));

            panel.Controls.Add(Section(LocalizationManager.GetString("SectionInfo"))); // New section for the info button
            _infoButton = ActionButton(LocalizationManager.GetString("ButtonAbout"), InfoButton_Click, Color.FromArgb(58, 58, 58)); // Info button
            panel.Controls.Add(_infoButton);

            var modules = new Label
            {
                Width = 265,
                Height = 82,
                ForeColor = Color.FromArgb(130, 130, 130),
                Font = new Font("Segoe UI", 8),
                Text = LocalizationManager.GetString("Modules",
                       OnOff(_options.EnableImages),
                       OnOff(_options.EnableVideo),
                       OnOff(_options.EnableAudio),
                       OnOff(_options.Enable3D),
                       OnOff(_options.EnableReports))
            };
            panel.Controls.Add(modules);

            return panel;
        }

        private Control BuildMainArea()
        {
            var vertical = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 420,
                BackColor = BackColor
            };

            var top = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 520,
                BackColor = BackColor
            };

            _preview = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 10, 10),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            top.Panel1.Controls.Add(_preview);

            _chart = new LiveChartControl
            {
                Dock = DockStyle.Fill
            };
            top.Panel2.Controls.Add(_chart);
            vertical.Panel1.Controls.Add(top);

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9)
            };
            _mainTabControl = tabs;

            var historyTab = new TabPage(LocalizationManager.GetString("TabHistory"));
            historyTab.BackColor = Color.FromArgb(45, 45, 48);
            _history = CreateHistoryGrid();
            historyTab.Controls.Add(_history);

            var logTab = new TabPage(LocalizationManager.GetString("TabLog"));
            logTab.BackColor = Color.FromArgb(45, 45, 48);
            _logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(8, 8, 8),
                ForeColor = Color.FromArgb(210, 210, 210),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9)
            };
            logTab.Controls.Add(_logBox);

            tabs.TabPages.Add(historyTab);
            tabs.TabPages.Add(logTab);
            vertical.Panel2.Controls.Add(tabs);

            return vertical;
        }

        private DataGridView CreateHistoryGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.FromArgb(45, 45, 48),
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 30, 30);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Gainsboro;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            grid.DefaultCellStyle.BackColor = Color.FromArgb(37, 37, 38);
            grid.DefaultCellStyle.ForeColor = Color.Gainsboro;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(55, 55, 75);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.GridColor = Color.FromArgb(45, 45, 45);
            grid.Columns.Add("Plik", LocalizationManager.GetString("GridColumnFile"));
            grid.Columns.Add("Typ", LocalizationManager.GetString("GridColumnType"));
            grid.Columns.Add("BDPM", LocalizationManager.GetString("GridColumnBDPM"));
            grid.Columns.Add("Werdykt", LocalizationManager.GetString("GridColumnVerdict"));
            grid.Columns.Add("Szczegoly", LocalizationManager.GetString("GridColumnDetails"));
            grid.Columns["Szczegoly"].FillWeight = 180; // This column name needs to match the one used in AddFinalResult
            return grid;
        }

        private Label Section(string text)
        {
            return new Label
            {
                Text = text,
                Width = 265,
                Height = 28,
                Margin = new Padding(0, 12, 0, 2),
                ForeColor = Color.FromArgb(160, 160, 160),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.BottomLeft
            };
        }

        private Button ActionButton(string text, EventHandler handler, Color color)
        {
            var button = new Button
            {
                Text = text,
                Width = 265,
                Height = 38,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += handler;
            _actionButtons.Add(button);
            return button;
        }

        private static string OnOff(bool value)
        {
            return value ? "ON" : "OFF";
        }

        private async Task RebuildDatabaseAsync()
        {
            if (_busy) return;
            SetBusy(true, true);
            Log(LocalizationManager.GetString("LogBaseScanning"));
            try
            {
                if (_options.EnableImages)
                {
                    await Task.Run(() => ImageFeatureExtractor.Warmup());
                }

                var counts = await Task.Run(() => _database.Rebuild(Log, LocalizationManager.CurrentCulture));
                Log(LocalizationManager.GetString("LogBaseLoaded",
                    counts["DOBRE"], counts["ZLE"], counts["KRYTYCZNE"]));
                if (!_database.HasAnyReference())
                {
                    Log(LocalizationManager.GetString("LogBaseEmptyWarning"));
                }
            }
            catch (Exception ex)
            {
                Log(LocalizationManager.GetString("LogBaseError", ex.Message));
            }
            finally
            {
                SetBusy(false, false);
            }
        }

        private async Task PickFilesAsync()
        {
            if (_busy) return;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Multiselect = true;
                dialog.Filter = LocalizationManager.GetString("LogPickFilesFilter");
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    await ScanFilesAsync(dialog.FileNames);
                }
            }
        }

        private async Task ScanFolderAsync()
        {
            if (_busy) return;
            _paths.EnsureDirectories();
            var files = Directory.EnumerateFiles(_paths.ScanFolder)
                .Where(CringeRules.IsSupported)
                .OrderBy(x => x)
                .ToArray();

            if (files.Length == 0)
            {
                Log(LocalizationManager.GetString("LogScanFolderEmpty"));
                return;
            }

            await ScanFilesAsync(files);
        }

        private async Task ScanFilesAsync(IList<string> files)
        {
            if (files == null || files.Count == 0 || _busy) return;
            var runResults = new List<AnalysisResult>();
            _chart.ResetChart();
            _chart.SetTotal(files.Count);
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Minimum = 0;
            _progress.Maximum = files.Count;
            _progress.Value = 0;
            SetBusy(true, false);

            Log(LocalizationManager.GetString("LogAuditStart", files.Count));
            try
            {
                for (var i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    Log(LocalizationManager.GetString("LogScanFile", Path.GetFileName(file)));
                    AnalysisResult result;
                    try
                    {
                        result = await Task.Run(() => _analyzer.AnalyzeFile(file, AddIntermediateResult));
                    }
                    catch (Exception ex)
                    {
                        result = AnalysisResult.Skipped(file, LocalizationManager.GetString("LogAnalysisError", ex.Message));
                    }

                    runResults.Add(result);
                    AddFinalResult(result);
                    _progress.Value = Math.Min(_progress.Maximum, i + 1);
                }

                var max = runResults.Count == 0 ? 0 : runResults.Max(x => x.Bdpm);
                Log(LocalizationManager.GetString("LogAuditEnd", max));

                if (_options.EnableReports && runResults.Count > 0)
                {
                    var report = await Task.Run(() => ReportWriter.WriteSummary(_paths, runResults, LocalizationManager.CurrentCulture));
                    Log(LocalizationManager.GetString("LogReportSummary", Path.GetFileName(report)));
                }
            }
            finally
            {
                SetBusy(false, false);
            }
        }

        private void AddIntermediateResult(AnalysisResult result)
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() =>
            {
                _chart.AddPoint(result.ChartLabel ?? result.FileName, result.Bdpm);
                UpdateStatus(result.Bdpm);
                if (result.Preview != null)
                {
                    SetPreview(result.Preview);
                    result.Preview.Dispose(); // Cleanup after UI update
                }
            }));
        }

        private void AddFinalResult(AnalysisResult result)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => AddFinalResult(result)));
                return;
            }

            _currentFilePath = result.FilePath;
            UpdateStatus(result.Bdpm);
            _chart.AddPoint(result.ChartLabel ?? result.FileName, result.Bdpm);

            var rowIndex = _history.Rows.Add(result.FileName, result.Type, result.Bdpm, result.Verdict, result.Details);
            var row = _history.Rows[rowIndex]; // This line needs to be after the columns are added in CreateHistoryGrid
            row.DefaultCellStyle.ForeColor = CringeRules.GetAcc(result.Bdpm).Color;

            if (result.Preview != null)
            {
                SetPreview(result.Preview);
                result.Preview.Dispose();
                result.Preview = null;
            }
            
            Log(LocalizationManager.GetString("LogResult", result.FileName, result.Bdpm, result.Verdict, result.Details));
        }

        private async Task DownloadUrlAsync()
        {
            if (_busy || !_options.EnableUrlDownload) return;
            var url = _urlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                Log(LocalizationManager.GetString("LogDownloadURLPrompt"));
                return;
            }

            SetBusy(true, true);
            try
            {
                Log(LocalizationManager.GetString("LogDownloadURLStart", url));
                var target = await _analyzer.DownloadToScanFolderAsync(url);
                _urlBox.Clear();
                Log(LocalizationManager.GetString("LogDownloadURLSuccess", Path.GetFileName(target)));
            }
            catch (Exception ex)
            {
                Log("[BŁĄD POBIERANIA] " + ex.Message);
            }
            finally
            {
                SetBusy(false, false);
            }
        }

        private void EditTemplate()
        {
            using (var form = new Form())
            {
                form.Text = LocalizationManager.GetString("TemplateEditorTitle");
                form.Size = new Size(400, 200);
                form.StartPosition = FormStartPosition.CenterParent;
                form.BackColor = Color.FromArgb(45, 45, 48);
                form.ForeColor = Color.Gainsboro;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;

                var label = new Label { Text = LocalizationManager.GetString("TemplateEditorLabel"), Dock = DockStyle.Top, Height = 40, Padding = new Padding(10) };
                var textBox = new TextBox { Dock = DockStyle.Top, Margin = new Padding(10), Text = string.Join(", ", _options.Keywords) };
                var btnOk = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60) };
                
                btnOk.Click += (s, e) => 
                {
                    _options.Keywords = textBox.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                .Select(k => k.Trim())
                                                .Where(k => !string.IsNullOrEmpty(k))
                                                .ToList();
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };

                form.Controls.Add(textBox);
                form.Controls.Add(label);
                form.Controls.Add(btnOk);
                form.ShowDialog(this);
            }
        }

        private async Task RateCurrentAsync(string rating)
        {
            if (_busy) return;
            if (string.IsNullOrWhiteSpace(_currentFilePath) && _preview.Image == null)
            {
                Log(LocalizationManager.GetString("LogActiveLearningNoFile"));
                return;
            }

            try
            {
                string target;
                if (!string.IsNullOrWhiteSpace(_currentFilePath) && File.Exists(_currentFilePath) && CringeRules.IsImage(_currentFilePath))
                {
                    target = _analyzer.CopyToTraining(_currentFilePath, rating);
                }
                else
                {
                    if (_preview.Image == null)
                    {
                        Log(LocalizationManager.GetString("LogActiveLearningNoPreview"));
                        return;
                    }

                    var sourceName = string.IsNullOrWhiteSpace(_currentFilePath) ? "podglad" : Path.GetFileName(_currentFilePath);
                    target = _analyzer.SavePreviewToTraining(_preview.Image, rating, sourceName);
                }

                Log(LocalizationManager.GetString("LogActiveLearningSaved", rating, Path.GetFileName(target)));
                await RebuildDatabaseAsync(); // Rebuild database after active learning
            }
            catch (Exception ex)
            {
                Log("[ACTIVE LEARNING] Błąd: " + ex.Message);
            }
        }

        private async Task ExportZipAsync()
        {
            if (_busy || !_options.EnableReports) return;
            SetBusy(true, true);
            try
            {
                var zip = await Task.Run(() => ZipExporter.Export(_paths));
                Log(LocalizationManager.GetString("LogExportZipReady", Path.GetFileName(zip)));
            }
            catch (Exception ex)
            {
                Log("[EKSPORT] Błąd: " + ex.Message);
            }
            finally
            {
                SetBusy(false, false);
            }
        }

        private async Task ExportCedAsync()
        {
            if (_busy) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "Cringe Engine Data|*.ced";
                sfd.FileName = "Baza_" + CringeRules.Timestamp() + ".ced";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    SetBusy(true, true);
                    try
                    {
                        var path = await Task.Run(() => CedManager.Export(_paths, sfd.FileName, (cur, tot) => 
                        {
                            BeginInvoke(new Action(() => { _progress.Maximum = tot; _progress.Value = cur; }));
                        }));
                        Log(LocalizationManager.GetString("LogCedExported", Path.GetFileName(path)));
                    }
                    catch (Exception ex)
                    {
                        Log(LocalizationManager.GetString("LogCedError", ex.Message));
                    }
                    finally { SetBusy(false, false); }
                }
            }
        }

        private void StageFiles(List<string> targetList)
        {
            using (var ofd = new OpenFileDialog { Multiselect = true, Filter = "Obrazy|*.jpg;*.jpeg;*.png;*.webp" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    targetList.AddRange(ofd.FileNames);
                    Log(LocalizationManager.GetString("LogCreatorAdded", ofd.FileNames.Length, targetList.Count));
                }
            }
        }

        private async Task BuildAndLoadStagedCedAsync()
        {
            if (_stagedGood.Count == 0 && _stagedBad.Count == 0 && _stagedCritical.Count == 0)
            {
                Log(LocalizationManager.GetString("LogCreatorNoFiles"));
                return;
            }

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "Cringe Engine Data|*.ced";
                sfd.FileName = "NowaPaczka_" + CringeRules.Timestamp() + ".ced";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    SetBusy(true, true);
                    try
                    {
                        string targetCed = sfd.FileName;

                        await Task.Run(() => {
                            if (File.Exists(targetCed)) File.Delete(targetCed);
                            using (var archive = ZipFile.Open(targetCed, ZipArchiveMode.Create))
                            {
                                AddToZip(archive, _stagedGood, "DOBRE");
                                AddToZip(archive, _stagedBad, "ZLE");
                                AddToZip(archive, _stagedCritical, "KRYTYCZNE");
                            }
                        });

                        Log(LocalizationManager.GetString("LogCreatorCreated", Path.GetFileName(targetCed)));
                        Log(LocalizationManager.GetString("LogCreatorImporting"));

                        await Task.Run(() => CedManager.Import(_paths, targetCed, false));

                        _stagedGood.Clear();
                        _stagedBad.Clear();
                        _stagedCritical.Clear();

                        await RebuildDatabaseAsync();
                    }
                    catch (Exception ex)
                    {
                        Log(LocalizationManager.GetString("LogCreatorError", ex.Message));
                    }
                    finally
                    {
                        SetBusy(false, false);
                    }
                }
            }
        }

        private void AddToZip(ZipArchive archive, List<string> files, string folderName)
        {
            foreach (var file in files)
            {
                if (File.Exists(file))
                {
                    archive.CreateEntryFromFile(file, folderName + "/" + Path.GetFileName(file));
                }
            }
        }

        private async Task LoadCedAsync()
        {
            if (_busy) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Cringe Engine Data|*.ced";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    await ImportCedFileAsync(ofd.FileName);
                }
            }
        }

        private async Task ImportCedFileAsync(string path)
        {
            if (_busy) return;
            SetBusy(true, true);
            try
            {
                await Task.Run(() => CedManager.Import(_paths, path, false, (cur, tot) =>
                {
                    BeginInvoke(new Action(() => { _progress.Maximum = tot; _progress.Value = cur; }));
                }));
                Log(LocalizationManager.GetString("LogCedImported"));
                await RebuildDatabaseAsync();
            }
            catch (Exception ex)
            {
                Log(LocalizationManager.GetString("LogCedError", ex.Message));
            }
            finally { SetBusy(false, false); }
        }

        private void UpdateStatus(int bdpm)
        {
            var acc = CringeRules.GetAcc(bdpm);
            _bdpmLabel.Text = bdpm.ToString();
            _bdpmLabel.ForeColor = acc.Color;
            _verdictLabel.Text = acc.Label;
            _verdictLabel.ForeColor = acc.Color;
        }

        private void SetPreview(Image image)
        {
            var old = _preview.Image;
            _preview.Image = new Bitmap(image);
            if (old != null)
            {
                old.Dispose();
            }
        }

        private void SetBusy(bool busy, bool marquee)
        {
            _busy = busy;
            foreach (var button in _actionButtons)
            {
                button.Enabled = !busy;
            }
            _progress.Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy && _progress.Style == ProgressBarStyle.Blocks)
            {
                _progress.Value = 0;
            }
        }

        private void Log(string message)
        {
            if (_logBox == null || IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Log(message)));
                return;
            }

            _logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        }

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private async void Form1_DragDrop(object sender, DragEventArgs e)
        {
            if (_busy) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;

            // Try to find a CED file first
            var cedFile = files.FirstOrDefault(f => Path.GetExtension(f).Equals(".ced", StringComparison.OrdinalIgnoreCase));
            if (cedFile != null)
            {
                await ImportCedFileAsync(cedFile);
                return;
            }

            // Handle media files (Images, Videos, 3D Models)
            var supportedFiles = files.Where(f => CringeRules.IsSupported(f)).ToList();
            if (supportedFiles.Count > 0)
            {
                var copiedFiles = new List<string>();
                _paths.EnsureDirectories();
                foreach (var file in supportedFiles)
                {
                    var dest = Path.Combine(_paths.ScanFolder, Path.GetFileName(file));
                    try
                    {
                        File.Copy(file, dest, true);
                        copiedFiles.Add(dest);
                    }
                    catch (Exception ex) { Log(LocalizationManager.GetString("LogActiveLearningError", ex.Message)); }
                }

                if (copiedFiles.Count > 0) await ScanFilesAsync(copiedFiles);
            }
        }

        private static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }

        /// <summary>
        /// Handles the language selection change.
        /// For a full localization, this would involve loading resource files
        /// and updating all UI text dynamically.
        /// </summary>
        private void LanguageSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedLanguageText = _languageSelector.SelectedItem.ToString();
            if (selectedLanguageText.Contains("(") && selectedLanguageText.Contains(")"))
            {
                var cultureCode = selectedLanguageText.Split('(')[1].TrimEnd(')');
                LocalizationManager.SetLanguage(cultureCode);
                Log(LocalizationManager.GetString("LogLanguageChanged", selectedLanguageText));
                RefreshUIStrings();
            }
        }

        private void RefreshUIStrings()
        {
            if (_mainSplit == null || _mainTabControl == null) return;

            // Update main form title
            Text = LocalizationManager.GetString("AppTitle");

            // Update header
            _rootLabel.Text = LocalizationManager.GetString("DataPath") + _paths.Root;
            UpdateStatus(int.Parse(_bdpmLabel.Text)); // Refresh verdict label based on current BDPM

            // Update sidebar sections and buttons
            _actionButtons.Clear(); // Zapobiegamy duplikowaniu przycisków w liście przy zmianie języka
            var sidebarPanel = BuildSidebar();
            _mainSplit.Panel1.Controls.Clear();
            _mainSplit.Panel1.Controls.Add(sidebarPanel);

            // Update tab control
            _mainTabControl.TabPages[0].Text = LocalizationManager.GetString("TabHistory");
            _mainTabControl.TabPages[1].Text = LocalizationManager.GetString("TabLog");

            // Update history grid headers
            _history.Columns[0].HeaderText = LocalizationManager.GetString("GridColumnFile");
            _history.Columns[1].HeaderText = LocalizationManager.GetString("GridColumnType");
            _history.Columns[2].HeaderText = LocalizationManager.GetString("GridColumnBDPM");
            _history.Columns[3].HeaderText = LocalizationManager.GetString("GridColumnVerdict");
            _history.Columns[4].HeaderText = LocalizationManager.GetString("GridColumnDetails");

            // Update placeholder text for URL box
            SetPlaceholder(_urlBox, LocalizationManager.GetString("PlaceholderURL"));

            // Update initial log messages (if they are still visible or need re-logging)
            // For simplicity, we just log the initial messages again in the new language.
            // In a real app, you might clear and re-populate the log or only update static parts.
            Log(LocalizationManager.GetString("LogBoot"));
            Log(LocalizationManager.GetString("LogInfoExamples"));
            Log(LocalizationManager.GetString("LogInfoChart"));
            Log(LocalizationManager.GetString("LogInfoReload"));

            // Update LiveChartControl (if it has localized strings)
            // The LiveChartControl itself needs to be updated to use LocalizationManager
            // For now, it's not directly updated here, but its internal strings should use LocalizationManager.

            // Update BootOptionsForm (if it's still relevant or needs to be re-shown)
            // This would typically be handled when the BootOptionsForm is created.
        }

        // Helper to rebuild sidebar content
        private void RebuildSidebarContent(FlowLayoutPanel panel)
        {
            panel.Controls.Clear();
            // Re-add all controls using localized strings
            // This part is already handled by calling BuildSidebar() and replacing its content.
            // The BuildSidebar method itself needs to use LocalizationManager.GetString()
        }
        
        /// <summary>
        /// Displays information about the program in a message box.
        /// </summary>
        private void InfoButton_Click(object sender, EventArgs e)
        {
            var appName = Application.ProductName;
            var appVersion = Application.ProductVersion;
            // Use System.Reflection to get assembly attributes for more detailed info
            var copyright = ((System.Reflection.AssemblyCopyrightAttribute)Attribute.GetCustomAttribute(
                System.Reflection.Assembly.GetExecutingAssembly(), typeof(System.Reflection.AssemblyCopyrightAttribute), false))?.Copyright;
            var trademark = ((System.Reflection.AssemblyTrademarkAttribute)Attribute.GetCustomAttribute(
                System.Reflection.Assembly.GetExecutingAssembly(), typeof(System.Reflection.AssemblyTrademarkAttribute), false))?.Trademark;
            var description = ((System.Reflection.AssemblyDescriptionAttribute)Attribute.GetCustomAttribute(
                System.Reflection.Assembly.GetExecutingAssembly(), typeof(System.Reflection.AssemblyDescriptionAttribute), false))?.Description;

            var infoText = new StringBuilder();
            infoText.AppendLine($"{appName} v{appVersion}"); // AppName and Version are not localized
            infoText.AppendLine(description ?? LocalizationManager.GetString("InfoAboutDescription"));
            infoText.AppendLine();
            infoText.AppendLine(copyright ?? LocalizationManager.GetString("InfoAboutCopyright"));
            infoText.AppendLine(trademark ?? LocalizationManager.GetString("InfoAboutTrademark"));
            infoText.AppendLine();
            infoText.AppendLine(LocalizationManager.GetString("InfoAboutAuthor"));
            infoText.AppendLine(LocalizationManager.GetString("InfoAboutAIModel"));
            infoText.AppendLine(LocalizationManager.GetString("InfoAboutLibraries"));
            infoText.AppendLine(LocalizationManager.GetString("InfoAboutSupport"));
            infoText.AppendLine(LocalizationManager.GetString("InfoAboutDisclaimer"));
            infoText.AppendLine(LocalizationManager.GetString("InfoAboutBuild"));

            MessageBox.Show(infoText.ToString(), LocalizationManager.GetString("InfoAboutTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_videoSource != null && _videoSource.IsRunning) _videoSource.Stop();
            if (_preview != null && _preview.Image != null)
            {
                _preview.Image.Dispose();
                _preview.Image = null;
            }
        }

        private void ToggleCamera()
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                _videoSource = null;
                Log(LocalizationManager.GetString("CameraOff"));
                _preview.Image = null;
            }
            else
            {
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (_videoDevices.Count == 0) return;
                _videoSource = new VideoCaptureDevice(_videoDevices[0].MonikerString);
                _videoSource.NewFrame += (s, e) => {
                    var old = _preview.Image;
                    _preview.Image = (Bitmap)e.Frame.Clone();
                    old?.Dispose();
                };
                _videoSource.Start();
                Log(LocalizationManager.GetString("CameraOn"));
            }
        }

        private async Task ScanCameraAsync()
        {
            if (_videoSource == null || !_videoSource.IsRunning || _preview.Image == null)
            {
                Log(LocalizationManager.GetString("CameraError"));
                return;
            }
            
            using (var bmp = new Bitmap(_preview.Image))
            {
                var result = await Task.Run(() => _database.AnalyzeBitmap(bmp, "Kamera_LIVE"));
                result.Preview = new Bitmap(bmp);
                AddFinalResult(result);
            }
        }

        /// <summary>
        /// Sprawdza dostępność nowej wersji na GitHubie.
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    // GitHub wymaga nagłówka User-Agent
                    client.Headers.Add("User-Agent", "Cringometr-App");
                    
                    // Pobieramy informacje o najnowszym wydaniu
                    string json = await client.DownloadStringTaskAsync("https://api.github.com/repos/adammc769/CringometrMDSPC/releases/latest");
                    
                    // Proste wyciągnięcie wersji z JSONa bez zewnętrznych bibliotek
                    var match = System.Text.RegularExpressions.Regex.Match(json, @"""tag_name"":\s*""([^""]+)""");
                    if (match.Success)
                    {
                        string latestVersionStr = match.Groups[1].Value.TrimStart('v');
                        if (Version.TryParse(latestVersionStr, out Version latestVersion))
                        {
                            Version currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                            if (latestVersion > currentVersion)
                            {
                                Log(LocalizationManager.GetString("LogNewVersionAvailable", latestVersionStr));
                                
                                string title = LocalizationManager.GetString("NewVersionTitle");
                                string msg = LocalizationManager.GetString("NewVersionMessage", latestVersionStr);
                                
                                if (MessageBox.Show(this, msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                                {
                                    Process.Start(new ProcessStartInfo { FileName = "https://github.com/adammc769/CringometrMDSPC/releases/latest", UseShellExecute = true });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("Update check failed: " + ex.Message); }
        }
    }
}
