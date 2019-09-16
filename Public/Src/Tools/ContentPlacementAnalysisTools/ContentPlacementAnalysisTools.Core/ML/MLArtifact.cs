using System;
using System.Collections.Generic;
using System.IO;
using ContentPlacementAnalysisTools.Core.ML.Classifier;

namespace ContentPlacementAnalysisTools.Core.ML
{
    /// <summary>
    ///The data structutre that stores ML related data for each artifact
    /// </summary>
    public class MLArtifact
    {

        private static readonly int s_defaultPrecision = 10;

        private static readonly string[] s_columns = {
            "SharingClassLabel",
            "SharingClassId",
            "NumQueues",
            "NumBuilds",
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

        /// <summary>
        ///The labels we use to classify artifacts
        /// </summary>
        public static readonly string[] SharingClassLabels = {
            "Shared",
            "NonShared"
        };

        private static readonly Dictionary<string, int> s_sharingClassIds = new Dictionary<string, int> {
            ["Shared"] = 0,
            ["NonShared"] = 1
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
        /// The builds where this hash is present
        /// </summary>
        public HashSet<string> Builds { get; set; } = new HashSet<string>();
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
        /// The reported file
        /// </summary>
        public HashSet<string> ReportedPaths { get; set; } = new HashSet<string>();

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
                    Queues.Count > 1 ? SharingClassLabels[0] : SharingClassLabels[1],
                    s_sharingClassIds[Queues.Count > 1 ? SharingClassLabels[0] : SharingClassLabels[1]],
                    Queues.Count,
                    Builds.Count,
                    SizeBytes,
                    Math.Round(AvgInputPips, s_defaultPrecision),
                    Math.Round(AvgOutputPips, s_defaultPrecision),
                    Math.Round(AvgPositionForInputPips, s_defaultPrecision),
                    Math.Round(AvgPositionForOutputPips, s_defaultPrecision),
                    Math.Round(AvgDepsForInputPips, s_defaultPrecision),
                    Math.Round(AvgDepsForOutputPips, s_defaultPrecision),
                    Math.Round(AvgInputsForInputPips, s_defaultPrecision),
                    Math.Round(AvgInputsForOutputPips, s_defaultPrecision),
                    Math.Round(AvgOutputsForInputPips, s_defaultPrecision),
                    Math.Round(AvgOutputsForOutputPips, s_defaultPrecision),
                    Math.Round(AvgPriorityForInputPips, s_defaultPrecision),
                    Math.Round(AvgPriorityForOutputPips, s_defaultPrecision),
                    Math.Round(AvgWeightForInputPips, s_defaultPrecision),
                    Math.Round(AvgWeightForOutputPips, s_defaultPrecision),
                    Math.Round(AvgTagCountForInputPips, s_defaultPrecision),
                    Math.Round(AvgTagCountForOutputPips, s_defaultPrecision),
                    Math.Round(AvgSemaphoreCountForInputPips, s_defaultPrecision),
                    Math.Round(AvgSemaphoreCountForOutputPips, s_defaultPrecision)
                )
           );

        /// <summary>
        /// Represents an artifact as instance, mainly for testing purposes
        /// </summary>
        public RandomForestInstance AsInstance()
        {
            var instance = new RandomForestInstance()
            {
                Attributes = new Dictionary<string, double>()
                {
                    ["SizeBytes"] = SizeBytes,
                    ["AvgInputPips"] = Math.Round(AvgInputPips, s_defaultPrecision),
                    ["AvgOutputPips"] = Math.Round(AvgOutputPips, s_defaultPrecision),
                    ["AvgPositionForInputPips"] = Math.Round(AvgPositionForInputPips, s_defaultPrecision),
                    ["AvgPositionForOutputPips"] = Math.Round(AvgPositionForOutputPips, s_defaultPrecision),
                    ["AvgDepsForInputPips"] = Math.Round(AvgDepsForInputPips, s_defaultPrecision),
                    ["AvgDepsForOutputPips"] = Math.Round(AvgDepsForOutputPips, s_defaultPrecision),
                    ["AvgInputsForInputPips"] = Math.Round(AvgInputsForInputPips, s_defaultPrecision),
                    ["AvgInputsForOutputPips"] = Math.Round(AvgInputsForOutputPips, s_defaultPrecision),
                    ["AvgOutputsForInputPips"] = Math.Round(AvgOutputsForInputPips, s_defaultPrecision),
                    ["AvgOutputsForOutputPips"] = Math.Round(AvgOutputsForOutputPips, s_defaultPrecision),
                    ["AvgPriorityForInputPips"] = Math.Round(AvgPriorityForInputPips, s_defaultPrecision),
                    ["AvgPriorityForOutputPips"] = Math.Round(AvgPriorityForOutputPips, s_defaultPrecision),
                    ["AvgWeightForInputPips"] = Math.Round(AvgWeightForInputPips, s_defaultPrecision),
                    ["AvgWeightForOutputPips"] = Math.Round(AvgWeightForOutputPips, s_defaultPrecision),
                    ["AvgTagCountForInputPips"] = Math.Round(AvgTagCountForInputPips, s_defaultPrecision),
                    ["AvgTagCountForOutputPips"] = Math.Round(AvgTagCountForOutputPips, s_defaultPrecision),
                    ["AvgSemaphoreCountForInputPips"] = Math.Round(AvgSemaphoreCountForInputPips, s_defaultPrecision),
                    ["AvgSemaphoreCountForOutputPips"] = Math.Round(AvgSemaphoreCountForOutputPips, s_defaultPrecision)
                }
            };
            return instance;
        }

        /// <summary>
        /// Represents an artifact as instance, mainly for testing purposes
        /// </summary>
        public static RandomForestInstance FromCsvString(string input, int precision)
        {
            var values = input.Split(',');
            return new RandomForestInstance()
            {
                Attributes = new Dictionary<string, double>()
                {
                    ["Class"] = s_sharingClassIds[values[0]],
                    ["SizeBytes"] = Convert.ToDouble(values[4]),
                    ["AvgInputPips"] = Math.Round(Convert.ToDouble(values[5]), precision),
                    ["AvgOutputPips"] = Math.Round(Convert.ToDouble(values[6]), precision),
                    ["AvgPositionForInputPips"] = Math.Round(Convert.ToDouble(values[7]), precision),
                    ["AvgPositionForOutputPips"] = Math.Round(Convert.ToDouble(values[8]), precision),
                    ["AvgDepsForInputPips"] = Math.Round(Convert.ToDouble(values[9]), precision),
                    ["AvgDepsForOutputPips"] = Math.Round(Convert.ToDouble(values[10]), precision),
                    ["AvgInputsForInputPips"] = Math.Round(Convert.ToDouble(values[11]), precision),
                    ["AvgInputsForOutputPips"] = Math.Round(Convert.ToDouble(values[12]), precision),
                    ["AvgOutputsForInputPips"] = Math.Round(Convert.ToDouble(values[13]), precision),
                    ["AvgOutputsForOutputPips"] = Math.Round(Convert.ToDouble(values[14]), precision),
                    ["AvgPriorityForInputPips"] = Math.Round(Convert.ToDouble(values[15]), precision),
                    ["AvgPriorityForOutputPips"] = Math.Round(Convert.ToDouble(values[16]), precision),
                    ["AvgWeightForInputPips"] = Math.Round(Convert.ToDouble(values[17]), precision),
                    ["AvgWeightForOutputPips"] = Math.Round(Convert.ToDouble(values[18]), precision),
                    ["AvgTagCountForInputPips"] = Math.Round(Convert.ToDouble(values[19]), precision),
                    ["AvgTagCountForOutputPips"] = Math.Round(Convert.ToDouble(values[20]), precision),
                    ["AvgSemaphoreCountForInputPips"] = Math.Round(Convert.ToDouble(values[21]), precision),
                    ["AvgSemaphoreCountForOutputPips"] = Math.Round(Convert.ToDouble(values[22]), precision)
                }
                
            };
        }


        /// <summary>
        /// For testing purposes, compares the real class with the prediced class
        /// </summary>
        public static bool Evaluate(RandomForestInstance instance, string predictedClass) => predictedClass == SharingClassLabels[(int)instance.Attributes["Class"]];
    }

}
