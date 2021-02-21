using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;
using System;

using AutofocusAlgorithm;


List<(Scene, string)> scenes = new List<(Scene, string)>();

foreach (string dir in Directory.GetDirectories(Directory.GetCurrentDirectory()))
{
    int count = 0;
    Scene s = new Scene(128, 128);
    var files = (from f in Directory.GetFiles(dir)
                 orderby f ascending
                 where f.match(@"\.png", out _)
                 select new
                 {
                     Image = Image.FromFile(f) as Bitmap,
                     Index = count++
                 }).ToArray();

    foreach (var f in files)
        s.Add(new SceneLayer(1f - f.Index / (float)count, f.Image));

    scenes.Add((s, dir));
}

new LiveSceneViewer(scenes.ToArray()).ShowDialog();

//Console.WriteLine("Press any key to exit ...");
//Console.ReadKey(true);

return 0;
