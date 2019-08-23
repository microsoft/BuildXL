using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
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
                    Queues.Count > 1 ? 0 : 1,
                    Queues.Count,
                    Builds.Count,
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
    }

    /// <summary>
    ///The configuation necessary to run weka as a process
    /// </summary>
    public sealed class WekaConfiguration
    {
        /// <summary>
        /// Weka jar file, fullpath
        /// </summary>
        public string WekaJar { get; set; }
        /// <summary>
        /// Weka jar file, fullpath
        /// </summary>
        public string WekaCsvToArffCommand { get; set; }
        /// <summary>
        /// Weka jar file, fullpath
        /// </summary>
        public string WekaRunRTCommand { get; set; }
        /// <summary>
        /// Values related to the random tree creation
        /// </summary>
        public RandomTreeConfiguration RandomTreeConfig { get; set; }
        /// <inheritdoc />
        public override string ToString()
        {
            return new StringBuilder()
                .Append("WekaJar=").Append(WekaJar).Append(", ")
                .Append("WekaCsvToArffCommand=").Append(WekaCsvToArffCommand).Append(", ")
                .Append("WekaRunRTCommand=").Append(WekaRunRTCommand).Append(", ")
                .Append("RandomTreeConfig=[").Append(RandomTreeConfig).Append("]")
                .ToString();
        }
    }

    /// <summary>
    ///The configuation necessary to run a random tree
    /// </summary>
    public sealed class RandomTreeConfiguration
    {
       
    }

    /// <summary>
    /// A random tree node
    /// </summary>
    public class RandomTreeNode
    {
        public static readonly string NoClass = "NOT_FROM_CLASS";
        public string OutputClass { get; set; } = null;
        public RandomTreeNode Parent { get; set; }
        public List<RandomTreeNode> Children { get; set; } = new List<RandomTreeNode>();
        public Predicate<Dictionary<string, double>> EvaluationPredicate { get; set; }
        public int Level { get; set; }

        public bool Evaluate(Dictionary<string, double> instance) => EvaluationPredicate.Invoke(instance);

        public static RandomTreeNode BuildFromString(string predicateLine)
        {
            var node = new RandomTreeNode
            {
                // get the level, i.e., the number of '|' in the string
                Level = DetermineLevel(predicateLine)
            };
            // parse predicate. They look like: |   AvgDepsForOutputPips < 3446.5
            // or: AvgTagCountForOutputPips < 10.13 : Shared (4/1)
            var predicateText = predicateLine.Substring(predicateLine.Contains("|") ? predicateLine.LastIndexOf("|") + 1 : 0).Trim();
            // now, split by spaces 
            var pieces = predicateText.Split(' ');
            // here, 0 is the name, 1 is the op and 2 is the value
            node.EvaluationPredicate = BuildPredicate(pieces[0], pieces[1], pieces[2]);
            // now, check if this outputs something
            if (predicateLine.Contains(":"))
            {
                node.OutputClass = pieces[4];
            }
            return node;
        }

        private static int DetermineLevel(string predicateLine) => predicateLine.Count(e => e == '|');

        private static Predicate<Dictionary<string, double>> BuildPredicate(string attr, string operation, string value)
        {
            switch (operation)
            {
                case ">": return instance => instance[attr] > Convert.ToDouble(value);
                case ">=": return instance => instance[attr] >= Convert.ToDouble(value);
                case "<": return instance => instance[attr] < Convert.ToDouble(value);
                case "<=": return instance => instance[attr] <= Convert.ToDouble(value);
                case "=": return instance => instance[attr] == Convert.ToDouble(value);
            }
            throw new Exception($"Unknown operator, could not create predicate ({operation})");
        }
    }

    public class RandomTree
    {
        public List<RandomTreeNode> Roots { get; set; }

        public static RandomTree FromStream(StreamReader reader)
        {
            var output = new RandomTree();
            var keepReading = true;
            RandomTreeNode previous = null;
            while (keepReading){
                try
                {
                    var node = RandomTreeNode.BuildFromString(reader.ReadLine());
                    if(node.Level == 0)
                    {
                        // for roots
                        node.Parent = null;
                        output.Roots.Add(node);
                        previous = node;
                    }
                    else
                    {
                        // count the levels
                        if (node.Level > previous.Level)
                        {
                            // child
                            previous.Children.Add(node);
                            node.Parent = previous;
                            previous = node;
                        }
                        else if (node.Level < previous.Level)
                        {
                            // predecessor: travel backwards and look for the first parent 
                            // with level = level
                            while(previous != null)
                            {
                                if(previous.Level == node.Level)
                                {
                                    // we found its sibling
                                    node.Parent = previous.Parent;
                                    previous = node;
                                    break;
                                }
                                previous = previous.Parent;
                            }
                        }
                        else
                        {
                            // same level, they are siblings
                            previous.Parent.Children.Add(node);
                            node.Parent = previous.Parent;
                            previous = node;
                        }
                        
                    }
                    
                }
                catch(Exception)
                {
                    // we could not read, so we are in the line after we finished...
                    // how do we know? cause we could not build a predicate
                    break;
                }
            }
            // done...
            return output;
        }
       

    }
   

}
