using System;
using System.Drawing;
using System.Windows.Forms;

namespace CringometrMDSPC
{
    internal sealed class BootOptionsForm : Form
    {
        private readonly CheckBox _images;
        private readonly CheckBox _video;
        private readonly CheckBox _audio;
        private readonly CheckBox _models3d;
        private readonly CheckBox _reports;
        private readonly CheckBox _urlDownload;
        private readonly NumericUpDown _fpsInput;

        public AppOptions Options { get; private set; }

        public BootOptionsForm()
        {
            Options = new AppOptions();
            Text = LocalizationManager.GetString("BootManagerTitle");
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(45, 45, 48);
            ForeColor = Color.Gainsboro;
            ClientSize = new Size(480, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(28)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Controls.Add(root);

            var title = new Label
            {
                Text = LocalizationManager.GetString("AppTitle"),
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 22, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(200, 200, 200)
            };
            root.Controls.Add(title, 0, 0);

            var optionsPanel = new FlowLayoutPanel
            { // This panel needs to be localized
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(37, 37, 38),
                Padding = new Padding(18) // This padding is fine
            };
            root.Controls.Add(optionsPanel, 0, 1);

            _images = CreateOption(LocalizationManager.GetString("BootManagerCLIP"), Options.EnableImages);
            _video = CreateOption(LocalizationManager.GetString("BootManagerVideo"), Options.EnableVideo);
            _audio = CreateOption(LocalizationManager.GetString("BootManagerAudio"), Options.EnableAudio);
            _models3d = CreateOption(LocalizationManager.GetString("BootManager3D"), Options.Enable3D);
            _reports = CreateOption(LocalizationManager.GetString("BootManagerReports"), Options.EnableReports);
            _urlDownload = CreateOption(LocalizationManager.GetString("BootManagerURLDownload"), true);

            optionsPanel.Controls.Add(_images);
            optionsPanel.Controls.Add(_video);
            optionsPanel.Controls.Add(_audio);
            optionsPanel.Controls.Add(_models3d);
            optionsPanel.Controls.Add(_reports);
            optionsPanel.Controls.Add(_urlDownload);

            var fpsPanel = new TableLayoutPanel
            {
                Width = 390,
                Height = 40,
                ColumnCount = 2,
                Margin = new Padding(0, 10, 0, 0)
            };
            fpsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            fpsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

            var fpsLabel = new Label
            {
                Text = LocalizationManager.GetString("BootManagerFps"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10)
            };
            _fpsInput = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 1, Dock = DockStyle.Fill, BackColor = Color.FromArgb(28, 28, 28), ForeColor = Color.Gainsboro };
            
            fpsPanel.Controls.Add(fpsLabel, 0, 0);
            fpsPanel.Controls.Add(_fpsInput, 1, 0);
            optionsPanel.Controls.Add(fpsPanel);

            var startButton = new Button
            {
                Text = LocalizationManager.GetString("BootManagerStart"),
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(122, 78, 194),
                ForeColor = Color.White, // This is fine
                Font = new Font("Consolas", 13, FontStyle.Bold)
            };
            startButton.FlatAppearance.BorderSize = 0;
            startButton.Click += StartButton_Click;
            root.Controls.Add(startButton, 0, 2);

            var hint = new Label
            {
                Text = LocalizationManager.GetString("BootManagerHint"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 9)
            };
            root.Controls.Add(hint, 0, 3);
        }

        private CheckBox CreateOption(string text, bool value)
        {
            return new CheckBox
            {
                Text = text,
                Checked = value,
                AutoSize = false,
                Width = 390,
                Height = 38,
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 10),
                FlatStyle = FlatStyle.Flat
            };
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            Options = new AppOptions
            {
                EnableImages = _images.Checked,
                EnableVideo = _video.Checked,
                EnableAudio = _audio.Checked,
                Enable3D = _models3d.Checked,
                EnableReports = _reports.Checked,
                EnableUrlDownload = _urlDownload.Checked,
                VideoFps = (int)_fpsInput.Value
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
