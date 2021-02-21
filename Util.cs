using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Drawing;
using System;

namespace AutofocusAlgorithm
{
    using static Math;


    public unsafe delegate void LockUtilization(RGB* ptr);

    public static unsafe class Util
    {
        internal const double σ = 1 / (PI * .16);


        internal static float Constrain(this float v, float min = 0, float max = 1) => Max(min, Min(v, max));

        internal static double Constrain(this double v, double min = 0, double max = 1) => Max(min, Min(v, max));

        internal static Func<float, float> GetFalloffDistribution(float offs, float foff) =>
            x => (float)(σ * Pow(E, Pow(PI * foff * x - foff * offs, 2) / -2)) / 2;

        internal static bool match(this string s, string p, out Match m, RegexOptions opt = RegexOptions.Compiled | RegexOptions.IgnoreCase) => (m = Regex.Match(s, p, opt)).Success;

        internal static Bitmap Convolute(this Bitmap bmp, double[,] hkernel, double[,] vkernel, ConvolutionMode mode = ConvolutionMode.RepeatEdges)
        {
            int ksize = hkernel.GetLength(0);
            int h = bmp.Height;
            int w = bmp.Width;

            if ((ksize * ksize != hkernel.Length) || (ksize < 3) || (ksize % 2 == 0) ||
                (ksize * ksize != vkernel.Length) || (vkernel.GetLength(0) != ksize))
                throw new ArgumentException("The convolution kernels must be squared with an odd side length and must have an equal size.");

            Bitmap dst = new Bitmap(w, h);

            bmp.Lock(psrc =>
            dst.Lock(pdst =>
            {
                RGB getsrc(int x, int y)
                {
                    if ((x >= 0) && (y >= 0) && (x < w) && (y < h))
                        return psrc[y * w + x];
                    else if (mode == ConvolutionMode.Zero)
                        return new RGB();
                    else if (mode == ConvolutionMode.RepeatEdges)
                        return getsrc(x < 0 ? 0 : x >= w ? w - 1 : x, y < 0 ? 0 : y >= h ? h - 1 : y);
                    else
                        return getsrc(x % w, y % h);
                }

                Parallel.For(0, w, x =>
                {
                    for (int y = 0; y < h; ++y)
                    {
                        double rv = 0, gv = 0, bv = 0;
                        double rh = 0, gh = 0, bh = 0;

                        for (int i = 0; i < ksize; ++i)
                            for (int j = 0; j < ksize; ++j)
                            {
                                RGB color = getsrc(x + i - ksize / 2, y + j - ksize / 2);

                                rv += vkernel[i, j] * color.Rf;
                                gv += vkernel[i, j] * color.Gf;
                                bv += vkernel[i, j] * color.Bf;

                                rh += hkernel[i, j] * color.Rf;
                                gh += hkernel[i, j] * color.Gf;
                                bh += hkernel[i, j] * color.Bf;
                            }

                        pdst[y * w + x] = new RGB
                        (
                            Sqrt(rv * rv + rh * rh).Constrain(),
                            Sqrt(gv * gv + gh * gh).Constrain(),
                            Sqrt(bv * bv + bh * bh).Constrain()
                        );
                    }
                });
            }));

            return dst;
        }

        internal static void Lock(this Bitmap bmp, LockUtilization func)
        {
            BitmapData dat = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);

            func((RGB*)dat.Scan0);

            bmp.UnlockBits(dat);
        }
    }

    public sealed class GaussianBlur
    {
        private readonly int[] _α;
        private readonly int[] _r;
        private readonly int[] _g;
        private readonly int[] _b;
        private readonly int _w;
        private readonly int _h;

        private readonly ParallelOptions _pOptions = new ParallelOptions { MaxDegreeOfParallelism = 16 };


        public GaussianBlur(Bitmap image)
        {
            Rectangle rct = new Rectangle(0, 0, image.Width, image.Height);
            int[] source = new int[rct.Width * rct.Height];

            BitmapData bits = image.LockBits(rct, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            Marshal.Copy(bits.Scan0, source, 0, source.Length);

            image.UnlockBits(bits);

            _w = image.Width;
            _h = image.Height;
            _α = new int[_w * _h];
            _r = new int[_w * _h];
            _g = new int[_w * _h];
            _b = new int[_w * _h];

            Parallel.For(0, source.Length, _pOptions, i =>
            {
                _α[i] = (int)((source[i] & 0xff000000) >> 24);
                _r[i] = (source[i] & 0xff0000) >> 16;
                _g[i] = (source[i] & 0x00ff00) >> 8;
                _b[i] = (source[i] & 0x0000ff);
            });
        }

        public Bitmap Process(int radial)
        {
            int[] nα = new int[_w * _h];
            int[] nr = new int[_w * _h];
            int[] ng = new int[_w * _h];
            int[] nb = new int[_w * _h];
            int[] dest = new int[_w * _h];

            Parallel.Invoke(
                () => gaussBlur_4(_α, nα, radial),
                () => gaussBlur_4(_r, nr, radial),
                () => gaussBlur_4(_g, ng, radial),
                () => gaussBlur_4(_b, nb, radial)
            );
            Parallel.For(0, dest.Length, _pOptions, i =>
            {
                nα[i] = nα[i] > 255 ? 255 : nα[i] >= 0 ? nα[i] : 0;
                nr[i] = nr[i] > 255 ? 255 : nr[i] >= 0 ? nr[i] : 0;
                ng[i] = ng[i] > 255 ? 255 : ng[i] >= 0 ? ng[i] : 0;
                nb[i] = nb[i] > 255 ? 255 : nb[i] >= 0 ? nb[i] : 0;

                dest[i] = (int)((uint)(nα[i] << 24) |
                                (uint)(nr[i] << 16) |
                                (uint)(ng[i] << 8) |
                                (uint)nb[i]);
            });

            Bitmap image = new Bitmap(_w, _h);
            Rectangle rct = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData bits2 = image.LockBits(rct, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            Marshal.Copy(dest, 0, bits2.Scan0, dest.Length);

            image.UnlockBits(bits2);

            return image;
        }

        private void gaussBlur_4(int[] source, int[] dest, int r)
        {
            int[] bxs = boxesForGauss(r, 3);

            boxBlur_4(source, dest, _w, _h, (bxs[0] - 1) / 2);
            boxBlur_4(dest, source, _w, _h, (bxs[1] - 1) / 2);
            boxBlur_4(source, dest, _w, _h, (bxs[2] - 1) / 2);
        }

        private int[] boxesForGauss(int sigma, int n)
        {
            double wIdeal = Sqrt((12 * sigma * sigma / n) + 1);
            int wl = (int)Floor(wIdeal);

            if (wl % 2 == 0)
                --wl;

            int wu = wl + 2;

            double mIdeal = (12d * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4d * wl - 4);
            double m = Round(mIdeal);

            List<int> sizes = new List<int>();

            for (int i = 0; i < n; ++i)
                sizes.Add(i < m ? wl : wu);

            return sizes.ToArray();
        }

        private void boxBlur_4(int[] source, int[] dest, int w, int h, int r)
        {
            for (int i = 0; i < source.Length; ++i)
                dest[i] = source[i];

            boxBlurH_4(dest, source, w, h, r);
            boxBlurT_4(source, dest, w, h, r);
        }

        private void boxBlurH_4(int[] source, int[] dest, int w, int h, int r)
        {
            double iar = 1 / (r + r + 1d);

            Parallel.For(0, h, _pOptions, i =>
            {
                int ti = i * w;
                int li = ti;
                int ri = ti + r;
                int fv = source[ti];
                int lv = source[ti + w - 1];
                int val = (r + 1) * fv;

                for (int j = 0; j < r; ++j)
                    val += source[ti + j];

                for (int j = 0; j <= r; ++j)
                {
                    val += source[ri++] - fv;

                    dest[ti++] = (int)Round(val * iar);
                }

                for (int j = r + 1; j < w - r; ++j)
                {
                    val += source[ri++] - dest[li++];

                    dest[ti++] = (int)Round(val * iar);
                }

                for (int j = w - r; j < w; ++j)
                {
                    val += lv - source[li++];

                    dest[ti++] = (int)Round(val * iar);
                }
            });
        }

        private void boxBlurT_4(int[] source, int[] dest, int w, int h, int r)
        {
            double iar = (double)1 / (r + r + 1);

            Parallel.For(0, w, _pOptions, i =>
            {
                int ti = i;
                int li = ti;
                int ri = ti + r * w;
                int fv = source[ti];
                int lv = source[ti + w * (h - 1)];
                int val = (r + 1) * fv;

                for (int j = 0; j < r; ++j)
                    val += source[ti + j * w];

                for (int j = 0; j <= r; ++j)
                {
                    val += source[ri] - fv;
                    dest[ti] = (int)Round(val * iar);
                    ri += w;
                    ti += w;
                }

                for (int j = r + 1; j < h - r; ++j)
                {
                    val += source[ri] - source[li];
                    dest[ti] = (int)Round(val * iar);
                    li += w;
                    ri += w;
                    ti += w;
                }

                for (int j = h - r; j < h; ++j)
                {
                    val += lv - source[li];
                    dest[ti] = (int)Round(val * iar);
                    li += w;
                    ti += w;
                }
            });
        }
    }

    [Serializable, NativeCppClass, StructLayout(LayoutKind.Sequential)]
    public struct RGB
    {
        public byte R;
        public byte G;
        public byte B;


        public byte Gray => (byte)((R + G + B) / 3);

        public uint Raw
        {
            set
            {
                R = (byte)((value >> 16) & 0xff);
                G = (byte)((value >> 8) & 0xff);
                B = (byte)(value & 0xff);
            }
            get => ((uint)R << 16) |
                   ((uint)G << 8) | B;
        }
        
        public double Rf
        {
            set => R = (byte)(value.Constrain() * 255);
            get => R / 255.0;
        }

        public double Gf
        {
            set => G = (byte)(value.Constrain() * 255);
            get => G / 255.0;
        }

        public double Bf
        {
            set => B = (byte)(value.Constrain() * 255);
            get => B / 255.0;
        }

        public double Grayf => (Rf + Gf + Bf) / 3d;


        public RGB(uint argb)
            : this() => Raw = argb;

        public RGB(double v)
            : this(v, v, v)
        {
        }

        public RGB(double r, double g, double b)
            : this() => (Rf, Gf, Bf) = (r, g, b);
    }

    public enum ConvolutionMode
        : byte
    {
        Zero,
        RepeatEdges,
        Torus
    }
}
