using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace LENet
{
    internal static class Utils
    {
        public static List<T> MakeList<T>(uint count) where T : new()
        {
            var result = new List<T>((int)count);
            for(var i = 0; i < count; i++)
            {
                result.Add(new T());
            }
            return result;
        }

        public static T[] MakeArray<T>(uint count) where T : new()
        {
            var result = new T[(int)count];
            for (var i = 0; i < count; i++)
            {
                result[i] = new T();
            }
            return result;
        }
    }
}
