using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Runtime;

namespace ClrSpy
{
    public class TasksSpy
    {
        private readonly ClrRuntime runtime;
        private readonly ClrHeap heap;
        private readonly IClrDriver clrDriver;

        public IEnumerable<ThreadPoolItem> GetTasks()
        {
            var workItems = clrDriver.EnumerateManagedWorkItems();
            var timerTasks = clrDriver.EnumerateTimerTasks();
            return workItems.Concat(timerTasks)
                .Select(clrDriver.GetThreadPoolItem);
        }

        public TasksSpy(ClrRuntime runtime) {
            (this.runtime, heap) = (runtime, runtime.Heap);
            clrDriver = runtime.ClrInfo.Flavor == ClrFlavor.Core ? (IClrDriver)new NetCoreClrDriver(runtime) : new NetFrameworkClrDriver(runtime);
        }
    }

    public static class TasksExtensions
    {
        private static readonly Regex reAngleBrackets = new Regex(@"\+\<([^>]+)\>", RegexOptions.Compiled);

        private static string MakeReadableTaskName(string s) =>
            reAngleBrackets.Replace(s, ".$1.");

        public static void WriteGroupedTasks(this TextWriter w, IEnumerable<ThreadPoolItem> tasks)
        {
            foreach (var group in tasks.GroupBy(o => o.MethodName).OrderByDescending(g => g.Count()))
                Console.WriteLine($"{group.Count()}\t{MakeReadableTaskName(group.Key)}");
        }
    }
}
