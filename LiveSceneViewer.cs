using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutofocusAlgorithm
{
    using static Math;


    public partial class LiveSceneViewer
        : Form
    {
        private readonly (Scene, string)[] _scenes;
        private (Point start, Point end) _focusframe;
        private (Point start, Point end) _rawfocframe;


        public LiveSceneViewer((Scene, string)[] s)
        {
            InitializeComponent();

            foreach (var sc in _scenes = s)
                comboBox1.Items.Add($"{sc.Item2} ({sc.Item1.Width} x {sc.Item1.Height} pixels)");

            comboBox1.SelectedIndex = 0;
        }

        private void UpdateScene()
        {
            float focal = getval(trackBar1);
            float falloff = getval(trackBar2);

            pictureBox1.Image?.Dispose();
            pictureBox1.Image = new Bitmap(pictureBox1.Width, pictureBox1.Height);

            _focusframe.start = new Point(Min(_rawfocframe.start.X, _rawfocframe.end.X), Min(_rawfocframe.start.Y, _rawfocframe.end.Y));
            _focusframe.end = new Point(Max(_rawfocframe.start.X, _rawfocframe.end.X), Max(_rawfocframe.start.Y, _rawfocframe.end.Y));

            if (_focusframe.start != _focusframe.end)
                using (Graphics g = Graphics.FromImage(pictureBox1.Image))
                {
                    Rectangle rect = new Rectangle(_focusframe.start, new Size(_focusframe.end.X - _focusframe.start.X, _focusframe.end.Y - _focusframe.start.Y));

                    g.FillRectangle(new SolidBrush(Color.FromArgb(0x30ff0000)), rect);
                    g.DrawRectangle(Pens.Red, rect);
                }

            pictureBox1.BackgroundImage = _scenes[comboBox1.SelectedIndex].Item1.Render(focal, falloff, falloff);

            label5.Text = $"{focal:F7}";
            label6.Text = $"{falloff:F7}";
        }

        private float getval(TrackBar tb) => (float)(tb.Value - tb.Minimum) / (tb.Maximum - tb.Minimum - 1);

        private Point GetUnscaledCoord(Point pt)
        {
            int iw = pictureBox1.BackgroundImage.Width;
            int ih = pictureBox1.BackgroundImage.Height;
            int cw = pictureBox1.Width;
            int ch = pictureBox1.Height;

            float ir = iw / (float)ih;
            float cr = cw / (float)ch;

            if (ir >= cr)
            {
                float scaleFactor = cw / (float)iw;
                float scaledHeight = ih * scaleFactor;
                float filler = Abs(ch - scaledHeight) / 2;

                return new Point((int)(pt.X / scaleFactor), (int)((pt.Y - filler) / scaleFactor));
            }
            else
            {
                float scaleFactor = ch / (float)ih;
                float scaledWidth = iw * scaleFactor;
                float filler = Abs(cw - scaledWidth) / 2;

                return new Point((int)((pt.X - filler) / scaleFactor), (int)(pt.Y / scaleFactor));
            }
        }

        private void TrackBar1_Scroll(object sender, EventArgs e) => UpdateScene();

        private void TrackBar2_Scroll(object sender, EventArgs e) => UpdateScene();

        private void LiveSceneViewer_Load(object sender, EventArgs e) => UpdateScene();

        private void Button1_Click(object sender, EventArgs e)
        {
            Scene scene = _scenes[comboBox1.SelectedIndex].Item1;
            AutofocusStrategy strat = AutofocusStrategy.FocusEntireImage(scene);

            if (_focusframe.start != _focusframe.end)
            {
                Point ps = GetUnscaledCoord(_focusframe.start);
                Point pe = GetUnscaledCoord(_focusframe.end);

                strat = AutofocusStrategy.FocusRegion(scene, new Rectangle(ps, new Size(pe.X - ps.X, pe.Y - ps.Y)));
            }

            Autofocus af = new Autofocus(strat);
            AutofocusResult res = af.Focus(getval(trackBar2), getval(trackBar2), 1f / 20);

            trackBar1.Value = (int)(trackBar1.Minimum + res.FocalDistance * (trackBar1.Maximum - trackBar1.Minimum));

            UpdateScene();

            panel1.Controls.Clear();

            int wdh = 2;

            foreach (var (original, derivative, focal, spectrum) in res.Frames)
            {
                int imgh = 128; // original.Height;
                int imgw = 128; // original.Width;

                panel1.Controls.Add(new Label
                {
                    Top = 10,
                    Left = wdh,
                    Width = imgw,
                    Height = 23,
                    Text = $"{focal:F7}",
                });
                panel1.Controls.Add(new PictureBox
                {
                    Top = 25,
                    Left = wdh,
                    Width = imgw,
                    Height = imgh,
                    Image = original,
                    SizeMode = PictureBoxSizeMode.Zoom,
                });
                panel1.Controls.Add(new PictureBox
                {
                    Top = 27 + imgh,
                    Left = wdh,
                    Width = imgw,
                    Height = imgh,
                    Image = derivative,
                    SizeMode = PictureBoxSizeMode.Zoom,
                });
                
                string txt = $"avg: {spectrum.Average:F3}" + Environment.NewLine +
                             $"dev: {spectrum.StandardDeviation:F3}" + Environment.NewLine +
                             $"ndv: {spectrum.NormalizedStandardDeviation:F3}" + Environment.NewLine +
                             $"Σ r: {spectrum.RSum}" + Environment.NewLine +
                             $"Σ g: {spectrum.GSum}" + Environment.NewLine +
                             $"Σ b: {spectrum.BSum}" + Environment.NewLine +
                             $"cnt: {spectrum.Contrast:F3}";

                panel1.Controls.Add(new Label
                {
                    Top = 29 + 2 * imgh,
                    Left = wdh,
                    Width = imgw,
                    Text = txt,
                    Height = txt.Split('\n').Length * 23,
                    BackColor = focal == res.FocalDistance ? Color.Green : Color.Transparent
                });
                
                wdh += imgw + 2;
            }
        }

        private void ComboBox1_SelectedIndexChanged(object sender, EventArgs e) => Button2_Click(sender, e);

        private void PictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _rawfocframe.end =
                _rawfocframe.start = e.Location;

                UpdateScene();
            }
        }

        private void PictureBox1_MouseMove(object sender, MouseEventArgs e) => PictureBox1_MouseUp(sender, e);

        private void PictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _rawfocframe.end = e.Location;

                UpdateScene();
            }
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            _rawfocframe.start =
            _rawfocframe.end = new Point(-1, -1);

            UpdateScene();
        }
    }
}
