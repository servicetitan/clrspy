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
            public string Name { get; }
            public List<object> Objects { get; internal set;  }
            public int Weight => Objects.Count;
            public List<Node>? Children { get; internal set; }

            public Node(string name, List<object> objects) =>
                (Name, Objects) = (name, objects);
        }

        private static List<Node> MergeTrees(IEnumerable<Node> trees)
        {
            var r = new List<Node>();
            foreach (var node in trees) {
                Node? last = r.Count == 0 ? null : r.Last();
                if (last != null && last.Name == node.Name) {
                    last.Objects = last.Objects.Concat(node.Objects).ToList();
                    if (last.Children != null || node.Children != null) {
                        last.Children = MergeTrees((last.Children ?? new List<Node>()).Concat(node.Children ?? new List<Node>()));
                    }
                }
                else {
                    if (last?.Children != null && last.Children.Count > 1) {
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
                .Select(c => new KeyValuePair<string, object[]>(string.Join(".", c.Select(o => o.ToString())), c))
                .OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
            var tree = sorted.Select(c => {
                Node? node = null;
                for (int i = c.Length; --i >= 0;) {
                    node = new Node(c[i].ToString(), new List<object> { c[i] }) {
                        Children = node != null ? new List<Node> { node } : null,
                    };
                }
                return node;
            }).ToArray();
            return MergeTrees(tree);
        }

        public static void WriteTree(this TextWriter w, List<Node> tree)
        {
            w.WriteLine("Parallel Stacks:\n");
            WriteTree(w, tree, new List<bool>());
        }

        private static void WriteTree(this TextWriter w, List<Node> tree, List<bool> parentLines)
        {
            for (int i = 0; i < tree.Count; ++i) {
                var node = tree[i];
                bool isLast = i == tree.Count - 1;
                bool isSimpleChain = false;
                for (List<Node>? children; ; node = children[0]) {
                    foreach (var v in parentLines) {
                        w.Write(v ? "│ " : "  ");
                    }
                    if (parentLines.Count > 0) {
                        w.Write(isSimpleChain
                            ? (isLast ? " " : "│")
                            : isLast ? "└" : "├");
                    }
                    w.Write(node.Name);
                    children = node.Children;
                    if (!isSimpleChain && node.Weight > 1)
                        w.Write($" - {node.Weight} threads");
                    w.WriteLine();
                    if (children == null || children.Count != 1) {
                        if (children != null) {
                            var childLines = parentLines.ToList();
                            childLines.Add(!isLast);
                            w.WriteTree(children, childLines);
                        }
                        if (isSimpleChain) {
                            foreach (var v in parentLines)
                                w.Write(v ? "│ " : "  ");
                            w.WriteLine(isLast ? " " : "│");
                        }
                        break;
                    }
                    isSimpleChain = true;
                }
            }
        }
    }
}


