using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ContentPlacementAnalysisTools.Core.Kusto;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlacementAnalysisTools.Extraction.Main;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;

namespace ContentPlacementAnalysisTools.Extraction.Action
{
    /// <summary>
    /// This is the action that collects queue data in order to establish similarity between queues
    /// </summary>
    public class DownloadMonthlyQueueData : TimedAction<DownloadMonthlyQueueDataInput, DownloadMonthlyQueueDataOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ApplicationConfiguration m_configuration = null;
        private ICslQueryProvider m_queryProvider = null;
        private string m_query = null;
        private StreamWriter m_writer = null;
        private string m_outputDir = null;
        private string m_distanceMaps = null;
        private string m_outputFile = null;

        /// <summary>
        /// Constructor
        /// </summary>
        public DownloadMonthlyQueueData(ApplicationConfiguration config)
        {
            m_configuration = config;
        }
        /// <summary>
        /// Download data for queues over the specified month
        /// </summary>
        protected override DownloadMonthlyQueueDataOutput Perform(DownloadMonthlyQueueDataInput input)
        {
            s_logger.Debug($"DownloadMonthlyQueueData starts");
            var queues = new List<KustoQueueData>();
            var minMax = new List<MinMaxPair>();
            try
            {
                using (var reader = m_queryProvider.ExecuteQuery(m_query, null))
                {
                    // each line has a single queue
                    while (reader.Read())
                    {
                        var queueData = new KustoQueueData()
                        {
                            Stamp = reader.GetString(0),
                            Architecture = reader.GetString(1),
                            QueueName = reader.GetString(2)
                        };
                        // initialize this here
                        if(minMax.Count == 0)
                        {
                            for (var i = 0; i < reader.FieldCount - 3; ++i)
                            {
                                minMax.Add(new MinMaxPair());
                            }
                        }
                        // add the numbers
                        for (var i = 3; i < reader.FieldCount; ++i)
                        {
                            try
                            {
                                queueData.Data.Add(Math.Round(reader.GetDouble(i), KustoQueueData.DefaultPrecision));
                            }
                            #pragma warning disable ERP022
                            catch (Exception)
                            {
                                // if we could not get a double, we might have got NaN, which is not wrong
                                queueData.Data.Add(0.0);
                            }
                            #pragma warning enable ERP022
                            // store the min and max
                            var minMaxPos = i - 3;
                            if (minMax[minMaxPos].Min > queueData.Data[minMaxPos])
                            {
                                minMax[minMaxPos].Min = queueData.Data[minMaxPos];
                            }
                            if (minMax[minMaxPos].Max < queueData.Data[minMaxPos])
                            {
                                minMax[minMaxPos].Max = queueData.Data[minMaxPos];
                            }
                        }
                        // save it
                        queues.Add(queueData);
                    }
                    // normalize
                    s_logger.Debug($"Got {queues.Count} rows, normalizing");
                    foreach (var q in queues)
                    {
                        for(var i = 0; i < q.Data.Count; ++i)
                        {
                            if(minMax[i].Min != minMax[i].Max)
                            {
                                q.Data[i] = (q.Data[i] - minMax[i].Min) / (minMax[i].Max - minMax[i].Min);
                            }
                            else
                            {
                                q.Data[i] = q.Data[i] - minMax[i].Min;
                            }
                        }
                    }
                    // now, we can write it
                    s_logger.Debug($"Saving to [{m_outputFile}]");
                    foreach (var q in queues)
                    {
                        m_writer.WriteLine(string.Join(",", q.Stamp, q.Architecture, q.QueueName, string.Join(",", q.Data)));
                    }
                    // and we can create the distance maps
                    s_logger.Debug($"Creating distance maps in [{m_distanceMaps}]");
                    foreach(var q in queues)
                    {
                        var writer = new StreamWriter(Path.Combine(m_distanceMaps, q.QueueName));
                        // calculate
                        var neighbors = q.ClosestNeighborsByEuclideanDistance(queues);
                        // write to file
                        foreach(var neighbor in neighbors)
                        {
                            writer.WriteLine(string.Join(",", neighbor.Name, neighbor.Distance));
                        }
                        writer.Close();
                    }
                    // and done...
                    return new DownloadMonthlyQueueDataOutput(queues);
                }
            }
            finally
            {
                s_logger.Debug($"DownloadMonthlyQueueData ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
        }

        internal class MinMaxPair
        {
            public double Min { get; set; } = double.MaxValue;
            public double Max { get; set; } = double.MinValue;
        }
        /// <inheritdoc />
        protected override void CleanUp(DownloadMonthlyQueueDataInput input, DownloadMonthlyQueueDataOutput output)
        {
            // dispose the connection here
            if (m_queryProvider != null)
            {
                m_queryProvider.Dispose();
            }
            // and the writer
            if(m_writer != null)
            {
                m_writer.Close();
            }
        }
        /// <inheritdoc />
        protected override void Setup(DownloadMonthlyQueueDataInput input)
        {
            // prepare query, calculate last day of requested month
            var lastDayOfMonth = DateTime.DaysInMonth(input.Year, input.Month);
            // try to connect..
            m_queryProvider = GetKustoConnection(m_configuration.KustoConfig);
            // and also parse the query
            m_query = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), constants.GetMonthlyQueueDataQuery))
                .Replace("{0}", Convert.ToString(input.Year))
                .Replace("{1}", Convert.ToString(input.Month))
                .Replace("{2}", Convert.ToString(lastDayOfMonth))
                .Replace("{3}", m_configuration.UseCBTest ? constants.CBTestDatabaseName : constants.ProdDatabaseName);
            s_logger.Debug($"Target Query: {m_query}");
            // and prepare the directory with the output
            m_outputDir = Path.Combine(input.OutputDirectory, constants.ResultDirectoryName);
            m_distanceMaps = Path.Combine(m_outputDir, constants.QueueMapDirectoryName);
            // create these dirs
            Directory.CreateDirectory(m_outputDir);
            Directory.CreateDirectory(m_distanceMaps);
            m_outputFile = Path.Combine(m_outputDir, $"queues-{input.Year}-{input.Month}.csv");
            m_writer = new StreamWriter(m_outputFile);
            Contract.Requires(File.Exists(m_outputFile), $"Could not create output file [{m_outputFile}]");
            Contract.Requires(Directory.Exists(m_distanceMaps), $"Could not create output directory [{m_distanceMaps}]");
        }

        private ICslQueryProvider GetKustoConnection(KustoConnectionConfiguration conf)
        {
            var kustoConnectionStringBuilder = new KustoConnectionStringBuilder(conf.Cluster, conf.DefaultDB)
            {
                UserID = conf.User,
                Authority = conf.AuthorityId,
                FederatedSecurity = true
            };
            return KustoClientFactory.CreateCslQueryProvider(kustoConnectionStringBuilder);
        }
    }

    /// <summary>
    /// Input contains fields for the related query
    /// </summary>
    public class DownloadMonthlyQueueDataInput
    {
        /// <summary>
        /// The year for the query
        /// </summary>
        public int Year { get; set; }
        /// <summary>
        /// The month for the query
        /// </summary>
        public int Month { get; set; }
        /// <summary>
        /// Where to write the results
        /// </summary>
        public string OutputDirectory { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        public DownloadMonthlyQueueDataInput(int y, int m, string od)
        {
            Year = y;
            Month = m;
            OutputDirectory = od;
        }
    }
    /// <summary>
    /// Output includes list of queue data points
    /// </summary>
    public class DownloadMonthlyQueueDataOutput
    {
        /// <summary>
        /// List of queue data points (queue name + data vector)
        /// </summary>
        public List<KustoQueueData> Queues { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        public DownloadMonthlyQueueDataOutput(List<KustoQueueData> q)
        {
            Queues = q;
        }
    }
}
