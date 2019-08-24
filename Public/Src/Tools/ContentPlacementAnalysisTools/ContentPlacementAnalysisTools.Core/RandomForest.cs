using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ContentPlacementAnalysisTools.Core
{
    /// <summary>
    ///The configuation necessary to operate a random tree
    /// </summary>
    public sealed class RandomTreeConfiguration
    {
        /// <summary>
        ///  When creating tree with weka, which columns of the datasets to remove (1 based)
        /// </summary>
        public string RemovedColumns { get; set; }
        /// <summary>
        ///  The number of random trees weka will create
        /// </summary>
        public int RandomTreeCount { get; set; }
        /// <summary>
        ///  The percentage that will be kept in bag (weka)
        /// </summary>
        public int BagSizePercentage { get; set; }
        /// <summary>
        ///  How many threads to use when creating (weka)
        /// </summary>
        public int MaxCreationParallelism { get; set; }
        /// <summary>
        ///  When using a tree, if this is greater than zero it will clasiffy an instance in parallel with this many threads
        /// </summary>
        public int MaxClassificationParallelism { get; set; }
        /// <summary>
        ///  The classes the forst can output (classification)
        /// </summary>
        public string Classes { get; set; }
        /// <inheritdoc />
        public override string ToString()
        {
            return new StringBuilder()
                    .Append("RemovedColumns=").Append(RemovedColumns).Append(", ")
                    .Append("RandomTreeCount=").Append(RandomTreeCount).Append(", ")
                    .Append("BagSizePercentage=").Append(BagSizePercentage).Append(", ")
                    .Append("MaxCreationParallelism=").Append(MaxCreationParallelism).Append(", ")
                    .Append("MaxClassificationParallelism=").Append(MaxClassificationParallelism)
                    .ToString();
        }
        /// <summary>
        ///  Gets the classes we will use for classification
        /// </summary>
        public HashSet<string> ClassificationClasses()
        {
            return new HashSet<string>(Classes.Split(','));
        }
    }

    /// <summary>
    /// A random forest, which is a collection of decision trees
    /// </summary>
    public class RandomForest
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly int s_defaultPrecision = 10;
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
        /// Evaluates a batch of TRAINING instances and computes a confusion matrix
        /// </summary>
        public void EvaluateOnTrainingSet(string trainingFile, bool hasHeaders, bool parallel, int maxParalellism)
        {
            // parse the instances
            s_logger.Info($"Parsing instances from [{trainingFile}]");
            var rdr = new StreamReader(trainingFile);
            var instances = new List<Dictionary<string, double>>();
            string line;
            while ((line = rdr.ReadLine()) != null)
            {
                if (hasHeaders)
                {
                    // skip column headers
                    hasHeaders = false;
                    continue;
                }
                instances.Add(MLArtifact.FromCsvString(line, s_defaultPrecision));
            }
            // now start evaluating them
            s_logger.Info($"Evaluation starting...");
            var timer = Stopwatch.StartNew();
            var confusion = new Dictionary<string, int>();
            foreach(var cl in Classes)
            {
                confusion[$"{cl}-true"]= 0;
                confusion[$"{cl}-false"] = 0;
            }
            foreach(var instance in instances)
            {
                var prediction = parallel ? Classify(instance, maxParalellism) : Classify(instance);
                if(MLArtifact.Evaluate(instance, prediction))
                {
                    confusion[$"{prediction}-true"] += 1;
                }
                else
                {
                    confusion[$"{prediction}-false"] += 1;
                }
            }
            timer.Stop();
            var elapsedEvaluationMillis = timer.ElapsedMilliseconds;
            var perInstanceTime = elapsedEvaluationMillis * 1.0 / instances.Count * 1.0;
            s_logger.Info($"Times: Total={elapsedEvaluationMillis}ms, PerInstance={perInstanceTime}ms");
            s_logger.Info("Confusion matrix :");
            foreach(var entry in confusion)
            {
                s_logger.Info($"{entry.Key}: {entry.Value}");
            }
            var correctlyClassified = 0.0;
            var errors = 0.0;
            foreach (var entry in confusion)
            {
                if (entry.Key.Contains("-true"))
                {
                    correctlyClassified += entry.Value;
                }
                else
                {
                    errors += entry.Value;
                }
            }
            s_logger.Info($"Overral accuracy: {(correctlyClassified) / (correctlyClassified + errors)}");
            rdr.Close();


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
            votes[RandomTreeNode.NoClass] = 0;
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
            votes[RandomTreeNode.NoClass] = 0;
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
            int id = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                // keep reading until we find one of these. This one is followed
                // by another line with equal signs and then empty lines until we find the first text
                if (line.StartsWith("RandomTree"))
                {
                    ++id;
                    string firstLine;
                    reader.ReadLine();
                    while ((firstLine = reader.ReadLine()).Trim().Length == 0)
                    {
                        continue;
                    }
                    // now, we can start reading the tree
                    output.Trees.Add(RandomTree.FromStream(reader, firstLine, s_defaultPrecision, id));
                }
            }
            reader.Close();
            return output;
        }
        /// <summary>
        /// Logs a forest, for debug purposes
        /// </summary>
        public void LogForest()
        {
            s_logger.Info($"Forest ({Trees.Count})");
            foreach(var tree in Trees)
            {
                tree.LogTree();
            }
        }
    }

    /// <summary>
    /// A random tree, as a set of interconnected nodes
    /// </summary>
    public class RandomTree
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// These are the roots of the tree, since this kind of tree is not necessarily single root
        /// </summary>
        internal List<RandomTreeNode> Roots { get; set; } = new List<RandomTreeNode>();
        /// <summary>
        /// Numeric id of the tree
        /// </summary>
        internal int Id { get; set; }
        /// <summary>
        /// These nuber of nodes this tree hash
        /// </summary>
        internal int NumNodes { get; set; }

        internal string Evaluate(Dictionary<string, double> instance)
        {
            var maxDepthReached = 0;
            foreach (var root in Roots)
            {
                var evaluation = Evaluate(root, instance);
                maxDepthReached = Math.Max(maxDepthReached, evaluation.Item2);
                if (evaluation.Item1 != RandomTreeNode.NoClass)
                {
                    return evaluation.Item1;
                }
            }
            // Console.WriteLine($"Instance gets {maxDepthReached} in tree {Id}");
            return RandomTreeNode.NoClass;
        }

        internal Tuple<string, int> Evaluate(RandomTreeNode node, Dictionary<string, double> instance)
        {
            var depthReached = 0;
            var stack = new Stack<RandomTreeNode>();
            stack.Push(node);
            while (stack.Any())
            {
                var next = stack.Pop();
                depthReached = Math.Max(next.Level, depthReached);
                if (next.Evaluate(instance))
                {
                    // its not a leaf
                    if (next.OutputClass == null)
                    {
                        foreach (var child in next.Children)
                        {
                            stack.Push(child);
                        }
                    }
                    // its a leaf
                    else
                    {
                        return new Tuple<string, int>(next.OutputClass, depthReached);
                    }
                }
            }
            // tree could not classify instance...
            return new Tuple<string, int>(RandomTreeNode.NoClass, depthReached);
        }

        internal static RandomTree FromStream(StreamReader reader, string firstLine, int nodePrecision, int id)
        {
            var output = new RandomTree()
            {
                Id = id
            };
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
                    var node = RandomTreeNode.BuildFromString(line, initialId, nodePrecision);
                    output.NumNodes =+ 1;
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
                        }
                        else if (node.Level < previous.Level)
                        {
                            // predecessor: travel backwards and look for the first parent with level = level
                            while (previous != null)
                            {
                                if (previous.Level == node.Level - 1)
                                {
                                    // we found its parent
                                    previous.Children.Add(node);
                                    node.Parent = previous;
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
                        }
                        previous = node;
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

        internal void LogTree()
        {
            s_logger.Info($"TreeId={Id}");
            foreach(var root in Roots)
            {
                LogTree(root);
            }
        }

        private void LogTree(RandomTreeNode root)
        {
            var stack = new Stack<RandomTreeNode>();
            stack.Push(root);
            while (stack.Any())
            {
                var next = stack.Pop();
                next.LogNode();
                foreach (var child in next.Children)
                {
                    stack.Push(child);
                }
            }
        }
    }


    /// <summary>
    /// A random tree node
    /// </summary>
    public class RandomTreeNode
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        internal static readonly string NoClass = "OUT_OF_RANGE";
        internal int Id { get; set; }
        internal string OutputClass { get; set; } = null;
        internal RandomTreeNode Parent { get; set; }
        internal List<RandomTreeNode> Children { get; set; } = new List<RandomTreeNode>();
        internal Predicate<Dictionary<string, double>> EvaluationPredicate { get; set; }
        internal int Level { get; set; }

        internal bool Evaluate(Dictionary<string, double> instance) => EvaluationPredicate.Invoke(instance);

        internal static RandomTreeNode BuildFromString(string predicateLine, int id, int precision)
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
            node.EvaluationPredicate = BuildPredicate(pieces[0], pieces[1], pieces[2], precision);
            // now, check if this outputs something
            if (predicateLine.Contains(":"))
            {
                node.OutputClass = pieces[4];
            }
            return node;
        }

        internal void LogNode()
        {
            var tabs = new StringBuilder();
            for (int i = 0; i < Level; ++i)
            {
                tabs.Append(" ");
            }
            s_logger.Info($"{tabs.ToString()}Id={Id}, Level={Level}, Parent={(Parent != null ? Parent.Id : -1)}");
        }

        private static int DetermineLevel(string predicateLine) => predicateLine.Count(e => e == '|');

        private static Predicate<Dictionary<string, double>> BuildPredicate(string attr, string operation, string value, int precision)
        {
            switch (operation)
            {
                case ">": return instance => instance[attr] > Math.Round(Convert.ToDouble(value), precision);
                case ">=": return instance => instance[attr] >= Math.Round(Convert.ToDouble(value), precision);
                case "<": return instance => instance[attr] < Math.Round(Convert.ToDouble(value), precision);
                case "<=": return instance => instance[attr] <= Math.Round(Convert.ToDouble(value), precision);
                case "=": return instance => instance[attr] == Math.Round(Convert.ToDouble(value), precision);
            }
            throw new Exception($"Unknown operator, could not create predicate ({operation})");
        }
    }

}
