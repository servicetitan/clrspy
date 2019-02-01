using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace ClrSpy
{
    public static class Tree
    {
        public class Node
        {
            public string Name { get; internal set; }
            public List<object> Objects { get; internal set; }
            public int Weight => Objects.Count;
            public List<Node> Children { get; internal set; }
        }

        private static List<Node> MergeTrees(IEnumerable<Node> trees)
        {
            var r = new List<Node>();
            foreach (var node in trees) {
                Node last = r.Count == 0 ? null : r.Last();
                if (last?.Name == node.Name) {
                    last.Objects = last.Objects.Concat(node.Objects).ToList();
                    last.Children = last.Children.Concat(node.Children).ToList();
                }
                else {
                    if (last?.Children.Count > 1) {
                        last.Children = MergeTrees(last.Children);
                    }
                    r.Add(node);
                }
            }
            return r.OrderByDescending(o => o.Weight).ToList();
        }

        public static List<Node> MergeChains(IEnumerable<IEnumerable<object>> chains)
        {
            var sorted = chains.Select(c => c.ToArray())
                .Select(c => KeyValuePair.Create(string.Join(".", c.Select(o => o.ToString())), c))
                .OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
            var tree = sorted.Select(c => {
                Node node = null;
                for (int i = c.Length; --i >= 0;) {
                    node = new Node {
                        Name = c[i].ToString(),
                        Objects = new List<object> { c[i] },
                        Children = node != null ? new List<Node> { node } : null,
                    };
                }
                return node;
            }).ToArray();
            return MergeTrees(tree);
        }

        public static void WriteTree(this TextWriter w, List<Node> tree, List<bool> parentLines = null)
        {
            for (int i = 0; i < tree.Count; ++i) {
                var node = tree[i];
                var isLast = i == tree.Count - 1;
                if (parentLines != null) {
                    foreach (var v in parentLines)
                        Console.Write(v ? "│ " : "  ");
                }
                Console.Write(isLast ? "┕" : "├");
                if (node.Weight > 1)
                    Console.Write($"{node.Weight} ");
                if (node.Children != null) {
                    var childLines = parentLines.ToList();
                    childLines.Add(!isLast);
                    w.WriteTree(node.Children, childLines);
                }
            }
        }
    }
}


