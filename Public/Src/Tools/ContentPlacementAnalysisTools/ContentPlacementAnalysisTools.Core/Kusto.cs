using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.Core
{
    /// <summary>
    /// Contains the parameters necessary to establish a connection to kusto, and its meant
    /// to be read from a json file that looks like (actual values might change):
    ///   {
    ///      "Cluster": "https://cbuild.kusto.windows.net",
    ///      "User": "TIPICALLY YOUR MICROSOFT EMAIL",
    ///      "AuthorityId": "YOUR AUTHORITY ID",
    ///      "DefaultDB": "CloudBuildProd"
    ///    }
    /// </summary>
    public class KustoConnectionConfiguration
    {
        /// <summary>
        /// The target cluster
        /// </summary>
        public string Cluster { get; set; }
        /// <summary>
        /// Username
        /// </summary>
        public string User { get; set; }
        /// <summary>
        /// The authority id
        /// </summary>
        public string AuthorityId { get; set; }
        /// <summary>
        /// The default db
        /// </summary>
        public string DefaultDB { get; set; }
        /// <inheritdoc />
        public override string ToString()
        {
            return new StringBuilder()
                .Append("Cluster=").Append(Cluster).Append(", ")
                .Append("User=").Append(User).Append(", ")
                .Append("AuthorityId=").Append(AuthorityId).Append(", ")
                .Append("DefaultDB=").Append(DefaultDB)
                .ToString();
        }
    }

    /// <summary>
    /// Represents a build obtained by querying kusto
    /// </summary>
    public class KustoBuild
    {
        /// <summary>
        /// The build identifier
        /// </summary>
        public string BuildId { get; set; }
        /// <summary>
        /// Directory where the logs are (master machine)
        /// </summary>
        public string LogDirectory { get; set; }
        /// <summary>
        /// ticks when the build started
        /// </summary>
        public long StartTime { get; set; }
        /// <summary>
        /// Duration of this build
        /// </summary>
        public double BuildDurationMs { get; set; }
        /// <summary>
        /// Master machine, no port
        /// </summary>
        public string BuildControllerMachineName { get; set; }
        /// <summary>
        /// The queue where the build took place
        /// </summary>
        public string BuildQueue { get; set; }
    }
}
