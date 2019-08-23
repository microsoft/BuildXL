using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
                    s_sharingClassIds[Queues.Count > 1 ? s_sharingClassLabels[0] : s_sharingClassLabels[1]],
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

        /// <summary>
        /// Represents an artifact as instance, mainly for testing purposes
        /// </summary>
        public Dictionary<string, double> AsInstance()
        {
            var instance = new Dictionary<string, double>
            {
                ["Class"] = s_sharingClassIds[Queues.Count > 1 ? s_sharingClassLabels[0] : s_sharingClassLabels[1]],
                ["SizeBytes"] = SizeBytes,
                ["AvgInputPips"] = AvgInputPips,
                ["AvgOutputPips"] = AvgOutputPips,
                ["AvgPositionForInputPips"] = AvgPositionForInputPips,
                ["AvgPositionForOutputPips"] = AvgPositionForOutputPips,
                ["AvgDepsForInputPips"] = AvgDepsForInputPips,
                ["AvgDepsForOutputPips"] = AvgDepsForOutputPips,
                ["AvgInputsForInputPips"] = AvgInputsForInputPips,
                ["AvgInputsForOutputPips"] = AvgInputsForOutputPips,
                ["AvgOutputsForInputPips"] = AvgOutputsForInputPips,
                ["AvgOutputsForOutputPips"] = AvgOutputsForOutputPips,
                ["AvgPriorityForInputPips"] = AvgPriorityForInputPips,
                ["AvgPriorityForOutputPips"] = AvgPriorityForOutputPips,
                ["AvgWeightForInputPips"] = AvgWeightForInputPips,
                ["AvgWeightForOutputPips"] = AvgWeightForOutputPips,
                ["AvgTagCountForInputPips"] = AvgTagCountForInputPips,
                ["AvgTagCountForOutputPips"] = AvgTagCountForOutputPips,
                ["AvgSemaphoreCountForInputPips"] = AvgSemaphoreCountForInputPips,
                ["AvgSemaphoreCountForOutputPips"] = AvgSemaphoreCountForOutputPips
            };
            return instance;
        }

        /// <summary>
        /// For testing purposes, compares the real class with the prediced class
        /// </summary>
        public static bool Evaluate(Dictionary<string, double> instance, string cl)
        {
            return s_sharingClassLabels[(int)instance["Class"]] == cl;
        } 
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
    ///The configuation necessary to create a random tree IN WEKA
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
        public int Id { get; set; }
        public string OutputClass { get; set; }
        public RandomTreeNode Parent { get; set; }
        public List<RandomTreeNode> Children { get; set; } = new List<RandomTreeNode>();
        public Predicate<Dictionary<string, double>> EvaluationPredicate { get; set; }
        public int Level { get; set; }

        internal bool Evaluate(Dictionary<string, double> instance) => EvaluationPredicate.Invoke(instance);

        internal static RandomTreeNode BuildFromString(string predicateLine, int id)
        {
            var node = new RandomTreeNode
            {
                // get the level, i.e., the number of '|' in the string
                Level = DetermineLevel(predicateLine),
                Id = id
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

    /// <summary>
    /// A random tree, as a set of interconnected nodes
    /// </summary>
    public class RandomTree
    {
        /// <summary>
        /// These are the roots of the tree, since this kind of tree is not necessarily single root
        /// </summary>
        public List<RandomTreeNode> Roots { get; set; } = new List<RandomTreeNode>();

        internal string Evaluate(Dictionary<string, double> instance)
        {
            foreach (var root in Roots)
            {
                var evaluation = Evaluate(root, instance);
                if (evaluation != RandomTreeNode.NoClass)
                {
                    return evaluation;
                }
            }
            return RandomTreeNode.NoClass;
        }

        internal string Evaluate(RandomTreeNode node, Dictionary<string, double> instance)
        {
            var stack = new Stack<RandomTreeNode>();
            stack.Push(node);
            while (stack.Any())
            {
                var next = stack.Pop();
                if (next.Evaluate(instance))
                {
                    if (next.OutputClass == null)
                    {
                        foreach (var child in next.Children)
                        {
                            stack.Push(child);
                        }
                    }
                    else
                    {
                        return next.OutputClass;
                    }
                }
            }
            // tree could not classify instance...
            return RandomTreeNode.NoClass;
        }

        internal static RandomTree FromStream(StreamReader reader, string firstLine)
        {
            var output = new RandomTree();
            var keepReading = true;
            RandomTreeNode previous = null;
            int initialId = 0;
            bool first = true;
            while (keepReading)
            {
                ++initialId;
                try
                {
                    var line = first ? firstLine : reader.ReadLine();
                    var node = RandomTreeNode.BuildFromString(line, initialId);
                    if (node.Level == 0)
                    {
                        // for roots
                        output.Roots.Add(node);
                        previous = node;
                        first = false;
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
                            while (previous != null)
                            {
                                if (previous.Level == node.Level)
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
                #pragma warning disable ERP022
                catch (Exception)
                {
                    // we could not read, so we are in the line after we finished.. how do we know? cause we could not build a predicate
                    break;
                }
                #pragma warning enable ERP022
            }
            // done...
            return output;
        }


    }

    /// <summary>
    /// A random forest, which is a collection of decision trees
    /// </summary>
    public class RandomForest
    {
        /// <summary>
        /// The trees that comprise the forest
        /// </summary>
        public List<RandomTree> Trees { get; set; } = new List<RandomTree>();
        /// <summary>
        /// The set of classes this kind of classifier can output
        /// </summary>
        public HashSet<string> Classes { get; set; }
        /// <summary>
        /// Constructors
        /// </summary>
        public RandomForest(HashSet<string> classes)
        {
            Classes = classes;
        }

        /// <summary>
        /// Returns the class with most votes among all the trees
        /// </summary>
        public string Classify(Dictionary<string, double> instance)
        {
            var votes = VoteCounter();
            foreach (var tree in Trees)
            {
                votes[tree.Evaluate(instance)] += 1;
            }
            return votes.Count > 0 ? votes.Aggregate((l, r) => l.Value > r.Value ? l : r).Key : RandomTreeNode.NoClass;
        }

        /// <summary>
        /// Returns the class with most votes among all the trees. It uses multiple threads to evaluate 
        /// </summary>
        public string Classify(Dictionary<string, double> instance, int parallelism)
        {
            var votes = ParallelVoteCounter();
            var options = new ParallelOptions() { MaxDegreeOfParallelism = parallelism };
            Parallel.ForEach(Trees, options, tree =>
            {
                votes[tree.Evaluate(instance)] += 1;
            });
            return votes.Count > 0 ? votes.Aggregate((l, r) => l.Value > r.Value ? l : r).Key : RandomTreeNode.NoClass;
        }

        private ConcurrentDictionary<string, int> VoteCounter()
        {
            var votes = new ConcurrentDictionary<string, int>();
            foreach (var cl in Classes)
            {
                votes[cl] = 0;
            }
            votes[RandomTreeNode.NoClass] = 0;
            return votes;
        }

        private ConcurrentDictionary<string, int> ParallelVoteCounter()
        {
            var votes = new ConcurrentDictionary<string, int>();
            foreach (var cl in Classes)
            {
                votes[cl] = 0;
            }
            votes[RandomTreeNode.NoClass] = 0;
            return votes;
        }

        /// <summary>
        /// Parses a random forest from a weka output file
        /// </summary>
        public static RandomForest FromWekaFile(string file, HashSet<string> classes)
        {
            var output = new RandomForest(classes);
            var reader = new StreamReader(file);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                // keep reading until we find one of these. This one is followed
                // by another line with equal signs and then empty lines until we find the first text
                if (line.StartsWith("RandomTree"))
                {
                    string firstLine;
                    reader.ReadLine();
                    while ((firstLine = reader.ReadLine()).Trim().Length == 0)
                    {
                        continue;
                    }
                    // now, we can start reading the tree
                    output.Trees.Add(RandomTree.FromStream(reader, firstLine));
                }
            }
            reader.Close();
            return output;
        }
    }


}
