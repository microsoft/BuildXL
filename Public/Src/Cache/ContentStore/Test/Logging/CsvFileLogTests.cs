// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using System.Collections.Generic;
using System.Text;

namespace ContentStoreTest.Logging
{
    public class CsvFileLogTests : TestBase
    {
        public CsvFileLogTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), NullLogger.Instance)
        {
        }

        private static readonly DateTime TestTimestamp = DateTime.UtcNow;
        private static readonly Severity TestSeverity = Severity.Always;
        private static readonly int TestThreadId = 52;
        private static readonly string TestMessage = "One,Two,Three";

        [Theory]
        [InlineData("123", "\"123\"")]     // columns are always enclosed in double quotes
        [InlineData("1\"3", "\"1\"\"3\"")] // a double quote is escaped with 2 double quotes
        [InlineData("1,3", "\"1,3\"")]     // a comma may appear in a message
        [InlineData("1\n3", "\"1\n3\"")]   // a new line may appear in a message
        public void TestRenderMessageColumn(string message, string expected)
        {
            string logFile = Path.Combine(Path.GetTempPath(), GetRandomFileName());
            using (var log = new CsvFileLog(logFile, new[] { CsvFileLog.ColumnType.Message }))
            {
                var actual = RenderMessage(log, message);
                actual.Should().BeEquivalentTo(expected);
            }
        }

        [Theory]
        [MemberData(nameof(TestRenderSchemaData), 3)]
        public void TestRenderSchema(CsvFileLog.ColumnType column, int columnIndex, int columnCount)
        {
            string logFile = Path.Combine(Path.GetTempPath(), GetRandomFileName());

            // schema:
            //   <empty>,...,<empty>,<column>,<empty>,...,<empty>
            // where
            //   - <column> is at position 'columnIndex'
            //   - total number of columns is 'columnCount'
            var schema =
                Repeat(CsvFileLog.ColumnType.EmptyString, columnIndex - 1)
                .Concat(new[] { column })
                .Concat(Repeat(CsvFileLog.ColumnType.EmptyString, columnCount - columnIndex - 1));

            using (var log = new CsvFileLog(logFile, schema))
            {
                var actual = RenderMessage(log, TestMessage);
                var expected = string.Join(",", schema
                    .Select(col => RenderColumn(log, col, TestMessage))
                    .Select(str => '"' + str + '"'));
                actual.Should().BeEquivalentTo(expected);
            }

            IEnumerable<CsvFileLog.ColumnType> Repeat(CsvFileLog.ColumnType col, int count)
            {
                return Enumerable.Range(0, Math.Max(0, count)).Select(_ => col);
            }
        }

        [Fact]
        public void TestOnLogFileProduced()
        {
            string logFile = Path.Combine(Path.GetTempPath(), GetRandomFileName());
            int maxFileSize = 1;  // max file size of 1 means every line will go to a new file
            int messageCount = 5; // number of messages to log
            var reportedLogFiles = new List<string>();
            using (var log = new CsvFileLog(logFile, new[] { CsvFileLog.ColumnType.Message }, maxFileSize: maxFileSize))
            {
                log.OnLogFileProduced += (path) => reportedLogFiles.Add(path);
                for (int i = 0; i < messageCount; i++)
                {
                    log.Write(TestTimestamp, TestThreadId, TestSeverity, i.ToString());
                }
            }

            // assert that a log file was reported for each line
            reportedLogFiles.Count.Should().Be(messageCount);

            // assert the content of each log file
            for (int i = 0; i < messageCount; i++)
            {
                var expected = $"\"{i}\"{Environment.NewLine}";
                File.ReadAllText(reportedLogFiles[i]).Should().BeEquivalentTo(expected);
            }
        }

        public static IEnumerable<object[]> TestRenderSchemaData(int maxNumberOfColumns)
        {
            foreach (int colCount in Enumerable.Range(1, maxNumberOfColumns))
            {
                foreach (int colIndex in Enumerable.Range(0, colCount))
                {
                    foreach (var colType in typeof(CsvFileLog.ColumnType).GetEnumValues())
                    {
                        yield return new object[] { (CsvFileLog.ColumnType)colType, colIndex, colCount };
                    }
                }
            }
        }

        private string RenderMessage(CsvFileLog log, string message)
        {
            var sb = new StringBuilder();
            log.RenderMessage(sb, TestTimestamp, TestThreadId, TestSeverity, message);
            return sb.ToString();
        }

        private string RenderColumn(CsvFileLog log, CsvFileLog.ColumnType col, string message)
        {
            return log.RenderColumn(col, TestTimestamp, TestThreadId, TestSeverity, message);
        }
    }
}
