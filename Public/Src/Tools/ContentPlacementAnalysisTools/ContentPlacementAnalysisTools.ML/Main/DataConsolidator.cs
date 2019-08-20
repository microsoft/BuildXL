using System;
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
            // this is where we will keep the consolidated artifacts
            var consolidatedArtifacs = new MultiValueDictionary<string, ArtifactWithBuild>();
            s_logger.Info($"DataConsolidator starting");
            try
            {
                s_logger.Info($"Using configuration [{arguments.AppConfig}]");
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
                // the second one is to consolidate, and this one is serialized
                var consolidateArtifactsBlock = new ActionBlock<TimedActionResult<ParseBuildArtifactsOutput>>(i =>
                {
                    if (i.ExecutionStatus)
                    {
                        var action = new ConsolidateArtifacts(consolidatedArtifacs);
                        action.PerformAction(i.Result);
                    }
                    else
                    {
                        // its better to log this as warning here, in case of error its already on the log
                        s_logger.Warn("One of the input files could not be processed correctly, check output logs");
                    }

                },
                    // enforce serial
                    new ExecutionDataflowBlockOptions()
                    {
                        MaxDegreeOfParallelism = 1
                    }
                );
                // link them
                var numParsingTasks = 0;
                buildArtifactParsingBlock.LinkTo(consolidateArtifactsBlock, new DataflowLinkOptions { PropagateCompletion = true });
                // do now we can post to the initial queue
                foreach(var file in Directory.EnumerateFiles(arguments.InputDirectory, "*.json"))
                {
                    buildArtifactParsingBlock.Post(new ParseBuildArtifactsInput(file));
                    ++numParsingTasks;
                }
                s_logger.Info($"Posted {numParsingTasks} parsing tasks, processing");
                // now wait
                buildArtifactParsingBlock.Complete();
                consolidateArtifactsBlock.Completion.Wait();
                // and now we can continue
                s_logger.Info($"Consolidated {consolidatedArtifacs.Count} artifacts from {numParsingTasks} files, linearizing...");

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
            /// <inheritdoc />
            public override string ToString()
            {
                return new StringBuilder()
                    .Append("MaxBuildParsingTasks=").Append(MaxBuildParsingTasks)
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
                        string name = ParseStringOption(opt);
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

                }
                // and a couple of checks here
                Contract.Requires(AppConfig != null, "You must specify a configuration file");
                Contract.Requires(OutputDirectory != null, "You must specify an output directory");
                Contract.Requires(InputDirectory != null, "You must specify an input directory");
                Contract.Requires(Directory.Exists(OutputDirectory), "The output directory must exist");
                Contract.Requires(Directory.Exists(InputDirectory), "The input directory must exist");
            }

            private static void WriteHelp()
            {
                var writer = new HelpWriter();
                writer.WriteBanner("cptools.ml.consolidate - Tool consolidating existing build artifact files (see cptools.builddownloader)");
                writer.WriteLine("");
                writer.WriteOption("applicationConfig", "Required. File containing the application config parameters (json)", shortName: "ac");
                writer.WriteOption("outputDirectory", "Required. The directory where the outputs will be stored", shortName: "od");
            }
        }
    }
}
