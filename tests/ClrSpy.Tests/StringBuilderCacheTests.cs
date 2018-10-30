using System;
using Xunit;

namespace ClrSpy.Tests
{
    public class StringBuilderCacheTests
    {
        [Fact]
        public void ReturnSameStringBuilderAfterRecycle()
        {
            var sb1 = StringBuilderCache.Get();
            sb1.ToStringRecycle();
            var sb2 = StringBuilderCache.Get();
            sb2.ToStringRecycle();

            Assert.Same(sb1, sb2);
        }

        [Fact]
        public void ReturnNotSameStringBuilderWithoutRecycle()
        {
            var sb1 = StringBuilderCache.Get();
            var sb2 = StringBuilderCache.Get();

            StringBuilderCache.Recycle(sb1);
            StringBuilderCache.Recycle(sb2);

            Assert.NotSame(sb1, sb2);
        }
    }
}
