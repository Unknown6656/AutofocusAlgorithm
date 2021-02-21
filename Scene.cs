using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Drawing.Text;
using System.Drawing;
using System.Linq;
using System.Text;
using System;

namespace AutofocusAlgorithm
{
    using static Math;


    public sealed class Scene
    {
        internal readonly List<SceneLayer> layers = new List<SceneLayer>();

        public int Height { get; }
        public int Width { get; }


        public Scene(int width, int height) => (Width, Height) = (width, height);

        public void Add(SceneLayer layer)
        {
            if (!layers.Contains(layer))
                layers.Add(layer);
        }

        public Bitmap Render(float focaldistance, float falloff, float bluramount)
        {
            bluramount = bluramount.Constrain();
            focaldistance = focaldistance.Constrain();
            falloff = falloff.Constrain() * 30;

            Func<float, float> distr = Util.GetFalloffDistribution(focaldistance, falloff);
            Bitmap bmp = new Bitmap(Width, Height);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceOver;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                
                foreach (SceneLayer l in layers.OrderBy(l => l.ZDistance).Reverse())
                {
                    int radius = (int)(bluramount * (1 - distr(l.ZDistance)) * 20);
                    float height = l.Image.Height;
                    float width = l.Image.Width;
                    float yoffs = (Height - height) / 2;
                    float xoffs = (Width - width) / 2;

                    if (radius > 0)
                    {
                        GaussianBlur blur = new GaussianBlur(l.Image);

                        g.DrawImage(blur.Process(radius), xoffs, yoffs, width, height);
                    }
                    else
                        g.DrawImage(l.Image, xoffs, yoffs, width, height);
                }

                return bmp;
            }
        }
    }

    public struct SceneLayer
    {
        public float ZDistance { get; }
        public Bitmap Image { get; }


        public SceneLayer(float zdist, string path)
            : this(zdist, System.Drawing.Image.FromFile(path) as Bitmap)
        {
        }

        public SceneLayer(float zdist, Bitmap img) => (ZDistance, Image) = (zdist.Constrain(), img);
    }
}
