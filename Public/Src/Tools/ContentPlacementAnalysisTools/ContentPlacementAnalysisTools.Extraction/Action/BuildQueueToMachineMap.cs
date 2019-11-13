using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using ContentPlacementAnalysisTools.Core.Kusto;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlacementAnalysisTools.Extraction.Main;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using NLog.Config;

namespace ContentPlacementAnalysisTools.Extraction.Action
{
    /// <summary>
    /// This action builds a map in which each queue has a set of possible machines (sorted by frequency). This is
    /// obtained querying Kusto for builds within a month 
    /// </summary>
    public class BuildQueueToMachineMap : TimedAction<BuildQueueToMachineMapInput, BuildQueueToMachineMapOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ApplicationConfiguration m_configuration = null;
        private ICslQueryProvider m_queryProvider = null;
        private string m_query = null;
        private StreamWriter m_writer = null;
        private string m_outputDir = null;
        private string m_outputFile = null;

        /// <summary>
        /// Constructor
        /// </summary>
        public BuildQueueToMachineMap(ApplicationConfiguration config)
        {
            m_configuration = config;
        }
        /// <summary>
        /// Executo a kisto query for a specific queue and parse its data
        /// </summary>
        protected override BuildQueueToMachineMapOutput Perform(BuildQueueToMachineMapInput input)
        {
            s_logger.Debug($"BuildQueueToMachineMap starts for queue {input.QueueName}");
            var machinesWithFrequencies = new Dictionary<string, int>();
            try
            {
                
                using (var reader = m_queryProvider.ExecuteQuery(m_query, null))
                {
                    // each line has a set of machines, which are comma separated
                    while (reader.Read())
                    {
                        // get their names
                        var machines = reader.GetString(0).Split(',');
                        foreach(var machine in machines)
                        {
                            var name = machine.Contains(":") ? machine.Substring(0, machine.IndexOf(":")) : machine;
                            // and store their frequencies
                            if (machinesWithFrequencies.ContainsKey(name))
                            {
                                machinesWithFrequencies[name] += 1;
                            }
                            else
                            {
                                machinesWithFrequencies[name] = 1;
                            }
                        }
                    }
                    // now sort them
                    var sortedByFrequency = machinesWithFrequencies.ToList();
                    sortedByFrequency.Sort((v1, v2) => { return v1.Value > v2.Value ? -1 : (v1.Value < v2.Value ? 1 : 0); });
                    // now, we can write it
                    s_logger.Debug($"Saving to [{m_outputFile}]");
                    foreach (var entry in sortedByFrequency)
                    {
                        m_writer.WriteLine(string.Join(",", entry.Key, entry.Value));
                    }
                    // and done...
                    return new BuildQueueToMachineMapOutput(sortedByFrequency);
                }
            }
            finally
            {
                s_logger.Debug($"BuildQueueToMachineMap ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
        }

        /// <inheritdoc />
        protected override void CleanUp(BuildQueueToMachineMapInput input, BuildQueueToMachineMapOutput output)
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
        protected override void Setup(BuildQueueToMachineMapInput input)
        {
            // prepare query, calculate last day of requested month
            var lastDayOfMonth = DateTime.DaysInMonth(input.Year, input.Month);
            // try to connect..
            m_queryProvider = GetKustoConnection(m_configuration.KustoConfig);
            // and also parse the query
            m_query = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), constants.GetMachineMapQuery))
                .Replace("{0}", Convert.ToString(input.Year))
                .Replace("{1}", Convert.ToString(input.Month))
                .Replace("{2}", Convert.ToString(lastDayOfMonth))
                .Replace("{3}", input.QueueName)
                .Replace("{4}", m_configuration.UseCBTest? constants.CBTestDatabaseName : constants.ProdDatabaseName);
            s_logger.Debug($"Target Query: {m_query}");
            // and prepare the directory with the output
            m_outputDir = Path.Combine(input.OutputDirectory, constants.ResultDirectoryName, constants.MachineMapDirectoryName);
            // create this dir
            Directory.CreateDirectory(m_outputDir);
            m_outputFile = Path.Combine(m_outputDir, input.QueueName);
            m_writer = new StreamWriter(m_outputFile);
            Contract.Requires(File.Exists(m_outputFile), $"Could not create output file [{m_outputFile}]");
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
    /// Includes the year, the month, the output directory and the queue name to build the output
    /// </summary>
    public class BuildQueueToMachineMapInput
    {
        /// <summary>
        /// The year to build the query
        /// </summary>
        public int Year { get; set; }
        /// <summary>
        /// The month to build the query
        /// </summary>
        public int Month { get; set; }
        /// <summary>
        /// The output directory
        /// </summary>
        public string OutputDirectory { get; set; }
        /// <summary>
        /// The target queue
        /// </summary>
        public string QueueName { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        public BuildQueueToMachineMapInput(int y, int m, string od, string q)
        {
            Year = y;
            Month = m;
            OutputDirectory = od;
            QueueName = q;
        }
    }
    /// <summary>
    /// Includes a list of keyvalue pairs in which the key is a machine name and the value is the frequency
    /// (how many times has this machine build in this queue)
    /// </summary>
    public class BuildQueueToMachineMapOutput
    {
        /// <summary>
        /// This list is sorted by frequency (highest first)
        /// </summary>
        public List<KeyValuePair<string, int>> MachineMap { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        public BuildQueueToMachineMapOutput(List<KeyValuePair<string, int>> mm)
        {
            MachineMap = mm;
        }
    }
}
