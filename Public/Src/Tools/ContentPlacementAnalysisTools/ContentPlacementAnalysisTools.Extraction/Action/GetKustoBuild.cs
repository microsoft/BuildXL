using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ContentPlacementAnalysisTools.Core.Kusto;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlacementAnalysisTools.Extraction.Main;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;

namespace ContentPlacementAnalysisTools.Extraction.Action
{
    /// <summary>
    /// This is the action queries kusto for a set of builds
    /// </summary>
    public class GetKustoBuild : TimedAction<GetKustoBuildInput, GetKustoBuildOutput>
    {

        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ApplicationConfiguration m_configuration = null;
        private ICslQueryProvider m_queryProvider = null;
        private string m_query = null;
        private static readonly int s_maxRetryBuilds = Convert.ToInt32(constants.MaxRetryBuilds);

        /// <summary>
        /// Constructor
        /// </summary>
        public GetKustoBuild(ApplicationConfiguration config)
        {
            m_configuration = config;
        }

        /// <summary>
        /// When this task is done, the connection to kusto is disposed
        /// </summary>
        protected override void CleanUp(GetKustoBuildInput input, GetKustoBuildOutput output)
        {
            // dispose the connection here
            if(m_queryProvider != null)
            {
                m_queryProvider.Dispose();
            }
        }

        /// <summary>
        /// This is the action queries kusto for a set of builds (less than or equal to MaxBuilds * 5)
        /// We obtain more than one because we will retry all failed with new builds
        /// </summary>
        protected override GetKustoBuildOutput Perform(GetKustoBuildInput input)
        {
            s_logger.Debug($"GetKustoBuild starts");
            try
            {
                var builds = new List<List<KustoBuild>>();
                using (var reader = m_queryProvider.ExecuteQuery(m_query, null))
                {
                    // each line has a single build
                    var pack = new List<KustoBuild>();
                    while (reader.Read())
                    {

                        pack.Add(
                            new KustoBuild()
                            {
                                BuildId = reader.GetString(0),
                                LogDirectory = reader.GetString(1),
                                StartTime = reader.GetDateTime(2).Ticks,
                                BuildDurationMs = Convert.ToDouble(reader.GetInt64(3)),
                                BuildControllerMachineName = reader.GetString(4),
                                BuildQueue = reader.GetString(5)
                            }
                        );
                        if (pack.Count == s_maxRetryBuilds)
                        {
                            var values = new KustoBuild[pack.Count];
                            pack.CopyTo(values);
                            builds.Add(new List<KustoBuild>(values));
                            pack = new List<KustoBuild>();
                        }
                    }
                    return new GetKustoBuildOutput(builds);
                }
            }
            finally
            {
                s_logger.Debug($"GetKustoBuild ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
            
        }
        /// <inheritdoc />
        protected override void Setup(GetKustoBuildInput input)
        {
            // try to connect..
            m_queryProvider = GetKustoConnection(m_configuration.KustoConfig);
            // and also parse the query
            m_query = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), constants.GetBuildQuery))
                .Replace("{0}", Convert.ToString(input.Year))
                .Replace("{1}", Convert.ToString(input.Month))
                .Replace("{2}", Convert.ToString(input.Day))
                .Replace("{3}", Convert.ToString(input.NumBuilds * s_maxRetryBuilds))
                .Replace("{4}", m_configuration.UseCBTest? constants.CBTestDatabaseName : constants.ProdDatabaseName);
            s_logger.Debug($"Target Query: {m_query}");

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
    /// This is the input for this action, it required a buildid (guid) and a kusto connection conf file
    /// </summary>
    public class GetKustoBuildInput
    {
        /// <summary>
        /// The number of builds that will be requested
        /// </summary>
        public int NumBuilds { get; set; }
        /// <summary>
        /// The year for the date when builds will be taken
        /// </summary>
        public int Year { get; set; }
        /// <summary>
        /// The mont for the date when builds will be taken
        /// </summary>
        public int Month { get; set; }
        /// <summary>
        /// The day for the date when builds will be taken
        /// </summary>
        public int Day { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        public GetKustoBuildInput(int nb, int y, int m, int d)
        {
            NumBuilds = nb;
            Year = y;
            Month = m;
            Day = d;
        }
    }

    /// <summary>
    /// This is the output for this action, an object that represents the row that was read from kusto, if any...
    /// </summary>
    public class GetKustoBuildOutput
    {
        /// <summary>
        /// The kusto data read. Its a list of list, each containing s_maxRetryBuilds results
        /// </summary>
        public List<List<KustoBuild>> KustoBuildData { get; }
        /// <summary>
        /// Constructor
        /// </summary>
        public GetKustoBuildOutput(List<List<KustoBuild>> k)
        {
            KustoBuildData = k;
        }
    }

}
