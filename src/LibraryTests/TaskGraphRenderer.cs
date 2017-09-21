using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    static class TaskGraphRenderer
    {
        private static readonly List<string> COLORS = new List<string>();

        static TaskGraphRenderer()
        {
            foreach (var r in Enumerable.Range(4, 7).Select(v => v * 24))
            {
                foreach (var g in Enumerable.Range(4, 7).Select(v => v * 24))
                {
                    foreach (var b in Enumerable.Range(4, 7).Select(v => v * 24))
                    {
                        COLORS.Add($"#{r:X2}{g:X2}{b:X2}");
                    }
                }
            }
        }

        private static readonly string[] SHAPES = new[]
        {
            "box", "polygon", "oval", "circle", "egg", "triangle", "invtriangle", "diamond",
            "trapezium", "invtrapezium", "parallelogram", "house", "invhouse",
            "pentagon", "hexagon", "septagon", "octagon",
        };

        private static Dictionary<string, string> CreateRandomLookup(Random rand, IEnumerable<string> groups, IEnumerable<string> choices)
        {
            var choiceList = new List<string>(choices);
            var result = new Dictionary<string, string>();
            foreach (var group in groups)
            {
                if (choiceList.Count == 0)
                {
                    result[group] = null;
                    continue;
                }
                var choice = choiceList[rand.Next(choiceList.Count)];
                result[group] = choice;
                choiceList.Remove(choice);
            }
            return result;
        }

        private static KeyValuePair<string, string> Attribute(string key, string value) 
            => new KeyValuePair<string, string>(key, value);

        private static string AttributeList(params KeyValuePair<string, string>[] attributes)
        {
            var validAttributes = attributes
                .Where(a => a.Value != null)
                .Select(a => $"{a.Key}=\"{a.Value}\"")
                .ToArray();
            return validAttributes.Length > 0 ? " [" + string.Join(", ", validAttributes) + "]" : string.Empty;
        }

        public static void WriteDotFile(TextWriter w, List<TestTask> tasks, Random rand = null)
        {
            rand = rand ?? new Random();
            var queueTags = new HashSet<string>(from t in tasks select t.QueueTag);
            var groups = new HashSet<string>(from t in tasks select t.Group);
            var groupColors = CreateRandomLookup(rand, groups, COLORS);
            var queueTagShapes = CreateRandomLookup(rand, queueTags, SHAPES);

            w.WriteLine("digraph Tasks {");
            var nodeAttributes = AttributeList(
                Attribute("style", "filled"),
                Attribute("fontname", "Segoe UI"));
            w.WriteLine($"  node{nodeAttributes};");
            foreach (var t in tasks)
            {
                var attributes = AttributeList(
                    Attribute("color", groupColors[t.Group]),
                    Attribute("fillcolor", groupColors[t.Group]),
                    Attribute("shape", queueTagShapes[t.QueueTag]));
                w.WriteLine($"  {t.Label}{attributes};");
            }
            foreach (var t in tasks)
            {
                foreach (TestTask d in t.DependencyList)
                {
                    w.WriteLine($"  {d.Label} -> {t.Label};");
                }
            }
            w.WriteLine("}");
        }

        public static void RenderGraph(List<TestTask> tasks, string targetPngFile, Random rand = null)
        {
            var tmpDotFile = Path.GetTempFileName();
            using (var w = new StreamWriter(tmpDotFile, false, Encoding.ASCII))
            {
                WriteDotFile(w, tasks, rand);
            }
            var p = Process.Start(new ProcessStartInfo("dot", $"-Tpng \"-o{targetPngFile}\" \"{tmpDotFile}\"")
            {
                CreateNoWindow = false,
                UseShellExecute = true
            });
            p.WaitForExit();
            File.Delete(tmpDotFile);
        }

        public static void DisplayGraph(List<TestTask> tasks, Random rand = null)
        {
            var tmpFile = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(Path.GetRandomFileName(), ".png"));
            RenderGraph(tasks, tmpFile, rand);
            if (!File.Exists(tmpFile)) throw new FileNotFoundException("Generated PNG file not found", tmpFile);
            Process.Start(tmpFile);
        }
    }
}
