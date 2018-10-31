// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class BinaryLogTests : XunitBuildXLTest
    {
        public BinaryLogTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void RoundTripLog()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();

            var pt = context.PathTable;

            string path1 = A("c", "a", "b", "c");
            var ap1 = AbsolutePath.Create(pt, path1);
            string path2 = A("c", "d", "c", "a");
            var ap2 = AbsolutePath.Create(pt, path2);

            string path3 = A("d", "a", "c", "a");
            var ap3 = AbsolutePath.Create(pt, path3);

            string path3Caps = A("D", "A", "c", "a");
            var ap3Caps = AbsolutePath.Create(pt, path3Caps);

            int lastTestCase0EventIteration = 0;
            int testCase0EventIteration = 0;
            int expectedReadCount = 0;
            Guid logId = Guid.NewGuid();
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryLogger writer = new BinaryLogger(ms, context, logId, lastStaticAbsolutePathIndex: ap2.Value.Value, closeStreamOnDispose: false))
                {
                    using (var eventScope = writer.StartEvent((uint)EventId.TestCase0, workerId: 0))
                    {
                        eventScope.Writer.Write(ap1);
                        eventScope.Writer.Write("test string");
                        eventScope.Writer.Write(ap3);
                        eventScope.Writer.Write(12345);
                        expectedReadCount++;
                        lastTestCase0EventIteration++;
                    }

                    using (var eventScope = writer.StartEvent((uint)EventId.TestCase0, workerId: 0))
                    {
                        eventScope.Writer.Write("test string 2");
                        eventScope.Writer.Write(true);
                        eventScope.Writer.Write(ap3);
                        expectedReadCount++;
                        lastTestCase0EventIteration++;
                    }
                }

                ms.Position = 0;

                using (BinaryLogReader reader = new BinaryLogReader(ms, context))
                {
                    XAssert.IsTrue(reader.LogId.HasValue);
                    XAssert.AreEqual(logId, reader.LogId.Value);
                    reader.RegisterHandler((uint)EventId.TestCase0, (eventId, workerId, timestamp, eventReader) =>
                        {
                            switch (testCase0EventIteration)
                            {
                                case 0:
                                    XAssert.AreEqual(ap1, eventReader.ReadAbsolutePath());
                                    XAssert.AreEqual("test string", eventReader.ReadString());
                                    XAssert.AreEqual(ap3, eventReader.ReadAbsolutePath());
                                    XAssert.AreEqual(12345, eventReader.ReadInt32());
                                    break;
                                case 1:
                                    XAssert.AreEqual("test string 2", eventReader.ReadString());
                                    XAssert.AreEqual(true, eventReader.ReadBoolean());
                                    XAssert.AreEqual(ap3, eventReader.ReadAbsolutePath());
                                    break;
                                default:
                                    XAssert.Fail("Event raised unexpected number of times.");
                                    break;
                            }

                            testCase0EventIteration++;
                        });

                    reader.RegisterHandler((uint)EventId.UnusedEvent, (eventId, workerId, timestamp, eventReader) =>
                    {
                        XAssert.Fail("This event should never be called.");
                    });

                    int readCount = 0;
                    BinaryLogReader.EventReadResult? readResult;
                    while ((readResult = reader.ReadEvent()) == BinaryLogReader.EventReadResult.Success)
                    {
                        XAssert.IsTrue(readCount < expectedReadCount);
                        readCount++;
                    }

                    XAssert.AreEqual(expectedReadCount, readCount);
                    XAssert.AreEqual(lastTestCase0EventIteration, testCase0EventIteration);
                    XAssert.AreEqual(BinaryLogReader.EventReadResult.EndOfStream, readResult);
                }
            }
        }

        private enum EventId
        {
            TestCase0 = 0,
            UnusedEvent = 1
        }
    }
}
