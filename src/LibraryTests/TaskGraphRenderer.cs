using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mastersign.Tasks.Test.Monitors;
using static Mastersign.Tasks.Test.Monitors.EventRecordPredicates;
using System.Globalization;

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

        private static readonly Dictionary<TaskState, string> TASK_STATE_COLORS =
            new Dictionary<TaskState, string> {
                {TaskState.Waiting, "#D0E8FF"},
                {TaskState.Obsolete, "#908070"},
                {TaskState.InProgress, "#FFE020"},
                {TaskState.CleaningUp, "#80FFFF"},
                {TaskState.Succeeded, "#80FF80"},
                {TaskState.Failed, "#FF6040"},
                {TaskState.Canceled, "#C040C0"},
            };

        private static Dictionary<T, string> CreateRandomLookup<T>(IEnumerable<T> groups, IEnumerable<string> choices, Random rand = null)
        {
            var choiceList = new List<string>(choices);
            var result = new Dictionary<T, string>();
            rand = rand ?? new Random();
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

        public interface IAttributeValueSelector
        {
            string SelectAttributeValue(string queueTag, string group, TaskState state);
        }

        public abstract class RandomValueSelector<T> : IAttributeValueSelector
        {
            protected Dictionary<T, string> Lookup { get; }

            protected RandomValueSelector(IEnumerable<T> propertyValues, IEnumerable<string> options, Random rand = null)
            {
                var propertyValueSet = new HashSet<T>(propertyValues);
                Lookup = CreateRandomLookup(propertyValueSet, options, rand);
            }

            public abstract string SelectAttributeValue(string queueTag, string group, TaskState state);
        }

        public class ByQueueSelector : RandomValueSelector<string>
        {
            public ByQueueSelector(IEnumerable<ITask> tasks, IEnumerable<string> options, Random rand = null)
                : base(from t in tasks select t.QueueTag, options, rand)
            { }

            public override string SelectAttributeValue(string queueTag, string group, TaskState state)
                => Lookup[queueTag];
        }

        public class ByGroupSelector : RandomValueSelector<string>
        {
            public ByGroupSelector(IEnumerable<TestTask> tasks, IEnumerable<string> options, Random rand = null)
                : base(from t in tasks select t.Group, options, rand)
            { }

            public override string SelectAttributeValue(string queueTag, string group, TaskState state)
                => Lookup[group];
        }

        public class ByTaskStateSelector : IAttributeValueSelector
        {
            private Dictionary<TaskState, string> Lookup { get; }

            public ByTaskStateSelector(Dictionary<TaskState, string> lookup)
            {
                Lookup = lookup;
            }

            public string SelectAttributeValue(string queueTag, string group, TaskState state)
                => Lookup[state];
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

        public static void WriteDotFile(TextWriter w, List<TestTask> tasks,
            Dictionary<TestTask, TaskState> taskStates = null,
            IAttributeValueSelector colorSelector = null,
            IAttributeValueSelector shapeSelector = null)
        {
            colorSelector = colorSelector ?? new ByTaskStateSelector(TASK_STATE_COLORS);
            shapeSelector = shapeSelector ?? new ByQueueSelector(tasks, SHAPES, new Random(0));
            w.WriteLine("digraph Tasks {");
            var nodeAttributes = AttributeList(
                Attribute("style", "filled"),
                Attribute("fontname", "Segoe UI"));
            w.WriteLine($"  node{nodeAttributes};");
            foreach (var t in tasks)
            {
                var taskState = taskStates != null ? taskStates[t] : TaskState.Waiting;
                var color = colorSelector.SelectAttributeValue(t.QueueTag, t.Group, taskState);
                var shape = shapeSelector.SelectAttributeValue(t.QueueTag, t.Group, taskState);
                var attributes = AttributeList(
                    Attribute("color", color),
                    Attribute("fillcolor", color),
                    Attribute("shape", shape));
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

        public static void RenderGraph(string targetPngFile,
            List<TestTask> tasks, Dictionary<TestTask, TaskState> taskStates = null,
            IAttributeValueSelector colorSelector = null,
            IAttributeValueSelector shapeSelector = null)
        {
            var tmpDotFile = Path.GetTempFileName();
            using (var w = new StreamWriter(tmpDotFile, false, Encoding.ASCII))
            {
                WriteDotFile(w, tasks, taskStates, colorSelector, shapeSelector);
            }
            var p = Process.Start(new ProcessStartInfo("dot", $"-Tpng \"-o{targetPngFile}\" \"{tmpDotFile}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            p.WaitForExit();
            File.Delete(tmpDotFile);
        }

        public static void DisplayGraph(List<TestTask> tasks,
            Dictionary<TestTask, TaskState> taskStates = null,
            IAttributeValueSelector colorSelector = null,
            IAttributeValueSelector shapeSelector = null)
        {
            var tmpFile = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(Path.GetRandomFileName(), ".png"));
            RenderGraph(tmpFile, tasks, taskStates, colorSelector, shapeSelector);
            if (!File.Exists(tmpFile)) throw new FileNotFoundException("Generated PNG file not found", tmpFile);
            Process.Start(tmpFile);
        }

        private static IEnumerable<Dictionary<TestTask, TaskState>> RebuildTaskState(List<TestTask> tasks, TaskGraphMonitor tgMon)
        {
            var taskStates = tasks.ToDictionary(t => t, t => TaskState.Waiting);
            foreach (var er in tgMon.History)
            {
                var ea = er.EventArgs as TaskEventArgs;
                if (ea != null)
                {
                    taskStates[(TestTask)ea.Task] = ea.State;
                    yield return taskStates;
                }
            }
        }

        public enum VideoFormat
        {
            AviMjpeg,
            Gif
        }

        public static void RenderTaskGraphAnimation(List<TestTask> tasks, TaskGraphMonitor tgMon,
            string outputFile, VideoFormat format = VideoFormat.AviMjpeg,
            int maxWidth = 1280, float fps = 2)
        {
            var taskStateGenerations = RebuildTaskState(tasks, tgMon);
            var colorSelector = new ByTaskStateSelector(TASK_STATE_COLORS);
            var shapeSelector = new ByQueueSelector(tasks, SHAPES, new Random(0));

            var tmpDir = Path.Combine(Path.GetDirectoryName(outputFile), Path.GetFileNameWithoutExtension(outputFile));
            Directory.CreateDirectory(tmpDir);

            var i = 0;
            foreach (var taskStates in taskStateGenerations)
            {
                var path = Path.Combine(tmpDir, $"{i:0000}.png");
                RenderGraph(path, tasks, taskStates, colorSelector, shapeSelector);
                i++;
            }

            string fileExt;
            string codec;
            switch (format)
            {
                case VideoFormat.Gif:
                    fileExt = ".gif";
                    codec = "";
                    break;
                case VideoFormat.AviMjpeg:
                default:
                    fileExt = ".avi";
                    codec = "-vcodec mjpeg -huffman optimal -q:v 3";
                    break;
            }
            outputFile = Path.ChangeExtension(outputFile, fileExt);
            var c = CultureInfo.InvariantCulture;
            var arguments = $"-f image2 -y -r {fps.ToString(c)} -i \"{tmpDir}\\%04d.png\" -vf scale={maxWidth.ToString(c)}:-1 {codec} \"{outputFile}\"";
            var p = Process.Start(new ProcessStartInfo("ffmpeg", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            p.WaitForExit();

            Directory.Delete(tmpDir, true);
        }
    }
}
