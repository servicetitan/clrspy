using System;

namespace ClrSpy
{
    public static class Std
    {
        public static T Exchange<T>(ref T obj, T newValue)
        {
            var r = obj;
            obj = newValue;
            return r;
        }
    }

    public struct Foreground : IDisposable
    {
        ConsoleColor prevColor;

        public void Dispose()
        {
            Console.ForegroundColor = prevColor;
        }

        public Foreground(ConsoleColor newColor)
        {
            prevColor = Console.ForegroundColor;
            Console.ForegroundColor = newColor;
        }
    }
}
