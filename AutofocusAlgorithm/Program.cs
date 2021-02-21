using System.Collections.Generic;
using System.Threading.Tasks;
using System.Drawing;
using System.Linq;
using System.Text;
using System.IO;
using System;

namespace AutofocusAlgorithm
{
    using static Math;
    using static Util;


    public static class Program
    {
        public static int Main(string[] argv)
        {
            List<(Scene, string)> scenes = new List<(Scene, string)>();

            foreach (string dir in Directory.GetDirectories(Directory.GetCurrentDirectory()))
            {
                int cnt = 0;
                Scene s = new Scene(128, 128);
                var files = from f in Directory.GetFiles(dir)
                            orderby f ascending
                            where f.match(@"\.png", out _)
                            select (Image.FromFile(f) as Bitmap, cnt++);

                foreach ((Bitmap, int) f in files)
                    s.Add(new SceneLayer(1f - f.Item2 / (float)cnt, f.Item1));

                scenes.Add((s, dir));
            }
            
            new LiveSceneViewer(scenes.ToArray()).ShowDialog();

            //Console.WriteLine("Press any key to exit ...");
            //Console.ReadKey(true);

            return 0;
        }

       
    }
}
