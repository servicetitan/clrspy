using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClrSpy
{
    public class StackFrameWrapper
    {
        public ClrStackFrame Frame { get; }

        private string? name;

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
        private static IEnumerable<IEnumerable<object>> StacksFromJson(string json) =>
            JsonConvert.DeserializeObject<string[][]>(json);

        public static IEnumerable<IEnumerable<object>> QueryFromHosts(string target, IEnumerable<string> hosts, string login, string password) =>
            hosts.AsParallel()
                .SelectMany(host => StacksFromJson(Remote.ExecuteCommand(host, login, password, $"clrspy stacks --json {target}")));

        public static IEnumerable<IEnumerable<object>> ReadJsons(TextReader reader) =>
            Util.ReadAllLines(reader).SelectMany(json => StacksFromJson(json) ?? Array.Empty<string[]>());

        public static void WriteStacks(this TextWriter w, IEnumerable<StackFrameWrapper[]> stacks, bool printAsJson)
        {
            if (printAsJson) {
                var ar = stacks.Select(st => st.Select(f => f.ToString()).ToArray()).ToArray();
                Console.WriteLine(JsonConvert.SerializeObject(ar));
            }
            else {
                foreach (var st in stacks) {
                    foreach (var frame in st)
                        w.WriteLine(frame.ToString());
                    w.WriteLine();
                }
            }
        }

        public static IEnumerable<StackFrameWrapper[]> GetStackTraces(ClrRuntime runtime) =>
        runtime.Threads.Select(t => t.EnumerateStackTrace().Reverse().Select(o => new StackFrameWrapper(o)).ToArray())
            .Where(o => o.Length > 0);
    }
}
