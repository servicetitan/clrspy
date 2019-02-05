using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace ClrSpy
{
    public class StackFrameWrapper
    {
        public ClrStackFrame Frame { get; }

        private string name;

        public override string ToString()
        {
            if (name == null) {
                var method = Frame.Method;
                if (method == null) {
                    name = Frame.ToString();
                }
                else {
                    var typename = method.Type.Name;
                    var lastDot = typename.LastIndexOf('.');
                    name = $"{(lastDot >= 0 ? typename.Substring(lastDot + 1) : typename)}.{method.Name}";
                }
            }
            return name;
        }

        public StackFrameWrapper(ClrStackFrame frame) => Frame = frame;
    }

    public static class CallStacks
    {
        public static IEnumerable<StackFrameWrapper[]> GetStackTraces(ClrRuntime runtime) =>
            runtime.Threads.Select(t => t.EnumerateStackTrace().Reverse().Select(o => new StackFrameWrapper(o)).ToArray())
                .Where(o => o.Length > 0);
    }
}
