using System.Collections.Generic;
using System.Linq;

#nullable disable
namespace ClrSpy
{
    public static class DictionaryDiffComparer
    {
        public static List<DiffEntry<TKey, TValue>> GetDiff<TKey, TValue>(
            IDictionary<TKey, TValue> prev,
            IDictionary<TKey, TValue> next)
        => prev.Keys.Concat(next.Keys).Distinct().Select(k =>
                new DiffEntry<TKey, TValue> {
                    Key = k,
                    PrevHasKey = prev.TryGetValue(k, out TValue prevValue),
                    NextHasKey = next.TryGetValue(k, out TValue nextValue),
                    PrevValue = prevValue,
                    NextValue = nextValue
                }).ToList();


    }

    public class DiffEntry<TKey, TValue>
    {
        public TKey Key { get; set; }
        public bool PrevHasKey { get; set; }
        public bool NextHasKey { get; set; }
        public TValue PrevValue { get; set; }
        public TValue NextValue { get; set; }
    }
}
