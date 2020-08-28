using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Mathematics;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Perfolizer.Horology;
using System;

namespace BuildXL.Cache.ContentStore.Benchmarks
{
    [RPlotExporter]
    public class ThroughputColumn : IColumn
    {
        private readonly int _totalBytes;
        private readonly bool _error;
        public ThroughputColumn(int totalBytes, bool error) => (_totalBytes, _error) = (totalBytes, error);

        public string Id => _error ? "RateError" : "Rate";
        public string ColumnName => Id;
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Metric;
        public int PriorityInCategory => _error ? 1 : 0;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Size;
        public string Legend => _error ? "Half of 99.9% confidence interval of throughput" : "Mean throughput";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            BenchmarkReport report = summary[benchmarkCase];
            if (report?.ResultStatistics == null)
            {
                return "NA";
            }

            Statistics stats = report.ResultStatistics;
            TimeInterval mean = TimeInterval.FromNanoseconds(stats.Mean);
            double bytesPerSec = _totalBytes / mean.ToSeconds();

            if (_error)
            {
                bytesPerSec *= stats.StandardError / stats.Mean;
            }

            SizeValue result = new SizeValue((long)Math.Round(bytesPerSec));

            return $"{result.ToString(cultureInfo: null)}/s";
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
        public bool IsAvailable(Summary summary) => true;
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    }
}
