using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClrSpy
{
    public static class Util
    {
        public static IEnumerable<T> Generate<T>(Func<T> generator)
        {
            while (true)
                yield return generator();
        }

        public static IEnumerable<string> ReadAllLines(this TextReader reader) =>
            Generate(reader.ReadLine).TakeWhile(o => o != null);
    }
}
