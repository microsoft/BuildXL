using System;
using System.IO;
using System.Reflection;
using ContentPlacementAnalysisTools.Core;
using ContentPlacementAnalysisTools.Extraction.CPResources;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;

namespace ContentPlacementAnalysisTools.Extraction.Action
{
    /// <summary>
    /// This is the action queries kusto for a single build's info
    /// </summary>
    public class GetKustoBuild : TimedAction<GetKustoBuildInput, GetKustoBuildOutput>
    {

        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private KustoConnectionConfiguration m_kustoConnectionConfiguration = null;
        private ICslQueryProvider m_queryProvider = null;
        private string m_query = null;

        /// <inheritdoc />
        protected override void CleanUp()
        {
            // dispose the connection here
            if(m_queryProvider != null)
            {
                m_queryProvider.Dispose();
            }
        }

        /// <summary>
        /// This is the action queries kusto for a single build's info
        /// </summary>
        protected override GetKustoBuildOutput Perform(GetKustoBuildInput input)
        {
            // so just get the single row and return it...
            try
            {
                s_logger.Debug($"GetKustoBuild starts for build=[{input.BuildId}]");
                using (var reader = m_queryProvider.ExecuteQuery(m_query, null))
                {
                    // this should always be one line...
                    while (reader.Read())
                    {
                        return new GetKustoBuildOutput(new KustoBuild() {
                            BuildId = reader.GetString(0),
                            LogDirectory = reader.GetString(1),
                            StartTime = reader.GetDateTime(2).Ticks,
                            BuildDurationMs = Convert.ToDouble(reader.GetInt64(3)),
                            BuildControllerMachineName = reader.GetString(4),
                            BuildQueue = reader.GetString(5)
                        });
                    }
                    return null;
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
            // parse the kusto connection configuration here
            m_kustoConnectionConfiguration = KustoConnectionConfiguration.FromJson(input.KustoConnectionConfigurationFile);
            // and connect
            m_queryProvider = GetKustoConnection(m_kustoConnectionConfiguration);
            // and also parse the query
            m_query = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), constants.GetBuildQuery)).Replace("{0}", input.BuildId);
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
        /// The build that will be queries
        /// </summary>
        public string BuildId { get; set; }
        /// <summary>
        /// Json file to get kusto connection params from
        /// </summary>
        public string KustoConnectionConfigurationFile { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        public GetKustoBuildInput(string bid, string kustoJson)
        {
            BuildId = bid;
            KustoConnectionConfigurationFile = kustoJson;
        }
    }

    /// <summary>
    /// This is the output for this action, an object that represents the row that was read from kusto, if any...
    /// </summary>
    public class GetKustoBuildOutput
    {
        /// <summary>
        /// The kusto data read
        /// </summary>
        public KustoBuild KustoBuildData { get; }
        /// <summary>
        /// Constructor
        /// </summary>
        public GetKustoBuildOutput(KustoBuild k)
        {
            KustoBuildData = k;
        }
    }

}
