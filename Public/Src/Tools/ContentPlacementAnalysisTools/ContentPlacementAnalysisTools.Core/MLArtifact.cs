using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ContentPlacementAnalysisTools.Core
{
    /// <summary>
    ///The data structutre that stores ML related data for each artifact
    /// </summary>
    public class MLArtifact
    {

        private static readonly string[] s_columns = {
            "SharingClassLabel",
            "NumQueues",
            "NumBuilds",
            "DominantExtension",
            "SizeBytes",
            "AvgInputPips",
            "AvgOutputPips",
            "AvgPositionForInputPips",
            "AvgPositionForOutputPips",
            "AvgDepsForInputPips",
            "AvgDepsForOutputPips",
            "AvgInputsForInputPips",
            "AvgInputsForOutputPips",
            "AvgOutputsForInputPips",
            "AvgOutputsForOutputPips",
            "AvgPriorityForInputPips",
            "AvgPriorityForOutputPips",
            "AvgWeightForInputPips",
            "AvgWeightForOutputPips",
            "AvgTagCountForInputPips",
            "AvgTagCountForOutputPips",
            "AvgSemaphoreCountForInputPips",
            "AvgSemaphoreCountForOutputPips"
        };

        private static readonly string[] s_sharingClassLabels = {
            "Shared",
            "NonShared"
        };

        private static readonly Regex s_guidRegex = new Regex("([0-9a-f]{8}[-:_]{1}[0-9a-f]{4}[-:_]{1}[0-9a-f]{4}[-:_]{1}[0-9a-f]{4}[-:_]{1}[0-9a-f]{12})");

        /// <summary>
        /// The hash of this object 
        /// </summary>
        public string Hash { get; set; }
        /// <summary>
        /// The queues where this hash is present
        /// </summary>
        public HashSet<string> Queues { get; set; } = new HashSet<string>();
        /// <summary>
        /// The builds where this hash is present
        /// </summary>
        public HashSet<string> Builds { get; set; } = new HashSet<string>();
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
        public double AvgInputPips { get; set; }
        /// <summary>
        /// Hw many pips produce this hash
        /// </summary>
        public double AvgOutputPips { get; set; }
        /// <summary>
        /// Position (avg) of the input pips in the context of a workload 
        /// </summary>
        public double AvgPositionForInputPips { get; set; }
        /// <summary>
        /// Position (avg) of the output pips in the context of a workload 
        /// </summary>
        public double AvgPositionForOutputPips { get; set; }
        /// <summary>
        /// Dependencies (avg) of the input pips
        /// </summary>
        public double AvgDepsForInputPips { get; set; }
        /// <summary>
        /// Dependencies (avg) of the output pips
        /// </summary>
        public double AvgDepsForOutputPips { get; set; }
        /// <summary>
        /// Inputs (avg) of the input pips
        /// </summary>
        public double AvgInputsForInputPips { get; set; }
        /// <summary>
        /// Inputs (avg) of the outputs pips
        /// </summary>
        public double AvgInputsForOutputPips { get; set; }
        /// <summary>
        /// Outputs (avg) of the input pips
        /// </summary>
        public double AvgOutputsForInputPips { get; set; }
        /// <summary>
        /// Outputs (avg) of the output pips
        /// </summary>
        public double AvgOutputsForOutputPips { get; set; }
        /// <summary>
        /// Priority (avg) of the input pips
        /// </summary>
        public double AvgPriorityForInputPips { get; set; }
        /// <summary>
        /// Priority (avg) of the output pips
        /// </summary>
        public double AvgPriorityForOutputPips { get; set; }
        /// <summary>
        /// Weight (avg) of the input pips
        /// </summary>
        public double AvgWeightForInputPips { get; set; }
        /// <summary>
        /// Weight (avg) of the output pips
        /// </summary>
        public double AvgWeightForOutputPips { get; set; }
        /// <summary>
        /// Tag count (avg) of the input pips
        /// </summary>
        public double AvgTagCountForInputPips { get; set; }
        /// <summary>
        /// Tag count (avg) of the output pips
        /// </summary>
        public double AvgTagCountForOutputPips { get; set; }
        /// <summary>
        /// Semaphore count (avg) of the input pips
        /// </summary>
        public double AvgSemaphoreCountForInputPips { get; set; }
        /// <summary>
        /// Semaphore count (avg) of the output pips
        /// </summary>
        public double AvgSemaphoreCountForOutputPips { get; set; }

        /// <summary>
        /// Writes the column headers for a csv output
        /// </summary>
        public static void WriteColumnsToStream(TextWriter writer) => writer.WriteLine(string.Join(",", s_columns));
        /// <summary>
        /// Writes the values associated to the object formatted ofr csv output
        /// </summary>
        public void WriteToCsvStream(TextWriter writer) => 
            writer.WriteLine(
                string.Join(
                    ",",
                    Queues.Count > 1 ? s_sharingClassLabels[0] : s_sharingClassLabels[1],
                    Queues.Count,
                    Builds.Count,
                    DominantExtension(),
                    SizeBytes,
                    AvgInputPips,
                    AvgOutputPips,
                    AvgPositionForInputPips,
                    AvgPositionForOutputPips,
                    AvgDepsForInputPips,
                    AvgDepsForOutputPips,
                    AvgInputsForInputPips,
                    AvgInputsForOutputPips,
                    AvgOutputsForInputPips,
                    AvgOutputsForOutputPips,
                    AvgPriorityForInputPips,
                    AvgPriorityForOutputPips,
                    AvgWeightForInputPips,
                    AvgWeightForOutputPips,
                    AvgTagCountForInputPips,
                    AvgTagCountForOutputPips,
                    AvgSemaphoreCountForInputPips,
                    AvgSemaphoreCountForOutputPips
                )
           );

        private bool ExtensionMatchesGuid(string extension) => s_guidRegex.IsMatch(extension);

        private string DominantExtension()
        {
            foreach(var ext in Extensions)
            {
                if (ext.Contains("."))
                {
                    return ext;
                }
            }
            return "none";
        }
        /// <summary>
        /// Adds a new extension, tries to resolve it when the extension matches a guid ()
        /// </summary>
        public void ReportExtension(string filename)
        {
            var extension = filename.Contains(".")? filename.Substring(filename.LastIndexOf('.')) : filename;
            if (ExtensionMatchesGuid(extension))
            {
                if (filename.Contains("dll"))
                {
                    Extensions.Add(".dll");
                }
                else if (filename.Contains("rll"))
                {
                    Extensions.Add(".dll");
                }
                else if (filename.Contains("exe"))
                {
                    Extensions.Add(".exe");
                }
            }
            else
            {
                if(extension.Length > 0)
                {
                    Extensions.Add(extension);
                }
            }
        }

    }

     
}
