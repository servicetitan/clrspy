using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace ClrSpy
{
    public static class CallStacks
    {
        public static IEnumerable<IEnumerable<object>> GetStackTrances(ClrRuntime runtime) =>
            runtime.Threads.Select(t => t.EnumerateStackTrace().Reverse());
    }
}
