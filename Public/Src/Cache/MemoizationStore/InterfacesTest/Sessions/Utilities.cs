using System;
using System.IO;
using BuildXL.Utilities;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public static class Utilities
    {
        public static void TestSerializationRoundtrip<T>(T expected, Action<BuildXLWriter> serializer, Func<BuildXLReader, T> deserializer)
        {
            using (var ms = new MemoryStream())
            {
                using (var bw = new BuildXLWriter(false, ms, false, false))
                {
                    serializer(bw);
                    ms.Position = 0;

                    using (var reader = new BuildXLReader(false, ms, false))
                    {
                        var deserialized = deserializer(reader);
                        Assert.Equal(expected, deserialized);
                    }
                }
            }
        }
    }
}
