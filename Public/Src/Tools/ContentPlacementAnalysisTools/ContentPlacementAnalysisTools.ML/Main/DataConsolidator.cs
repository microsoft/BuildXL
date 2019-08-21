using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks.Dataflow;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Collections;
using ContentPlacementAnalysisTools.Core;
using ContentPlacementAnalysisTools.ML.Action;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.ML.Main
{

    /// <summary>
    /// TODO
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
                if (!arguments.LinearizeOnly)
                {
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
                }
                var collectedArtifacts = new MultiValueDictionary<int, MLArtifact>();
                var linearFile = $"{Path.Combine(arguments.OutputDirectory, $"{Convert.ToString(Environment.TickCount)}.csv")}";
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
                foreach(var hashDir in Directory.EnumerateDirectories(arguments.OutputDirectory))
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
                    var proportion = 1.0 * (entryCount * arguments.SampleSize) / (1.0 * entryCount);
                    scale[queueCount] = (int)Math.Ceiling(proportion);
                }
                // we have the scale, lets post tasks here
                var createSampleBlocks = new ActionBlock<SampleArtifactsInput>(i =>
                {
                    

                },
                    // one per each sample
                    new ExecutionDataflowBlockOptions()
                    {
                        MaxDegreeOfParallelism = arguments.NumSamples
                    }
                );
            }
            finally
            {
                s_logger.Info("Done...");
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
            /// Build from a json string
            /// </summary>
            public static ApplicationConfiguration FromJson(string json) => JsonConvert.DeserializeObject<ApplicationConfiguration>(File.ReadAllText(json));
            /// <inheritdoc />
            public override string ToString()
            {
                return new StringBuilder()
                    .Append("ConcurrencyConfig=[").Append(ConcurrencyConfig).Append("]")
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
            /// <inheritdoc />
            public override string ToString()
            {
                return new StringBuilder()
                    .Append("MaxBuildParsingTasks=").Append(MaxBuildParsingTasks).Append(", ")
                    .Append("MaxArtifactStoreTasks=").Append(MaxArtifactStoreTasks).Append(", ")
                    .Append("MaxArtifactLinearizationTasks=").Append(MaxArtifactLinearizationTasks)
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
            public int SampleSize { get; } = -1;
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
                    else if (opt.Name.Equals("linearizeOnly", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("lo", StringComparison.OrdinalIgnoreCase))
                    {
                        LinearizeOnly = ParseBooleanOption(opt);
                    }
                    else if (opt.Name.Equals("numSamples", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("ns", StringComparison.OrdinalIgnoreCase))
                    {
                        NumSamples = ParseInt32Option(opt, 0, int.MaxValue);
                    }
                    else if (opt.Name.Equals("sampleSize", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("ss", StringComparison.OrdinalIgnoreCase))
                    {
                        SampleSize = ParseInt32Option(opt, 0, int.MaxValue);
                    }

                }
                // and a couple of checks here
                Contract.Requires(AppConfig != null, "You must specify a configuration file");
                Contract.Requires(OutputDirectory != null, "You must specify an output directory");
                Contract.Requires(InputDirectory != null, "You must specify an input directory");
                Contract.Requires(NumSamples >= 1, "You must specify at least one sample");
                Contract.Requires(SampleSize > 0, "The sample size must be positive");
                Contract.Requires(Directory.Exists(OutputDirectory), "The output directory must exist");
                Contract.Requires(Directory.Exists(InputDirectory), "The input directory must exist");
            }

            private static void WriteHelp()
            {
                var writer = new HelpWriter();
                writer.WriteBanner("cptools.ml.consolidate - Tool consolidating existing build artifact files (see cptools.builddownloader)");
                writer.WriteLine("");
                writer.WriteOption("applicationConfig", "Required. File containing the application config parameters (json)", shortName: "ac");
                writer.WriteOption("inputDirectory", "Required. The directory where the inputs will be taken from", shortName: "id");
                writer.WriteOption("outputDirectory", "Required. The directory where the outputs will be stored", shortName: "od");
                writer.WriteOption("linearizeOnly", "Optional. If true, then no input will be read, only the output directory will be linearized", shortName: "lo");
                writer.WriteOption("numSamples", "Optional. The number of samples to be taken (from the global output)", shortName: "ns");
                writer.WriteOption("sampleSize", "Optional. The size of each sample", shortName: "ns");
            }
        }
    }
}
