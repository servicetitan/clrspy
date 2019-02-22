using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

// This file containes implementation of ThreadPool WorkItem enumerator for .NET Core
// May be removed when ClrMD will implement it.

namespace ClrSpy
{
    internal class CoreManagedWorkItem : ManagedWorkItem
    {
        public override ulong Object { get; }
        public override ClrType Type { get; }

        public CoreManagedWorkItem(ObjectInfo oi)
        {
            Object = oi.Address;
            Type = oi.Type;
        }
    }

    public class CoreThreadPool : ClrThreadPool
    {
        private readonly ClrRuntime runtime;
        private readonly ClrHeap heap;
        private readonly ClrAppDomain domain;
        private readonly IClrDriver driver;

        public override int TotalThreads { get; }
        public override int RunningThreads { get; }
        public override int IdleThreads { get; }
        public override int MinThreads { get; }
        public override int MaxThreads { get; }
        public override int MinCompletionPorts { get; }
        public override int MaxCompletionPorts { get; }
        public override int CpuUtilization { get; }
        public override int FreeCompletionPortCount { get; }
        public override int MaxFreeCompletionPorts { get; }

        public override IEnumerable<NativeWorkItem> EnumerateNativeWorkItems() => runtime.ThreadPool.EnumerateNativeWorkItems();

        public override IEnumerable<ManagedWorkItem> EnumerateManagedWorkItems() =>
            driver.EnumerateManagedWorkItems().Select(oi => new CoreManagedWorkItem(oi));

        public CoreThreadPool(ClrRuntime runtime) {
            (this.runtime, heap, domain) = (runtime, runtime.Heap, runtime.AppDomains[0]);
            driver = new NetCoreClrDriver(runtime);

            var tp = runtime.ThreadPool;

            TotalThreads = tp.TotalThreads;
            RunningThreads = tp.RunningThreads;
            IdleThreads = tp.IdleThreads;
            MinThreads = tp.MinThreads;
            MaxThreads = tp.MaxThreads;
            MinCompletionPorts = tp.MinCompletionPorts;
            MaxCompletionPorts = tp.MaxCompletionPorts;
            CpuUtilization = tp.CpuUtilization;
            FreeCompletionPortCount = tp.FreeCompletionPortCount;
            MaxFreeCompletionPorts = tp.MaxFreeCompletionPorts;
        }
    }
}
