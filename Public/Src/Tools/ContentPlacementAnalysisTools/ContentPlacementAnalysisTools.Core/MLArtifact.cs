using System.Collections.Generic;
using System.IO;

namespace ContentPlacementAnalysisTools.Core
{
    /// <summary>
    ///The data structutre that stores ML related data for each artifact
    /// </summary>
    public class MLArtifact
    {

        private static string[] s_columns = {
            "NumQueues",
            "SharingClassLabel",
            "DominantExtension",
            "SizeBytes",
            "NumInputPips",
            "NumOutputPips",
            "AvgPositionForInputPips",
            "AvgPositionForOutputPips",
            "AvgDepsForInputPips",
            "AvgDepsForOutputPips",
            "AvgInputsForInputPips",
            "AvgInputsForOutputPips",
            "AvgOutputsForInputPips",
            "AvgOutputsForOutputPips"
        };

        private static string[] s_sharingClassLabels ={
            "Shared",
            "NonShared"
        };

        /// <summary>
        /// The hash of this object 
        /// </summary>
        public string Hash { get; set; }
        /// <summary>
        /// The queues where this hash is present
        /// </summary>
        public HashSet<string> Queues { get; set; } = new HashSet<string>();
        /// <summary>
        /// All the extensions reported for this hash
        /// </summary>
        public HashSet<string> Extensions { get; set; } = new HashSet<string>();
        /// <summary>
        /// The size of this file
        /// </summary>
        public long SizeBytes { get; set; }
        /// <summary>
        /// How many pips consume this hash
        /// </summary>
        public int NumInputPips { get; set; }
        /// <summary>
        /// Hw many pips produce this hash
        /// </summary>
        public int NumOutputPips { get; set; }
        public double AvgPositionForInputPips { get; set; }
        public double AvgPositionForOutputPips { get; set; }
        public double AvgDepsForInputPips { get; set; }
        public double AvgDepsForOutputPips { get; set; }
        public double AvgInputsForInputPips { get; set; }
        public double AvgInputsForOutputPips { get; set; }
        public double AvgOutputsForInputPips { get; set; }
        public double AvgOutputsForOutputPips { get; set; }




        public static void WriteColumnsToStream(StreamWriter writer) => writer.WriteLine(string.Join(",", s_columns));

        public void WriteToCsvStream(StreamWriter writer) => 
            writer.WriteLine(
                string.Join(
                    ",", 
                    Queues.Count,
                    Queues.Count > 1? MLArtifact.s_sharingClassLabels[0] : MLArtifact.s_sharingClassLabels[1],
                    DominantExtension(),
                    SizeBytes,
                    NumInputPips,
                    NumOutputPips,
                    AvgPositionForInputPips,
                    AvgPositionForOutputPips,
                    AvgDepsForInputPips,
                    AvgDepsForOutputPips,
                    AvgInputsForInputPips,
                    AvgInputsForOutputPips,
                    AvgOutputsForInputPips,
                    AvgOutputsForOutputPips
                )
           );

        private string DominantExtension()
        {
            return null;
        }

    }

     
}
