using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    class TestTaskFactory
    {
        public static List<TestTask> CreateMeshedCascade(Random rand, int count, int levels, int minDeps, int maxDeps, params string[] queueTags)
        {
            var tasks = new List<TestTask>();
            var groups = Enumerable.Range(0, levels).Select(i => new List<TestTask>()).ToArray();
            for (int i = 0; i < count; i++)
            {
                var queueTag = queueTags[i % queueTags.Length];
                var groupIndex = i % groups.Length;
                var group = groups[groupIndex];
                var t = new TestTask($"T{i:000}", queueTag, groupIndex.ToString());
                group.Add(t);
                tasks.Add(t);
            }
            for (int i = 0; i < levels - 1; i++)
            {
                foreach (var t in groups[i])
                {
                    var deps = rand.Next(minDeps, maxDeps + 1);
                    for (int j = 0; j < deps; j++)
                    {
                        var targetGroup = groups[rand.Next(i + 1, levels)];
                        var dependency = targetGroup[rand.Next(targetGroup.Count)];
                        t.AddDependency(dependency);
                    }
                }
            }
            return tasks;
        }

        public static IEnumerable<TestTask> TasksWithResponsibilities(List<TestTask> tasks) 
            => tasks.Where(t => TaskHelper.Responsibilities(t, tasks).Any());

        public static IEnumerable<TestTask> InnerTasks(List<TestTask> tasks)
            => TasksWithResponsibilities(tasks).Where(t => t.HasDependencies);
    }
}
