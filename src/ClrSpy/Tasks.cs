using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Desktop;

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
        public static void WriteTasks(this TextWriter w, IEnumerable<ThreadPoolItem> tasks)
        {
            foreach (var t in tasks) {
                Console.WriteLine(t.MethodName);
            }
        }
    }
}
