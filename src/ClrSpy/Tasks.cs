using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

#nullable enable
namespace ClrSpy
{
    public class TasksSpy
    {
        private readonly ClrRuntime runtime;
        private readonly ClrHeap heap;
        private readonly IClrDriver clrDriver;

        public IEnumerable<TaskInfo> GetTasks()
        {
            var workItems = clrDriver.EnumerateManagedWorkItems();
            var timerTasks = clrDriver.EnumerateTimerTasks();
            return workItems.Concat(timerTasks)
                .Select(clrDriver.GetTaskInfo)
                .Where(o => o != null);
        }

        public IEnumerable<TaskInfo> GetAllTasks(TextWriter errorWriter)
        {
            long count = 0, nextPrint = 1000000;
            foreach (var addr in heap.EnumerateObjectAddresses().Where(a => a != 0)) {
                var type = heap.GetObjectType(addr);
                if (!type.IsFree) {
                    if (clrDriver.IsTaskDescendant(type)) {
                        var taskInfo = clrDriver.GetTaskInfo(new ObjectInfo { Address = addr, Type = type });
                        if (taskInfo != null)
                            yield return taskInfo;
                    }
                    if (++count == nextPrint) {
                        errorWriter.Write($"\rEnumerated {count:n0} objects");
                        nextPrint = count + 1000000;
                    }
                }
            }
            errorWriter.WriteLine();
        }

        public TasksSpy(ClrRuntime runtime) {
            (this.runtime, heap) = (runtime, runtime.Heap);
            clrDriver = runtime.ClrInfo.Flavor == ClrFlavor.Core ? (IClrDriver)new NetCoreClrDriver(runtime) : new NetFrameworkClrDriver(runtime);
        }
    }

    public static class TasksExtensions
    {
        public static void WriteGroupedTasks(this TextWriter w, IEnumerable<TaskInfo> tasks)
        {
            foreach (var group in tasks.GroupBy(o => o.MethodName).OrderByDescending(g => g.Count()))
                w.WriteLine($"{group.Count()}\t{ClrMdUtils.MakeReadableTypeName(group.Key)}");
        }
    }
}
