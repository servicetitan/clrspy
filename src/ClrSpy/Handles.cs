using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Runtime;

#nullable enable
namespace ClrSpy
{
    public class HandleInfo
    {
        public ClrHandle? ClrHandle;
    }

    public class HandleSpy
    {
        private readonly ClrRuntime runtime;
        private readonly ClrHeap heap;
        private readonly IClrDriver clrDriver;


        public IEnumerable<HandleInfo> GetAllHandles()
        {
            return runtime.EnumerateHandles().Select(h => new HandleInfo { ClrHandle = h });
        }

        public HandleSpy(ClrRuntime runtime) {
            (this.runtime, heap) = (runtime, runtime.Heap);
            clrDriver = runtime.ClrInfo.Flavor == ClrFlavor.Core ? (IClrDriver)new NetCoreClrDriver(runtime) : new NetFrameworkClrDriver(runtime);
        }
    }

    public static class HandleExtensions
    {
        public static void WriteGroupedHandles(this TextWriter w, IEnumerable<HandleInfo> handles)
        {
            w.WriteLine("Handles:\n");
            foreach (var g in handles.GroupBy(h => h.ClrHandle?.Type.Name ?? "").OrderByDescending(g => g.Count())) {
                w.WriteLine($"{g.Count()}\t{ClrMdUtils.MakeReadableTypeName(g.Key)}");
            }
        }
    }
}
