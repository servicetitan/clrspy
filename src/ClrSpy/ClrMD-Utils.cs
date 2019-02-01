using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace ClrSpy
{
    public static class ClrMdUtils
    {
        public static ClrModule GetCorlib(ClrRuntime runtime) =>
            runtime.Modules.FirstOrDefault(module => {
                var name = module.AssemblyName.ToLower();
                return name.Contains("mscorlib.dll") || name.Contains("corelib.");
            }) ?? throw new InvalidOperationException("Impossible to find mscorlib.dll");

        private static readonly ConcurrentDictionary<ClrRuntime, CoreThreadPool> coreThreadPoolByRuntime = new ConcurrentDictionary<ClrRuntime, CoreThreadPool>();

        public static ClrThreadPool ThreadPool(this ClrRuntime runtime) =>
            (runtime.ClrInfo.Flavor == ClrFlavor.Core
                || runtime.ClrInfo.Flavor == ClrFlavor.Desktop) //!!!D
            ? coreThreadPoolByRuntime.GetOrAdd(runtime, _ => new CoreThreadPool(runtime))
            : runtime.ThreadPool;
    }
}
