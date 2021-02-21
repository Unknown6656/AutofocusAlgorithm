using System.Threading.Tasks;
using System.Drawing;
using System.Linq;
using System;

namespace AutofocusAlgorithm
{
    using static Math;


    public sealed unsafe class Autofocus
    {
        public AutofocusStrategy Strategy { get; }


        public Autofocus(AutofocusStrategy st) => Strategy = st;

        public AutofocusResult Focus(float falloff, float blur, float step = .01f)
        {
            int count = (int)Ceiling(1 / step);
            AutofocusResult res = new AutofocusResult
            {
                Frames = new(Bitmap, Bitmap, float, AutofocusSpectrum)[count]
            };

            for (int i = 0; i < count; ++i)
            {
                float f = step * i;
                Bitmap b = new Bitmap(Strategy._region.Width, Strategy._region.Height);

                using (Bitmap r = Strategy._scene.Render(f, falloff, blur))
                using (Graphics g = Graphics.FromImage(b))
                    g.DrawImage(r, -Strategy._region.Left, -Strategy._region.Top, r.Width, r.Height);

                AutofocusSpectrum spec = new AutofocusSpectrum
                {
                    Raw = new double[256],
                };
                Bitmap c = b.Convolute(new double[3, 3] {
                    { -1, 1, -1 },
                    { 0, 0, 0 },
                    { 1, -1, 1 },
                }, new double[3, 3] {
                    { -1, 0, 1 },
                    { 1, 0, -1 },
                    { -1, 0, 1 },
                });

                c.Lock(ptr =>
                {
                    long sumr = 0, sumg = 0, sumb = 0;

                    for (int y = 0; y < c.Height; ++y)
                        for (int x = 0; x < c.Width; ++x)
                        {
                            static int square(int n) => n * n;

                            ++spec.Raw[ptr[y * c.Width + x].Gray];

                            RGB* px1 = ptr + y * c.Width + (x + 1) % c.Width;
                            RGB* px2 = ptr + ((y + 1) % c.Height) * c.Width + x;

                            sumr += square(px1->R - px2->R);
                            sumg += square(px1->G - px2->G);
                            sumb += square(px1->B - px2->B);
                        }

                    spec.RSum = sumr;
                    spec.GSum = sumg;
                    spec.BSum = sumb;
                });

                res.Frames[i] = (b, c, f, spec);
            }

            res.FocalDistance = (from f in res.Frames
                                 orderby f.spectrum.Contrast descending
                                 select f.focal).First();

            return res;
        }
    }

    public sealed class AutofocusStrategy
    {
        internal readonly Rectangle _region;
        internal readonly Scene _scene;


        private AutofocusStrategy(Scene s, Rectangle r) => (_scene, _region) = (s, r);

        public static AutofocusStrategy FocusRegion(Scene scene, Rectangle region) => new AutofocusStrategy(scene, region);

        public static AutofocusStrategy FocusEntireImage(Scene scene) => new AutofocusStrategy(scene, new Rectangle(0, 0, scene.Width, scene.Height));
    }

    public struct AutofocusResult
    {
        public (Bitmap original, Bitmap derivative, float focal, AutofocusSpectrum spectrum)[] Frames { internal set; get; }
        public float FocalDistance { internal set; get; }
    }

    public struct AutofocusSpectrum
    {
        internal long RSum { set; get; }

        internal long GSum { set; get; }

        internal long BSum { set; get; }

        public double Contrast => Sqrt(RSum + GSum + BSum);

        public double[] Raw { internal set; get; }

        public double[] Normalized
        {
            get
            {
                double[] dat = new double[Raw.Length];
                double max = Raw.Max();
                double min = Raw.Min();

                if (min - max == 0)
                    ++max;

                Array.Copy(Raw, dat, Raw.Length);
                Parallel.For(0, Raw.Length, i => dat[i] = (dat[i] - min) / (max - min));

                return dat;
            }
        }

        public double Average => Raw.Average();
        
        public double StandardDeviation
        {
            get
            {
                double avg = Average;
                double sum = 0;

                for (int i = 0; i < Raw.Length; ++i)
                    sum += Pow(Raw[i] - avg, 2);

                return Sqrt(sum / Raw.Length);
            }
        }

        public double NormalizedStandardDeviation
        {
            get
            {
                double[] norm = Normalized;
                double avg = norm.Average();
                double sum = 0;

                for (int i = 0; i < norm.Length; ++i)
                    sum += Pow(norm[i] - avg, 2);

                return Sqrt(sum / norm.Length);
            }
        }
    }
}
