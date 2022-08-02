using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using ContentStoreTest.Test;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit.Abstractions;

namespace BuildXL.Cache.Logging
{
    public partial class KustoStructuredLogger
    {
        public static ILogger CreateLogger(string database, string table, ITestOutputHelper output)
        {
#if NETCOREAPP
            string connectionString = Environment.GetEnvironmentVariable("KustoStorageConnectionString");
            if (connectionString != null)
            {
                return new KustoStructuredLogger(output, connectionString, database: database, table: table, ((Logger)TestGlobal.Logger).GetLog<ILog>().ToArray());
            }
#endif
            return TestGlobal.Logger;
        }

        public static void SetTestName(string name)
        {
#if NETCOREAPP
            if (_logger != null)
            {
                _logger._output.WriteLine($"Setting test name to: {name}");
                _logger.Debug($"Setting test name to: {name}");
                _logger.TableUpdateQueue.Enqueue(() =>
                {
                    _logger._table.TestName.Column.DefaultValue = name;
                });
            }
#endif
        }
    }

#if NETCOREAPP
    public partial class KustoStructuredLogger : Logger, IStructuredLogger
    {
        private NagleQueue<Action> TableUpdateQueue { get; }
        private Table _table = CreateTable();

        private StreamWriter _sb = new StreamWriter(new MemoryStream());
        private ITestOutputHelper _output;
        private static readonly Regex EscapeRegex = new Regex("[\\n\\r\\t\\\\]");

        private static KustoStructuredLogger _logger;

        public KustoStructuredLogger(ITestOutputHelper output, string connectionString, string database, string table, params ILog[] logs)
            : base(logs)
        {
            _output = output;
            _logger = this;
            var credentials = new AzureBlobStorageCredentials(connectionString);
            var client = credentials.CreateCloudBlobClient();
            var testStartTime = (DateTime)_table.TestStartTime.DefaultValue;
            output.WriteLine($"Test start time: {testStartTime:o}");
            var blob = client.GetContainerReference(database).GetAppendBlobReference($"{table}/{testStartTime:yyyy/MM/dd/HHmm}/log.tsv");

            bool created = false;

            TableUpdateQueue = NagleQueue<Action>.CreateUnstarted(1, TimeSpan.FromSeconds(5), 100000);

            TableUpdateQueue.Start(async batch =>
            {
                if (!created)
                {
                    created = true;
                    await blob.CreateOrReplaceAsync();

                    var schema = new StringBuilder();
                    foreach (DataColumn column in _table.DataTable.Columns)
                    {
                        schema.Append($"{column.ColumnName}:{column.DataType.Name.ToString().ToLower()}, ");
                    }

                    output.WriteLine($"Schema: {schema}");
                }

                foreach (var item in batch)
                {
                    item();
                }
                try
                {
                    foreach (DataRow row in _table.DataTable.Rows)
                    {
                        foreach (DataColumn column in _table.DataTable.Columns)
                        {
                            if (!row.IsNull(column))
                            {
                                var value = row[column];
                                if (column.DataType == typeof(DateTime))
                                {
                                    _sb.Write(((DateTime)value).ToString("o"));
                                }
                                else if (column.DataType == typeof(TimeSpan))
                                {
                                    _sb.Write(((TimeSpan)value).ToString("G"));
                                }
                                else if (value != null)
                                {
                                    var stringValue = value.ToString();
                                    _sb.Write(EscapeRegex.Replace(stringValue, static m => Replace(m)));
                                }
                            }

                            _sb.Write("\t");
                        }

                        _sb.WriteLine();
                    }

                    _sb.Flush();
                    _sb.BaseStream.Position = 0;

                    await blob.AppendFromStreamAsync(_sb.BaseStream);
                }
                catch (Exception ex)
                {
                    TestGlobal.Logger.Error(ex, "Error while logging");
                }

                _sb.BaseStream.SetLength(0);
                _table.DataTable.Clear();
            });
        }

        private static string Replace(Match m)
        {
            switch (m.ValueSpan[0])
            {
                case '\r':
                    return "\\r";
                case '\n':
                    return "\\n";
                case '\t':
                    return "\\t";
                case '\\':
                    return "\\\\";
            }

            return string.Empty;
        }

        private static Table CreateTable()
        {
            var table = new DataTable();
            return new Table(table);
        }

        public void Log(Severity severity, string correlationId, string message)
        {
            Enqueue(() =>
            {
                return new TableRow(_table)
                {
                    Severity = severity.ToString(),
                    CorrelationId = correlationId,
                    Message = message
                };
            });
        }

        public void Log(in LogMessage logMessage)
        {
            var m = logMessage;
            Enqueue(() =>
            {
                return new TableRow(_table)
                {
                    Severity = m.Severity.ToString(),
                    CorrelationId = m.OperationId,
                    Message = m.Message,
                    Component = m.TracerName,
                    Operation = m.OperationName,
                    Exception = m.Exception?.ToString()
                };
            });
        }

        public void LogOperationFinished(in OperationResult result)
        {
            var m = result;
            Enqueue(() =>
            {
                return new TableRow(_table)
                {
                    Severity = m.Severity.ToString(),
                    CorrelationId = m.OperationId,
                    Message = m.Message,
                    Component = m.TracerName,
                    Operation = m.OperationName,
                    Exception = m.Exception?.ToString(),
                    Duration = m.Duration,
                    Result = m.Status.ToStringNoAlloc()
                };
            });
        }

        public void LogOperationStarted(in OperationStarted operation)
        {
            var m = operation;
            Enqueue(() =>
            {
                return new TableRow(_table)
                {
                    Severity = m.Severity.ToString(),
                    CorrelationId = m.OperationId,
                    Message = m.Message,
                    Component = m.TracerName,
                    Operation = m.OperationName,
                };
            });
        }

        private long _seqNo;

        private void Enqueue(Func<TableRow> rowFactory)
        {
            var timestamp = DateTime.UtcNow;
            var seqNo = Interlocked.Increment(ref _seqNo);
            TableUpdateQueue.Enqueue(() =>
            {
                var row = rowFactory();
                row.TimeStamp = timestamp;
                row.SeqNo = seqNo;
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await TableUpdateQueue.DisposeAsync();

            await base.DisposeAsync();
        }

        private record Table(DataTable DataTable)
        {
            private DataColumnCollection Columns => DataTable.Columns;

            public TableColumn<DateTime> TimeStamp = new(DataTable);
            public DataColumn TestStartTime = Add(DataTable, new DataColumn(nameof(TestStartTime), typeof(DateTime)) { DefaultValue = DateTime.UtcNow });

            public TableColumn<string> TestName = new(DataTable);
            public TableColumn<string> CorrelationId = new(DataTable);
            public TableColumn<string> Component = new(DataTable);
            public TableColumn<string> Operation = new(DataTable);
            public TableColumn<string> Result = new(DataTable);
            public TableColumn<TimeSpan> Duration = new(DataTable);
            public TableColumn<string> Message = new(DataTable);
            public TableColumn<string> Exception = new(DataTable);
            public TableColumn<string> Severity = new(DataTable);
            public TableColumn<long> SeqNo = new(DataTable);

            private static DataColumn Add(DataTable dataTable, DataColumn dataColumn)
            {
                dataTable.Columns.Add(dataColumn);
                return dataColumn;
            }
        }

        private record struct TableRow(Table Table)
        {
            public DataRow Row { get; } = CreateRow(Table);

            private static DataRow CreateRow(Table table)
            {
                var row = table.DataTable.NewRow();
                table.DataTable.Rows.Add(row);
                return row;
            }

            public DateTime TimeStamp { set => Row[Table.TimeStamp.Column] = value; }
            //public DateTime TestStartTime { set => Row[Table.TestStartTime.Column] = value; }
            public string TestName { set => Row[Table.TestName.Column] = value; }
            public long SeqNo { set => Row[Table.SeqNo.Column] = value; }
            public string CorrelationId { set => Row[Table.CorrelationId.Column] = value; }
            public string Component { set => Row[Table.Component.Column] = value; }
            public string Operation { set => Row[Table.Operation.Column] = value; }
            public string Result { set => Row[Table.Result.Column] = value; }
            public TimeSpan Duration { set => Row[Table.Duration.Column] = value; }
            public string Message { set => Row[Table.Message.Column] = value; }
            public string Exception { set => Row[Table.Exception.Column] = value; }
            public string Severity { set => Row[Table.Severity.Column] = value; }
        }

        private record struct TableColumn<T>(DataTable Table, [CallerMemberName] string Name = null)
        {
            public DataColumn Column { get; } = Table.Columns.Add(Name, typeof(T));
        }
    }
#endif
}
