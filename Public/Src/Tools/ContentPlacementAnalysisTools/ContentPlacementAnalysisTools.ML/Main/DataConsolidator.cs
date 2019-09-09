using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks.Dataflow;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Collections;
using ContentPlacementAnalysisTools.Core.ML;
using ContentPlacementAnalysisTools.Core.ML.Classifier;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlacementAnalysisTools.ML.Action;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.ML.Main
{
    /// <summary>
    /// The goal of this entry point is to create a usable random forest by sampling the collected data from
    /// some builds. To do this, the steps are:
    /// a) Creating a database, this is, reading all the per-build json files and group the artifacts
    /// by hash. The output of this process is a large set of directories (one per hash).
    /// b) Linearizing (this is, one row per hash) each hash into a global database (a csv file) and a set
    /// of samples (see /help). These samples should have a size thats 5-10% of the universe.
    /// c) build (train) random forests using this samples (and the samples only). The output
    /// here is a text file (wtree) that represents a ransom forest.
    /// d) evaluate this trees (compare their performance against UNSEEN samples).
    /// Notive that d) is optional, since after c) the forests are built and ready to be loaded.
    /// </summary>
    public class DataConsolidator
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private static void Main(string[] args)
        {
            // parse args
            var arguments = new Args(args);
            if (arguments.Help)
            {
                return;
            }
            s_logger.Info($"DataConsolidator starting");
            try
            {
                s_logger.Info($"Using configuration [{arguments.AppConfig}]");
                if (!(arguments.BuildRandomForestOnly || arguments.LinearizeOnly || arguments.EvaluateRandomForestOnly || arguments.EvaluateContentPlacementClassifierOnly))
                {
                    CreateDatabase(arguments);
                }
                if (arguments.LinearizeOnly)
                {
                    LinearizeDatabase(arguments);
                }
                if (arguments.BuildRandomForestOnly)
                {
                    BuildRandomForest(arguments);
                }
                if (arguments.EvaluateRandomForestOnly)
                {
                    EvaluateForests(arguments);
                }
                if (arguments.EvaluateContentPlacementClassifierOnly)
                {
                    EvaluateContentPlacementClassifier(arguments);
                }
            }
            finally
            {
                s_logger.Info("Done...");
            }
        }

        private static void EvaluateContentPlacementClassifier(Args arguments)
        {
            Contract.Requires(arguments.InputDirectory != null, "You must specify an input directory");
            Contract.Requires(Directory.Exists(arguments.InputDirectory), "The input directory must exist");
            var configurationFile = $"{Path.Combine(arguments.InputDirectory, "classifier.json")}";
            s_logger.Info($"Evaluating classifier from [{configurationFile}]");
            // approx memory consumption and check load time
            var initialMemory = GC.GetTotalMemory(true);
            var load = Stopwatch.StartNew();
            var classifier = new ContentPlacementClassifier(configurationFile);
            load.Stop();
            var consumedMemory = GC.GetTotalMemory(false) - initialMemory;
            s_logger.Info($"Classifier loaded in {load.ElapsedMilliseconds}ms, approxBytes={consumedMemory}");
            var numInstances = 0;
            var random = new Random(Environment.TickCount);
            // read some queue names
            var qNames = new List<string>();
            var instances = new Dictionary<ContentPlacementInstance, List<string>>();
            var uniqueMachines = new HashSet<string>();
            foreach (var qq in Directory.EnumerateFiles(Path.Combine(arguments.InputDirectory, "QueueMap")))
            {
                qNames.Add(Path.GetFileNameWithoutExtension(qq));
                ++numInstances;
            }
            // now test for some instances. Just some random instances, one per queue
            var ns = 0;
            var na = 0;
            var classify = Stopwatch.StartNew();
            foreach (var queueName in qNames)
            {
                var instance = new ContentPlacementInstance()
                {
                    QueueName = queueName,
                    Artifact = new RandomForestInstance()
                    {
                        Attributes = new Dictionary<string, double>()
                        {
                            ["SizeBytes"] = random.Next(0, 1000000000),
                            ["AvgInputPips"] = random.Next(0, 100000),
                            ["AvgOutputPips"] = random.Next(0, 100000),
                            ["AvgPositionForInputPips"] = random.NextDouble(),
                            ["AvgPositionForOutputPips"] = random.NextDouble(),
                            ["AvgDepsForInputPips"] = random.Next(0, 10000),
                            ["AvgDepsForOutputPips"] = random.Next(0, 10000),
                            ["AvgInputsForInputPips"] = random.Next(0, 100000),
                            ["AvgInputsForOutputPips"] = random.Next(0, 100000),
                            ["AvgOutputsForInputPips"] = random.Next(0, 100000),
                            ["AvgOutputsForOutputPips"] = random.Next(0, 100000),
                            ["AvgPriorityForInputPips"] = random.Next(0, 100),
                            ["AvgPriorityForOutputPips"] = random.Next(0, 100),
                            ["AvgWeightForInputPips"] = random.Next(0, 100),
                            ["AvgWeightForOutputPips"] = random.Next(0, 100),
                            ["AvgTagCountForInputPips"] = random.Next(0, 100),
                            ["AvgTagCountForOutputPips"] = random.Next(0, 100),
                            ["AvgSemaphoreCountForInputPips"] = random.Next(0, 100),
                            ["AvgSemaphoreCountForOutputPips"] = random.Next(0, 100)
                        }
                    }
                };

                var result = classifier.Classify(instance);

                if (result.Succeeded)
                {
                    instances.Add(instance, result.Value);
                }

                switch (result.ReturnCode)
                {
                    case ContentPlacementClassifierResult.ResultCode.ArtifactNotShared:
                        ns++;
                        break;
                    case ContentPlacementClassifierResult.ResultCode.NoAlternativesForQueue:
                        na++;
                        break;
                    default:
                        break;
                }
            }
            classify.Stop();
            s_logger.Info($"Classifier ({numInstances} instances, {ns} not shared, {na} without alternatives) done in {classify.ElapsedMilliseconds}ms (perInstanceAvg={(1.0 * classify.ElapsedMilliseconds) / (1.0 * numInstances)}ms)");
            foreach(var kvp in instances)
            {
                var instance = kvp.Key;
                var predictedClasses = kvp.Value;
                var unique = new HashSet<string>(predictedClasses).Count;
                var real = predictedClasses.Count;
                if(unique != real)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }
                s_logger.Info($"queue={instance.QueueName}, count={real}, uniqueCount={unique}, alternatives=[{string.Join(",", predictedClasses)}]");
                Console.ResetColor();
                uniqueMachines.AddRange(predictedClasses);
            }
            foreach(var qq in classifier.AlternativesPerQueue())
            {
                uniqueMachines.AddRange(qq.Value);
            }
            s_logger.Info($"totalMachinesAvailable={uniqueMachines.Count}, avg={(1.0 * uniqueMachines.Count) /(1.0 * classifier.AlternativesPerQueue().Count)} per queue");

        }

        private static void EvaluateForests(Args arguments)
        {
            Contract.Requires(arguments.InputDirectory != null, "You must specify an input directory");
            Contract.Requires(Directory.Exists(arguments.InputDirectory), "The input directory must exist");
            // we have a bunch of forests and we will compare them by classifying against
            // unknown samples. The forests are in weka format (wtree)
            s_logger.Info("Evaluating random forests...");
            // create the tree from the sample csvs...
            var treeEvaluationBlock = new ActionBlock<string>(i =>
            {
                var classifier = RandomForest.FromWekaFile(i);
                s_logger.Info($"Evaluating forest from [{i}] against universe");
                // and now evaluate
                foreach(var evaluationFile in Directory.EnumerateFiles(arguments.InputDirectory, "*.csv"))
                {
                    if (!evaluationFile.Contains("-sample"))
                    {
                        classifier.EvaluateOnTrainingSet(evaluationFile, true, false, 0);
                    }
                }
            },
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = 1
                }
            );
            // post each action
            foreach (var treeFile in Directory.EnumerateFiles(arguments.InputDirectory, "*.wtree"))
            {
                treeEvaluationBlock.Post(treeFile);
            }
            // complete
            treeEvaluationBlock.Complete();
            // wait
            treeEvaluationBlock.Completion.Wait();
            // done...
        }

        private static void BuildRandomForest(Args arguments)
        {
            Contract.Requires(arguments.InputDirectory != null, "You must specify an input directory");
            Contract.Requires(Directory.Exists(arguments.InputDirectory), "The input directory must exist");
            s_logger.Info("Building random forests...");
            // create the tree from the sample csvs...
            var treeCreationBlock = new ActionBlock<RandomForestFromWekaInput>(i =>
            {
                var action = new RandomForestFromWeka(arguments.AppConfig);
                action.PerformAction(i);
            },
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxForestCreationTasks
                }
            );
            // post each action
            foreach (var trainingFile in Directory.EnumerateFiles(arguments.InputDirectory, "*.csv"))
            {
                if (trainingFile.Contains("-sample"))
                {
                    var input = new RandomForestFromWekaInput(trainingFile);
                    treeCreationBlock.Post(input);
                }
            }
            // complete
            treeCreationBlock.Complete();
            // wait
            treeCreationBlock.Completion.Wait();
            // done...
        }

        private static void LinearizeDatabase(Args arguments)
        {
            // and a couple of checks here
            Contract.Requires(arguments.InputDirectory != null, "You must specify an input directory");
            Contract.Requires(Directory.Exists(arguments.InputDirectory), "The input directory must exist");
            var collectedArtifacts = new MultiValueDictionary<int, MLArtifact>();
            var currentTicks = Environment.TickCount;
            var linearFile = $"{Path.Combine(arguments.InputDirectory, $"{Convert.ToString(currentTicks)}.csv")}";
            var linearOutput = TextWriter.Synchronized(new StreamWriter(linearFile));
            s_logger.Info($"Linearizing to [{linearFile}]");
            // write the headers
            MLArtifact.WriteColumnsToStream(linearOutput);
            // so now we are ready to linearize
            var linearizeBlock = new TransformBlock<LinearizeArtifactsInput, TimedActionResult<LinearizeArtifactsOutput>>(i =>
            {
                var action = new LinearizeArtifacts(linearOutput);
                return action.PerformAction(i);

            },
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxArtifactLinearizationTasks
                }
            );
            var collectLinearResultsBlock = new ActionBlock<TimedActionResult<LinearizeArtifactsOutput>>(i =>
            {
                if (i.ExecutionStatus)
                {
                    collectedArtifacts.Add(i.Result.NumQueues, i.Result.Linear);
                }

            },
                // enforce serial
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = 1
                }
            );
            // connect
            linearizeBlock.LinkTo(collectLinearResultsBlock, new DataflowLinkOptions { PropagateCompletion = true });
            // and post the tasks
            var posted = 0;
            foreach (var hashDir in Directory.EnumerateDirectories(arguments.InputDirectory))
            {
                linearizeBlock.Post(new LinearizeArtifactsInput(hashDir));
                ++posted;
            }
            s_logger.Info($"Posted {posted} linearizing tasks, waiting...");
            linearizeBlock.Complete();
            // and wait
            collectLinearResultsBlock.Completion.Wait();
            // and close...
            linearOutput.Close();
            // now, scale to create the samples...
            s_logger.Info($"Creating {arguments.NumSamples} samples of size {arguments.SampleSize}");
            var scale = new Dictionary<int, int>();
            foreach (var entry in collectedArtifacts)
            {
                var queueCount = entry.Key;
                var entryCount = entry.Value.Count;
                var proportion = 1.0 * Math.BigMul(entryCount, arguments.SampleSize) / (1.0 * posted);
                scale[queueCount] = (int)Math.Ceiling(proportion);
            }
            // we have the scale, lets post tasks here
            var createSampleBlocks = new ActionBlock<SampleArtifactsInput>(i =>
            {
                var action = new SampleArtifacts();
                action.PerformAction(i);

            },
                // one per each sample
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = arguments.NumSamples
                }
            );
            // post some tasks in here
            for (var i = 0; i < arguments.NumSamples; ++i)
            {
                createSampleBlocks.Post(new SampleArtifactsInput($"{Path.Combine(arguments.InputDirectory, $"{Convert.ToString(currentTicks)}-sample{i}.csv")}", scale, collectedArtifacts));
            }
            // and wait...
            createSampleBlocks.Complete();
            createSampleBlocks.Completion.Wait();
            // done...
        }

        private static void CreateDatabase(Args arguments)
        {
            // and a couple of checks here
            Contract.Requires(arguments.OutputDirectory != null, "You must specify an output directory");
            Contract.Requires(arguments.InputDirectory != null, "You must specify an input directory");
            Contract.Requires(Directory.Exists(arguments.OutputDirectory), "The output directory must exist");
            Contract.Requires(Directory.Exists(arguments.InputDirectory), "The input directory must exist");
            // create the pipeline. The first step here is to parse the input files, and we can do this in parallel
            var buildArtifactParsingBlock = new TransformBlock<ParseBuildArtifactsInput, TimedActionResult<ParseBuildArtifactsOutput>>(i =>
            {
                var action = new ParseBuildArtifacts();
                return action.PerformAction(i);
            },
                 new ExecutionDataflowBlockOptions()
                 {
                     MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxBuildParsingTasks
                 }
            );
            // the second is to save artifacts in a central folder
            var storeArtifactBlock = new ActionBlock<TimedActionResult<ParseBuildArtifactsOutput>>(i =>
            {
                // the exception will be logged even if we dont do it here
                if (i.ExecutionStatus)
                {
                    var action = new StoreBuildArtifacts(arguments.OutputDirectory);
                    action.PerformAction(i.Result);
                }

            },
                 new ExecutionDataflowBlockOptions()
                 {
                     MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxArtifactStoreTasks
                 }
            );
            // link them
            var numParsingTasks = 0;
            buildArtifactParsingBlock.LinkTo(storeArtifactBlock, new DataflowLinkOptions { PropagateCompletion = true });
            // do now we can post to the initial queue
            foreach (var file in Directory.EnumerateFiles(arguments.InputDirectory, "*.json"))
            {
                buildArtifactParsingBlock.Post(new ParseBuildArtifactsInput(file));
                ++numParsingTasks;
            }
            s_logger.Info($"Posted {numParsingTasks} parsing tasks, processing");
            // now wait
            buildArtifactParsingBlock.Complete();
            storeArtifactBlock.Completion.Wait();
            // done
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
            /// GB of memory used to run weka
            /// </summary>
            public int MemoryGB { get; set; }
            /// <summary>
            /// Weka command to run forest creation
            /// </summary>
            public string WekaRunRTCommand { get; set; }
            /// <summary>
            /// Weka command to create arff file
            /// </summary>
            public string WekaRunArffCommand { get; set; }
            /// <summary>
            /// Values related to the random tree creation
            /// </summary>
            public RandomTreeConfiguration RandomTreeConfig { get; set; }
            /// <inheritdoc />
            public override string ToString()
            {
                return new StringBuilder()
                    .Append("WekaJar=").Append(WekaJar).Append(", ")
                    .Append("MemoryGB=").Append(MemoryGB).Append(", ")
                    .Append("WekaRunArffCommand=").Append(WekaRunArffCommand).Append(", ")
                    .Append("WekaRunRTCommand=").Append(WekaRunRTCommand).Append(", ")
                    .Append("RandomTreeConfig=[").Append(RandomTreeConfig).Append("]")
                    .ToString();
            }
        }

        /// <summary>
        /// Represents the configuration file of this app. A configuration file has the form
        /// {
        ///     "ConcurrencyConfig":{
        ///         // attrs of ConcurrencyConfiguration class    
        ///     }
        /// }
        /// </summary>
        public sealed class ApplicationConfiguration
        {
            /// <summary>
            /// Concurrency settings for the pipeline
            /// </summary>
            public ConcurrencyConfiguration ConcurrencyConfig { get; set; }
            /// <summary>
            /// Weka configuration
            /// </summary>
            public WekaConfiguration WekaConfig { get; set; }
            /// <summary>
            /// Build from a json string
            /// </summary>
            public static ApplicationConfiguration FromJson(string json) => JsonConvert.DeserializeObject<ApplicationConfiguration>(File.ReadAllText(json));
            /// <inheritdoc />
            public override string ToString()
            {
                return new StringBuilder()
                    .Append("ConcurrencyConfig=[").Append(ConcurrencyConfig).Append("], ")
                    .Append("WekaConfig=[").Append(WekaConfig).Append("]")
                    .ToString();
            }
        }

        /// <summary>
        /// Concurrency configuration for this application. You can specify the maximum number of threads processing tasks
        /// for each piece of the pipeline
        /// </summary>
        public sealed class ConcurrencyConfiguration
        {
            /// <summary>
            /// Maximum number of concurrent build parsing tasks
            /// </summary>
            public int MaxBuildParsingTasks { get; set; }
            /// <summary>
            /// Maximum number of concurrent artifact store tasks
            /// </summary>
            public int MaxArtifactStoreTasks { get; set; }
            /// <summary>
            /// Maximum number of concurrent artifact store tasks
            /// </summary>
            public int MaxArtifactLinearizationTasks { get; set; }
            /// <summary>
            /// Maximum forest creation/evaluation tasks
            /// </summary>
            public int MaxForestCreationTasks { get; set; }
            /// <inheritdoc />
            public override string ToString()
            {
                return new StringBuilder()
                    .Append("MaxBuildParsingTasks=").Append(MaxBuildParsingTasks).Append(", ")
                    .Append("MaxArtifactStoreTasks=").Append(MaxArtifactStoreTasks).Append(", ")
                    .Append("MaxArtifactLinearizationTasks=").Append(MaxArtifactLinearizationTasks).Append(", ")
                    .Append("MaxForestCreationTasks=").Append(MaxForestCreationTasks)
                    .ToString();
            }
        }

        internal sealed partial class Args : CommandLineUtilities
        {
            private static readonly string[] s_helpStrings = new[] { "?", "help" };
            /// <summary>
            /// This application config, parsed from the ac argument
            /// </summary>
            public ApplicationConfiguration AppConfig = null;
            /// <summary>
            /// The input directory to take the files from
            /// </summary>
            public string InputDirectory { get; } = null;
            /// <summary>
            /// The output directory saving results
            /// </summary>
            public string OutputDirectory { get; } = null;
            /// <summary>
            /// If true, then we will read an existing database
            /// </summary>
            public bool LinearizeOnly { get; } = false;
            /// <summary>
            /// The number of samples to create from the output
            /// </summary>
            public int NumSamples { get; } = 1;
            /// <summary>
            /// The size of each sample
            /// </summary>
            public int SampleSize { get; } = 10000;
            /// <summary>
            /// If true, only the random tree creator will be invoked
            /// </summary>
            public bool BuildRandomForestOnly { get; } = false;
            /// <summary>
            /// If true, only the existing trees will be loaded and evaluated
            /// </summary>
            public bool EvaluateRandomForestOnly { get; } = false;
            /// <summary>
            /// Flag to evaluate the content placement classifier
            /// </summary>
            public bool EvaluateContentPlacementClassifierOnly { get; } = false;
            /// <summary>
            /// True if help was requested
            /// </summary>
            public bool Help { get; } = false;

            public Args(string[] args) : base(args)
            {
                foreach (var opt in Options)
                {
                    if (s_helpStrings.Any(s => opt.Name.Equals(s, StringComparison.OrdinalIgnoreCase)))
                    {
                        WriteHelp();
                        Help = true;
                        return;
                    }
                    else if (opt.Name.Equals("applicationConfig", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("ac", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = ParseStringOption(opt);
                        Contract.Requires(File.Exists(name), "You must specify a configuration file");
                        AppConfig = ApplicationConfiguration.FromJson(name);
                    }
                    else if (opt.Name.Equals("inputDirectory", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                    {
                        InputDirectory = ParseStringOption(opt);
                    }
                    else if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("od", StringComparison.OrdinalIgnoreCase))
                    {
                        OutputDirectory = ParseStringOption(opt);
                    }
                    else if (opt.Name.Equals("linearize", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("lo", StringComparison.OrdinalIgnoreCase))
                    {
                        LinearizeOnly = ParseBooleanOption(opt);
                    }
                    else if (opt.Name.Equals("numSamples", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("ns", StringComparison.OrdinalIgnoreCase))
                    {
                        NumSamples = ParseInt32Option(opt, 1, int.MaxValue);
                    }
                    else if (opt.Name.Equals("sampleSize", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("ss", StringComparison.OrdinalIgnoreCase))
                    {
                        SampleSize = ParseInt32Option(opt, 1, int.MaxValue);
                    }
                    else if (opt.Name.Equals("buildRF", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("brfo", StringComparison.OrdinalIgnoreCase))
                    {
                        BuildRandomForestOnly = ParseBooleanOption(opt);
                    }
                    else if (opt.Name.Equals("evalRF", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("erfo", StringComparison.OrdinalIgnoreCase))
                    {
                        EvaluateRandomForestOnly = ParseBooleanOption(opt);
                    }
                    else if (opt.Name.Equals("evalCPC", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("ecpco", StringComparison.OrdinalIgnoreCase))
                    {
                        EvaluateContentPlacementClassifierOnly = ParseBooleanOption(opt);
                    }
                }
                Contract.Requires(AppConfig != null, "You must specify a configuration file");
            }

            private static void WriteHelp()
            {
                var writer = new HelpWriter();
                writer.WriteBanner("cptools.ml.consolidate - Tool consolidating existing build artifact files (see cptools.builddownloader)");
                writer.WriteLine("");
                writer.WriteOption("applicationConfig", "Required. File containing the application config parameters (json)", shortName: "ac");
                writer.WriteOption("inputDirectory", "Required. The directory where the inputs will be taken from", shortName: "id");
                writer.WriteOption("outputDirectory", "Required. The directory where the outputs will be stored", shortName: "od");
                writer.WriteOption("linearize", "Optional. If true, then no input will be read, only the output directory will be linearized", shortName: "lo");
                writer.WriteOption("numSamples", "Optional. The number of samples to be taken (from the global output). Defaults to 1", shortName: "ns");
                writer.WriteOption("sampleSize", "Optional. The size of each sample. Defaults to 10000", shortName: "ss");
                writer.WriteOption("buildRF", "Optional. If the samples are already created, this will only run the random forest generator", shortName: "brfo");
                writer.WriteOption("evalRF", "Optional. If the forests are already created, this will only run the evaluation phase", shortName: "erfo");
                writer.WriteOption("evalCPC", "Optional. Evaluates the whole content placement classifier using a random forest and queue/machine maps", shortName: "ecpco");
            }
        }
    }
}
