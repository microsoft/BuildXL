// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine;
using BuildXL.Utilities;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Engine
{
    public sealed class HistoricTableSizesTests
    {
        private const bool Debug = false;

        [Fact]
        public void TestHistoricDataEquals()
        {
            XAssert.AreNotEqual(default(HistoricDataPoint), null);
            XAssert.AreEqual(default(HistoricDataPoint), default(HistoricDataPoint));
            XAssert.AreEqual(default(HistoricDataPoint), new HistoricDataPoint(default(TableStats), default(TableStats), default(TableStats)));
            XAssert.AreNotEqual(default(HistoricDataPoint), new HistoricDataPoint(default(TableStats), default(TableStats), new TableStats(1, 1)));
            XAssert.AreNotEqual(
                new HistoricDataPoint(default(TableStats), new TableStats(1, 1), default(TableStats)),
                new HistoricDataPoint(default(TableStats), default(TableStats), new TableStats(1, 1)));
            XAssert.AreEqual(
                new HistoricDataPoint(default(TableStats), new TableStats(1, 2), new TableStats(3, 4)),
                new HistoricDataPoint(default(TableStats), new TableStats(1, 2), new TableStats(3, 4)));
        }

        [Theory]
        [MemberData(nameof(SerializationDeserializationData))]
        public void TestSerializationDeserialization(HistoricTableSizes historicData)
        {
            byte[] serializedBytes = Serialize(historicData);
            var reloadedData = Deserialize(serializedBytes);
            AssertEqual(historicData, reloadedData);
        }

        public static IEnumerable<object[]> SerializationDeserializationData()
        {
            var dataPoint1 = default(HistoricDataPoint);
            var dataPoint2 = new HistoricDataPoint(new TableStats(0, 1), new TableStats(2, 3), new TableStats(4, 5));

            yield return new object[] { new HistoricTableSizes(new HistoricDataPoint[0]) };
            yield return new object[]
            {
                new HistoricTableSizes(new[]
                                        {
                                            dataPoint1
                                        })
            };
            yield return new object[]
            {
                new HistoricTableSizes(new[]
                                        {
                                            dataPoint1,
                                            dataPoint2
                                        })
            };
            yield return new object[]
            {
                new HistoricTableSizes(new[]
                                        {
                                            dataPoint1,
                                            dataPoint2,
                                            dataPoint1
                                        })
            };
        }

        private static HistoricTableSizes Deserialize(byte[] serializedBytes)
        {
            using (var stream = new MemoryStream(serializedBytes))
            using (var reader = new BuildXLReader(debug: Debug, stream: stream, leaveOpen: false))
            {
                return HistoricTableSizes.Deserialize(reader);
            }
        }

        private static byte[] Serialize(HistoricTableSizes historicData)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BuildXLWriter(debug: Debug, stream: stream, leaveOpen: false, logStats: Debug))
            {
                historicData.Serialize(writer);
                return stream.ToArray();
            }
        }

        private static void AssertEqual(HistoricTableSizes expected, HistoricTableSizes actual)
        {
            XAssert.IsNotNull(expected);
            XAssert.IsNotNull(actual);
            XAssert.ArrayEqual(expected.ToArray(), actual.ToArray());
        }
    }
}
