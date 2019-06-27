// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Logging;
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
            using (var log = new CsvFileLog(logFile, new[] { CsvFileLog.ColumnKind.Message }))
            {
                var actual = RenderMessage(log, message);
                actual.Should().BeEquivalentTo(expected);
            }
        }

        [Theory]
        [MemberData(nameof(TestRenderSchemaData), 3)]
        public void TestRenderSchema(CsvFileLog.ColumnKind column, int columnIndex, int columnCount)
        {
            string logFile = Path.Combine(Path.GetTempPath(), GetRandomFileName());

            // schema:
            //   <empty>,...,<empty>,<column>,<empty>,...,<empty>
            // where
            //   - <column> is at position 'columnIndex'
            //   - total number of columns is 'columnCount'
            var schema =
                Repeat(CsvFileLog.ColumnKind.Empty, columnIndex - 1)
                .Concat(new[] { column })
                .Concat(Repeat(CsvFileLog.ColumnKind.Empty, columnCount - columnIndex - 1));

            using (var log = new CsvFileLog(logFile, schema, serviceName: "CsvFileLogTests"))
            {
                var actual = RenderMessage(log, TestMessage);
                var expected = string.Join(",", schema
                    .Select(col => RenderColumn(log, col, TestMessage))
                    .Select(str => '"' + str + '"'));
                actual.Should().BeEquivalentTo(expected);
            }

            IEnumerable<CsvFileLog.ColumnKind> Repeat(CsvFileLog.ColumnKind col, int count)
            {
                return Enumerable.Range(0, Math.Max(0, count)).Select(_ => col);
            }
        }

        public static IEnumerable<object[]> TestRenderSchemaData(int maxNumberOfColumns)
        {
            foreach (int colCount in Enumerable.Range(1, maxNumberOfColumns))
            {
                foreach (int colIndex in Enumerable.Range(0, colCount))
                {
                    foreach (var colType in typeof(CsvFileLog.ColumnKind).GetEnumValues())
                    {
                        yield return new object[] { (CsvFileLog.ColumnKind)colType, colIndex, colCount };
                    }
                }
            }
        }

        [Fact]
        public void TestOnLogFileProduced()
        {
            string logFile = Path.Combine(Path.GetTempPath(), GetRandomFileName());
            int maxFileSize = 1;  // max file size of 1 means every line will go to a new file
            int messageCount = 5; // number of messages to log
            var reportedLogFiles = new List<string>();
            using (var log = new CsvFileLog(logFile, new[] { CsvFileLog.ColumnKind.Message }, maxFileSize: maxFileSize))
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

        [Theory]
        [MemberData(nameof(TestParseTableSchemaData))]
        public void TestParseTableSchema(string schema, CsvFileLog.ColumnKind[] expected)
        {
            var actual = CsvFileLog.ParseTableSchema(schema);
            actual.Should().BeEquivalentTo(expected);
        }

        public static IEnumerable<object[]> TestParseTableSchemaData()
        {
            // some manual tests

            yield return new object[] { "",                   new CsvFileLog.ColumnKind[0] };
            yield return new object[] { "BuildId",            new[] { CsvFileLog.ColumnKind.BuildId } };
            yield return new object[] { "BuildId:string",     new[] { CsvFileLog.ColumnKind.BuildId } };
            yield return new object[] { "buildid:bogus_type", new[] { CsvFileLog.ColumnKind.BuildId } };
            yield return new object[] { "BOGUS",              new[] { CsvFileLog.ColumnKind.Empty } };
            yield return new object[] { "BOGUS: type1:type2", new[] { CsvFileLog.ColumnKind.Empty } };

            yield return new object[] { "env_os,env_osVer",             new[] { CsvFileLog.ColumnKind.env_os, CsvFileLog.ColumnKind.env_osVer } };
            yield return new object[] { "Env_OS, ENV_OsVer",            new[] { CsvFileLog.ColumnKind.env_os, CsvFileLog.ColumnKind.env_osVer } };
            yield return new object[] { "env_os:t1:t2, env_osVer : t1", new[] { CsvFileLog.ColumnKind.env_os, CsvFileLog.ColumnKind.env_osVer } };
            yield return new object[] { "env_os:t1:t2, BOGUS : t1",     new[] { CsvFileLog.ColumnKind.env_os, CsvFileLog.ColumnKind.Empty } };

            // some systematic tests

            var colNames = Enum.GetNames(typeof(CsvFileLog.ColumnKind));
            foreach (var colName in colNames)
            {
                var colKind = ToColumnKind(colName);

                // one column
                yield return new object[] { $"{colName}", new[] { colKind } };

                // one column with type
                yield return new object[] { $"{colName}:str", new[] { colKind } };

                // two columns
                yield return new object[] { $"{colName},{colName}", new[] { colKind, colKind } };
                yield return new object[] { $" {colName} , {colName}", new[] { colKind, colKind } };

                // two columns with type(s)
                yield return new object[] { $"{colName}:t1,{colName}:t1:t2", new[] { colKind, colKind } };
                yield return new object[] { $" {colName} : t1, {colName} : t1 : t2 ", new[] { colKind, colKind } };

                // with non-existent columns
                yield return new object[] { $"{colName},BOGUS", new[] { colKind, CsvFileLog.ColumnKind.Empty } };
                yield return new object[] { $"BOGUS,{colName}", new[] { CsvFileLog.ColumnKind.Empty, colKind } };
                yield return new object[] { $"BOGUS,{colName},BOGUS", new[] { CsvFileLog.ColumnKind.Empty, colKind, CsvFileLog.ColumnKind.Empty } };
            }

            // all columns joined
            var allColumnKinds = colNames.Select(ToColumnKind).Cast<object>().ToArray();
            yield return new object[] { string.Join(",", colNames), allColumnKinds };
            yield return new object[] { string.Join(", ", colNames.Select(cn => $"{cn}:string")), allColumnKinds };

            CsvFileLog.ColumnKind ToColumnKind(string name)
            {
                return (CsvFileLog.ColumnKind)Enum.Parse(typeof(CsvFileLog.ColumnKind), name);
            }
        }

        private string RenderMessage(CsvFileLog log, string message)
        {
            var sb = new StringBuilder();
            log.RenderMessage(sb, TestTimestamp, TestThreadId, TestSeverity, message);
            return sb.ToString();
        }

        private string RenderColumn(CsvFileLog log, CsvFileLog.ColumnKind col, string message)
        {
            return log.RenderColumn(col, TestTimestamp, TestThreadId, TestSeverity, message);
        }
    }
}
