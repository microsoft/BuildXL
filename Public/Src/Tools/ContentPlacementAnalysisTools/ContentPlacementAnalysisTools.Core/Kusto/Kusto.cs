using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.Core.Kusto
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

    /// <summary>
    /// Represents cummulative data from queues obtained by querying kusto. It does not keep
    /// actual column names but rather a queue and a vector
    /// </summary>
    public class KustoQueueData
    {
        /// <summary>
        /// The double precision that should be used to read and write one of these elements
        /// </summary>
        public static readonly int DefaultPrecision = 10;
        /// <summary>
        /// The architecture name
        /// </summary>
        public string Architecture { get; set; }
        /// <summary>
        /// The stamp name
        /// </summary>
        public string Stamp { get; set; }
        /// <summary>
        /// The queue name
        /// </summary>
        public string QueueName { get; set; }
        /// <summary>
        /// The cummulative data from the queue as a list of numbers
        /// </summary>
        public List<double> Data { get; set; } = new List<double>();
        /// <summary>
        /// This is a simple utility to calculate euclidean distance between this and the parameter
        /// </summary>
        public double EuclideanDistance(KustoQueueData to)
        {
            // i will let it fail if these guys dont have the same size
            var sum = 0.0;
            for(var i = 0; i < Data.Count; ++i)
            {
                sum += Math.Pow(Data[i] - to.Data[i], 2);
            }
            return Math.Round(Math.Sqrt(sum), DefaultPrecision);
        }
        /// <summary>
        /// This is a simple utlity to, given a list of possible neighbors, get a list of closest neightbors by euclidean distance. It looks 
        /// for the closes WITHIN THE SAME STAMP. It gives preference to queues building for the same architecture
        /// </summary>
        public List<Neighbor> ClosestNeighborsByEuclideanDistance(List<KustoQueueData> queues)
        {
            var neighbors = new List<Neighbor>();
            var maxDistance = 0.0;
            foreach (var q in queues)
            {
                if(q.QueueName != QueueName && q.Stamp == Stamp)
                {
                    var distance = EuclideanDistance(q);
                    neighbors.Add(new Neighbor(distance, q.QueueName, q.Architecture));
                    if(maxDistance < distance)
                    {
                        maxDistance = distance;
                    }
                }
            }
            // penalize not being the same architecture
            neighbors.ForEach(n => { n.Distance += (n.Arch == Architecture ? 0.0 : maxDistance); });
            // and return them sorted
            neighbors.Sort(
                (n1, n2) => 
                {
                    return n1.Distance - n2.Distance < 0 ? -1 : (n1.Distance - n2.Distance > 0 ? 1 : 0);
                }
            );
            return neighbors;
        }
        /// <summary>
        /// A wrapper class for a queue with distance
        /// </summary>
        public class Neighbor
        {
            /// <summary>
            /// The calculated distance
            /// </summary>
            public double Distance { get; set; }
            /// <summary>
            /// The name of the neighbor
            /// </summary>
            public string Name { get; set; }
            /// <summary>
            /// The architecture of the neighbor
            /// </summary>
            public string Arch { get; set; }
            /// <summary>
            /// Constructor
            /// </summary>
            public Neighbor(double d, string n, string a)
            {
                Distance = d;
                Name = n;
                Arch = a;
            }
        } 
    }

}
