using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks.Dataflow;
using BuildXL.ToolSupport;
using ContentPlacementAnalysisTools.Core.Kusto;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlacementAnalysisTools.Extraction.Action;
using ContentPlamentAnalysisTools.Core.Analyzer;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.Extraction.Main
{

    /// <summary>
    /// Main class for downloading a set of builds and getting sample artifacts from them
    /// </summary>
    public class BuildDownloader
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
            s_logger.Info($"BuildDownloader starting");
            try
            {
                s_logger.Info($"Using configuration [{arguments.AppConfig}]");
                // so in here we will create a new network.
                var buildInfoBlock = new TransformManyBlock<GetKustoBuildInput, List<KustoBuild>>(i =>
                {
                    var action = new GetKustoBuild(arguments.AppConfig);
                    var result = action.PerformAction(i);
                    // its not worth it to continue if this fails
                    if (!result.ExecutionStatus)
                    {
                        s_logger.Error(result.Exception, "Could not download builds");
                        throw result.Exception;
                    }
                    return result.Result.KustoBuildData;
                });
                var downloadBlock = new TransformBlock<List<KustoBuild>, TimedActionResult<DecompressionOutput>>( i => 
                {
                    var action = new BuildDownload(arguments.AppConfig, arguments.OutputDirectory);
                    return action.PerformAction(i);
                }, 
                    new ExecutionDataflowBlockOptions()
                    {
                        MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxDownloadTasks
                    }
                );
                var analysisBlock = new ActionBlock<TimedActionResult<DecompressionOutput>>(i =>
                {
                    // check
                    if (i.ExecutionStatus)
                    {
                        // analyze if decompression succeded
                        var action = new BuildAnalisys(arguments.AppConfig);
                        action.PerformAction(i.Result);
                    }
                },
                    new ExecutionDataflowBlockOptions()
                    {
                        MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxAnalysisTasks
                    }
                );
                // link them
                buildInfoBlock.LinkTo(downloadBlock, new DataflowLinkOptions { PropagateCompletion = true });
                downloadBlock.LinkTo(analysisBlock, new DataflowLinkOptions { PropagateCompletion = true });
                var input = new GetKustoBuildInput(arguments.NumBuilds, arguments.Year, arguments.Month, arguments.Day);
                // post the task...
                buildInfoBlock.Post(input);
                // and complete
                buildInfoBlock.Complete();
                // wait for the last...
                analysisBlock.Completion.Wait();
            }
            finally
            {
                s_logger.Info("Done...");
            }
        }
    }

    /// <summary>
    /// Represents the configuration file of this app. A configuration file has the form
    /// {
    ///     "AnalyzerConfig":{
    ///         // attrs of ContentPlacementAnalyzerConfig class
    ///     },
    ///     "KustoConfig":{
    ///         // attrs of KustoConnectionConfiguration class
    ///     },
    ///     "ConcurrencyConfig":{
    ///         // attrs of ConcurrencyConfiguration class    
    ///     }
    /// }
    /// </summary>
    public sealed class ApplicationConfiguration
    {
        /// <summary>
        /// Analyzer configuration that affects each analysis task
        /// </summary>
        public ContentPlacementAnalyzerConfig AnalyzerConfig { get; set; }
        /// <summary>
        /// Kusto connection config
        /// </summary>
        public KustoConnectionConfiguration KustoConfig { get; set; }
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
                .Append("AnalyzerConfig=[").Append(AnalyzerConfig).Append("], ")
                .Append("KustoConnectionConfiguration=[").Append(KustoConfig).Append("], ")
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
        /// Maximum number of concurrent download tasks
        /// </summary>
        public int MaxDownloadTasks { get; set; }
        /// <summary>
        /// Maximum number of concurrent analysis tasks
        /// </summary>
        public int MaxAnalysisTasks { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return new StringBuilder()
                .Append("MaxDownloadTasks=").Append(MaxDownloadTasks).Append(", ")
                .Append("MaxAnalysisTasks=").Append(MaxAnalysisTasks)
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
        /// The maximum number of builds to be downloaded
        /// </summary>
        public int NumBuilds { get; } = -1;
        /// <summary>
        /// Year, to build the date for downloading build info
        /// </summary>
        public int Year { get; } = -1;
        /// <summary>
        /// Month, to build the date for downloading build info
        /// </summary>
        public int Month { get; } = -1;
        /// <summary>
        /// Day, to build the date for downloading build info
        /// </summary>
        public int Day { get; } = -1;
        /// <summary>
        /// The output directory for downloading builds and saving results
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
                else if (opt.Name.Equals("year", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    Year = ParseInt32Option(opt, 0, int.MaxValue);
                }
                else if (opt.Name.Equals("month", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("m", StringComparison.OrdinalIgnoreCase))
                {
                    Month = ParseInt32Option(opt, 1, 12);
                }
                else if (opt.Name.Equals("day", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("d", StringComparison.OrdinalIgnoreCase))
                {
                    Day = ParseInt32Option(opt, 1, 31);
                }
                else if (opt.Name.Equals("numBuilds", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("nb", StringComparison.OrdinalIgnoreCase))
                {
                    NumBuilds = ParseInt32Option(opt, 1, int.MaxValue);
                }
                else if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("od", StringComparison.OrdinalIgnoreCase))
                {
                    OutputDirectory = ParseStringOption(opt);
                }

            }
            // and a couple of checks here
            Contract.Requires(NumBuilds > 0, "You must specify a number of builds");
            Contract.Requires(Year > 0, "You must specify a year");
            Contract.Requires(Month > 0, "You must specify a month");
            Contract.Requires(Day > 0, "You must specify a day");
            Contract.Requires(AppConfig != null, "You must specify a configuration file");
            Contract.Requires(OutputDirectory != null, "You must specify an output directory");
            Contract.Requires(File.Exists(AppConfig.AnalyzerConfig.Exe), "The analyzer executable file must exist");
            Contract.Requires(Directory.Exists(OutputDirectory), "The output directory must exist");
        }

        private static void WriteHelp()
        {
            var writer = new HelpWriter();
            writer.WriteBanner("cptools.buildownloader - Tool for downloading a set of builds and sampling artifacts (files) from them");
            writer.WriteLine("");
            writer.WriteOption("applicationConfig", "Required. File containing the application config parameters (json)", shortName:"ac");
            writer.WriteOption("year", "Required. Year from when the builds will be taken from", shortName: "y");
            writer.WriteOption("month", "Required. Month from when the builds will be taken from", shortName: "m");
            writer.WriteOption("day", "Required. Day from when the builds will be taken from", shortName: "d");
            writer.WriteOption("numBuilds", "Required. The number of builds (from different queues) that will be sampled", shortName: "nb");
            writer.WriteOption("outputDirectory", "Required. The directory where the outputs will be stored", shortName: "od");
        }
    }

}
