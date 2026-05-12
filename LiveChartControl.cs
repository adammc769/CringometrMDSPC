using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CringometrMDSPC
{
    internal sealed class LiveChartControl : Control
    {
        private readonly List<ChartPoint> _points = new List<ChartPoint>();
        private readonly object _sync = new object();
        private int _total;

        public LiveChartControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(17, 17, 17);
            ForeColor = Color.Gainsboro;
            Font = new Font("Segoe UI", 8);
            MinimumSize = new Size(260, 180);
        }

        public void SetTotal(int total)
        {
            _total = Math.Max(0, total);
            Invalidate();
        }

        public void ResetChart()
        {
            lock (_sync)
            {
                _points.Clear();
                _total = 0;
            }
            Invalidate();
        }

        public void AddPoint(string label, int bdpm)
        {
            lock (_sync)
            {
                _points.Add(new ChartPoint
                {
                    Label = string.IsNullOrWhiteSpace(label) ? "wynik" : label,
                    Bdpm = Math.Max(0, Math.Min(1200, bdpm))
                });
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            var bounds = ClientRectangle;
            if (bounds.Width < 20 || bounds.Height < 20)
            {
                return;
            }

            using (var titleBrush = new SolidBrush(Color.FromArgb(160, 160, 160)))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(105, 105, 105)))
            using (var axisPen = new Pen(Color.FromArgb(52, 52, 52)))
            {
                e.Graphics.DrawString(LocalizationManager.GetString("LiveChartTitle"), new Font(Font.FontFamily, 9, FontStyle.Bold), titleBrush, 12, 9);

                List<ChartPoint> points;
                lock (_sync)
                {
                    points = _points.Skip(Math.Max(0, _points.Count - 42)).ToList();
                }

                if (points.Count == 0)
                {
                    e.Graphics.DrawString(LocalizationManager.GetString("LiveChartWaiting"), Font, mutedBrush, 12, 36);
                    return;
                }

                var chart = new Rectangle(42, 32, bounds.Width - 56, bounds.Height - 70);
                if (chart.Width <= 0 || chart.Height <= 0)
                {
                    return;
                }

                e.Graphics.DrawLine(axisPen, chart.Left, chart.Bottom, chart.Right, chart.Bottom);
                e.Graphics.DrawLine(axisPen, chart.Left, chart.Top, chart.Left, chart.Bottom);

                DrawThreshold(e.Graphics, chart, 50, Color.FromArgb(255, 136, 0), "50");
                DrawThreshold(e.Graphics, chart, 500, Color.FromArgb(255, 0, 0), "500");
                DrawThreshold(e.Graphics, chart, 1000, Color.FromArgb(255, 0, 255), "1000");

                var slot = Math.Max(8f, chart.Width / (float)points.Count);
                var barWidth = Math.Max(5f, Math.Min(22f, slot * 0.62f));
                for (var i = 0; i < points.Count; i++)
                {
                    var point = points[i];
                    var x = chart.Left + (i * slot) + ((slot - barWidth) / 2f);
                    var h = (float)(point.Bdpm / 1200.0 * chart.Height);
                    var rect = new RectangleF(x, chart.Bottom - h, barWidth, Math.Max(1, h));
                    using (var brush = new SolidBrush(CringeRules.GetAcc(point.Bdpm).Color))
                    {
                        e.Graphics.FillRectangle(brush, rect);
                    }

                    if (slot > 14)
                    {
                        var shortLabel = point.Label.Length > 10 ? point.Label.Substring(0, 10) : point.Label;
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                        {
                            e.Graphics.TranslateTransform(x + (barWidth / 2f), chart.Bottom + 4);
                            e.Graphics.RotateTransform(-35);
                            e.Graphics.DrawString(shortLabel, Font, mutedBrush, 0, 0, sf);
                            e.Graphics.ResetTransform();
                        }
                    }
                }

                var max = points.Max(p => p.Bdpm);
                var acc = CringeRules.GetAcc(max);
                using (var accBrush = new SolidBrush(acc.Color))
                {
                    var progress = _total > 0 ? string.Format(LocalizationManager.CurrentCulture, " | {0}/{1}", _points.Count, _total) : string.Empty;
                    e.Graphics.DrawString(LocalizationManager.GetString("LiveChartMax", max, acc.Label, progress), Font, accBrush, 108, 10);
                }
            }
        }

        private static void DrawThreshold(Graphics g, Rectangle chart, int value, Color color, string label)
        {
            var y = chart.Bottom - (float)(value / 1200.0 * chart.Height);
            using (var pen = new Pen(Color.FromArgb(110, color)))
            using (var brush = new SolidBrush(Color.FromArgb(150, color)))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, chart.Left, y, chart.Right, y);
                g.DrawString(label, SystemFonts.CaptionFont, brush, chart.Left + 4, y - 14);
            }
        }

        private sealed class ChartPoint
        {
            public string Label { get; set; }
            public int Bdpm { get; set; }
        }
    }
}
