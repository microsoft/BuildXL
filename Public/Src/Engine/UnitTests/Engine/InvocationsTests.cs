// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Utilities.Instrumentation.Common;
using Xunit;

namespace Test.BuildXL.Engine
{
    public class InvocationsTests
    {
        private LoggingContext LoggingContext = new LoggingContext("InvocationTests");

        [Fact]
        public void TestRecordingSingleFieAndReadBack()
        {
            var testFile = GetFile();
            var invocations = Invocations.CreateForTesting(5, testFile);
            Assert.False(File.Exists(testFile));
            var recordDate = new DateTime(2222, 2, 2, 2, 2, 2, DateTimeKind.Utc);

            invocations.RecordInvocation(LoggingContext, CreateTestInvocation(1, recordDate));

            Assert.True(File.Exists(testFile));
            var lines = File.ReadAllLines(testFile);
            Assert.Equal(1, lines.Length);
            Assert.Equal("0\ts-1\t2222-02-02T02:02:02.0000000Z\tPrimaryConfigFile\tLogsFolder\tEngineVersion\tEngineBinFolder\tEngineCommitId", lines[0]);

            var allEntires = invocations.GetInvocations(LoggingContext);
            Assert.Equal(1, allEntires.Count);

            var entry = allEntires.First();
            Assert.Equal(0, entry.LineVersion);
            Assert.Equal(recordDate, entry.BuildStartTimeUtc);
            Assert.Equal("PrimaryConfigFile", entry.PrimaryConfigFile);
            Assert.Equal("LogsFolder", entry.LogsFolder);
            Assert.Equal("EngineVersion", entry.EngineVersion);
            Assert.Equal("EngineBinFolder", entry.EngineBinFolder);
            Assert.Equal("EngineCommitId", entry.EngineCommitId);
        }

        [Fact]
        public void TestFillingAndOverFlow()
        {
            var testFile = GetFile();
            var invocations = Invocations.CreateForTesting(3, testFile);

            for (int i = 0; i < 12; i++)
            {
                invocations.RecordInvocation(LoggingContext, CreateTestInvocation(i));
            }

            Assert.True(File.Exists(testFile));

            var lines = File.ReadAllLines(testFile);
            Assert.Equal(3, lines.Length);
            Assert.True(lines[0].StartsWith("0\ts-9\t"));
            Assert.True(lines[1].StartsWith("0\ts-10\t"));
            Assert.True(lines[2].StartsWith("0\ts-11\t"));
        }

        [Fact]
        public void TestShrinking()
        {
            var testFile = GetFile();
            File.WriteAllLines(testFile, new [] {"a", "b", "c", "d", "e", "f" });

            var invocations = Invocations.CreateForTesting(4, testFile);

            invocations.RecordInvocation(LoggingContext, CreateTestInvocation(1));
            invocations.RecordInvocation(LoggingContext, CreateTestInvocation(2));

            Assert.True(File.Exists(testFile));

            var lines = File.ReadAllLines(testFile);
            Assert.Equal(4, lines.Length);
            Assert.True(lines[0].StartsWith("e"));
            Assert.True(lines[1].StartsWith("f"));
            Assert.True(lines[2].StartsWith("0\ts-1\t"));
            Assert.True(lines[3].StartsWith("0\ts-2\t"));
        }

        [Fact]
        public void TestGetLatestEmpty()
        {
            var testFile = GetFile();
            var invocations = Invocations.CreateForTesting(3, testFile);
            Assert.Equal(null, invocations.GetLastInvocation(LoggingContext));
        }

        [Fact]
        public void TestGetLatestSingle()
        {
            var testFile = GetFile();
            var invocations = Invocations.CreateForTesting(3, testFile);
            invocations.RecordInvocation(LoggingContext, CreateTestInvocation(1));

            Assert.Equal("s-1", invocations.GetLastInvocation(LoggingContext).Value.SessionId);
        }

        [Fact]
        public void TestGetLatestOverflow()
        {
            var testFile = GetFile();
            var invocations = Invocations.CreateForTesting(3, testFile);
            for (int i = 0; i < 10; i++)
            {
                invocations.RecordInvocation(LoggingContext, CreateTestInvocation(i));
            }

            Assert.Equal("s-9", invocations.GetLastInvocation(LoggingContext).Value.SessionId);
        }


        [Fact]
        public void TestGetLatestInFaceOfErrors()
        {
            var testFile = GetFile();
            
            File.WriteAllLines(testFile, new []
                                         {
                                             "error",
                                             "0\ts-1\tBadTime\tConfig\tLogsFolder\tEngineVersion\tEngineBin\tEngineCommitId",
                                             "", // empty line
                                             "0\ts-2\t2018-08-15T19:35:50.1539252Z\tConfig\tEngineVersion\tOneColLess",
                                             "#\ts-x", // non numeric version number
                                             "0\ts-3\t2018-08-15T19:35:50.1539252Z\tConfig\tLogsFolder\tEngineVersion\tEngineBin\tEngineCommitId\tExtraCol",
                                             CreateTestInvocation(4).ToTsvLine(),
                                             CreateTestInvocation(5).ToTsvLine(),
                                             "99\ts-6\t2018-08-15T19:35:50.1539252Z\tConfig\tLogsFolder\tEngineVersion\tEngineBin\tEngineCommitId",
                                         });

            var invocations = Invocations.CreateForTesting(3, testFile);
            // Last 
            Assert.Equal("s-5", invocations.GetLastInvocation(LoggingContext).Value.SessionId);

            Assert.Equal(2, invocations.GetInvocations(LoggingContext).Count);
            Assert.Equal("s-4", invocations.GetInvocations(LoggingContext)[0].SessionId);
            Assert.Equal("s-5", invocations.GetInvocations(LoggingContext)[1].SessionId);
        }

        private Invocations.Invocation CreateTestInvocation(int sessionId, DateTime? dateTime = null)
        {
            return new Invocations.Invocation(
                "s-" + sessionId,
                dateTime ?? DateTime.UtcNow,
                "PrimaryConfigFile",
                "LogsFolder",
                "EngineVersion",
                "EngineBinFolder",
                "EngineCommitId");
        }

        private string GetFile()
        {
            return Path.Combine(Environment.GetEnvironmentVariable("TEMP"), Guid.NewGuid().ToString("D"));
        }
    }
}
