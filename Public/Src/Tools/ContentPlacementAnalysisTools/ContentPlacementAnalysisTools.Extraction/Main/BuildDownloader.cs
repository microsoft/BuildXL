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
                if (!arguments.QueueDataOnly)
                {
                    DownloadBuilds(arguments);
                }
                else
                {
                    DownloadMonthlyQueueData(arguments);
                }
                
            }
            finally
            {
                s_logger.Info("Done...");
            }
        }

        private static void DownloadMonthlyQueueData(Args arguments)
        {
            s_logger.Info($"Downloading Queue data ({(arguments.IncludeMachineMap? "including machine map" : "no machine map will be included")})");
            // couple of checks here
            Contract.Requires(arguments.Year > 0, "You must specify a year");
            Contract.Requires(arguments.Month > 0, "You must specify a month");
            Contract.Requires(arguments.OutputDirectory != null, "You must specify an output directory");
            Contract.Requires(Directory.Exists(arguments.OutputDirectory), "The output directory must exist");
            if (arguments.IncludeMachineMap)
            {
                Contract.Requires(arguments.AppConfig.ConcurrencyConfig.MaxMachineMapTasks > 0, "You must specify a positive number of MaxMachineMapTasks");
            }
            // and now download the data to a file
            var downloadQueueDataBlock = new TransformManyBlock<DownloadMonthlyQueueDataInput, KustoQueueData>(
                i =>
                {
                    var action = new DownloadMonthlyQueueData(arguments.AppConfig);
                    var result = action.PerformAction(i);
                    // its not worth it to continue if this fails
                    if (!result.ExecutionStatus)
                    {
                        s_logger.Error(result.Exception, "Could not download queue data");
                        // just throw the exception here
                        throw result.Exception;
                    }
                    return result.Result.Queues;
                }
            );
            // this is the one that builds machine maps
            var createMachineMapBlock = new ActionBlock<KustoQueueData>(
                i =>
                {
                    var action = new BuildQueueToMachineMap(arguments.AppConfig);
                    var result = action.PerformAction(new BuildQueueToMachineMapInput(arguments.Year, arguments.Month, arguments.OutputDirectory, i.QueueName));
                    // its not worth it to continue if this fails
                    if (!result.ExecutionStatus)
                    {
                        s_logger.Error(result.Exception, $"Could not create machine map for queue {i.QueueName}");
                    }
                },
                 new ExecutionDataflowBlockOptions()
                 {
                     MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxMachineMapTasks
                 }
            );
            // link
            downloadQueueDataBlock.LinkTo(createMachineMapBlock, new DataflowLinkOptions { PropagateCompletion = true });
            // create the input
            var input = new DownloadMonthlyQueueDataInput(arguments.Year, arguments.Month, arguments.OutputDirectory);
            // post
            downloadQueueDataBlock.Post(input);
            // complete
            downloadQueueDataBlock.Complete();
            // and wait
            if (!arguments.IncludeMachineMap)
            {
                downloadQueueDataBlock.Completion.Wait();
            }
            else
            {
                createMachineMapBlock.Completion.Wait();
            }
            // done
        }

        private static void DownloadBuilds(Args arguments)
        {
            s_logger.Info("Downloading Builds...");
            // a couple of checks here
            Contract.Requires(arguments.NumBuilds > 0, "You must specify a number of builds");
            Contract.Requires(arguments.Year > 0, "You must specify a year");
            Contract.Requires(arguments.Month > 0, "You must specify a month");
            Contract.Requires(arguments.Day > 0, "You must specify a day");
            Contract.Requires(arguments.AppConfig != null, "You must specify a configuration file");
            Contract.Requires(arguments.OutputDirectory != null, "You must specify an output directory");
            Contract.Requires(File.Exists(arguments.AppConfig.AnalyzerConfig.Exe), "The analyzer executable file must exist");
            Contract.Requires(Directory.Exists(arguments.OutputDirectory), "The output directory must exist");
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
            var downloadBlock = new TransformBlock<List<KustoBuild>, TimedActionResult<DecompressionOutput>>(i =>
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
            // in here, we need to set up the days for which the downloads will be taken
            var date = new DateTime(arguments.Year, arguments.Month, arguments.Day);
            for(var i = 0; i < arguments.Span; ++i)
            {
                s_logger.Info($"Downloading builds from year={date.Year}, month={date.Month}, day={date.Day}");
                // create the inout for this task
                var input = new GetKustoBuildInput(arguments.NumBuilds, date.Year, date.Month, date.Day);
                // post the task...
                buildInfoBlock.Post(input);
                // and add one more day
                date = date.AddDays(1);
            }
            // and complete
            buildInfoBlock.Complete();
            // wait for the last...
            analysisBlock.Completion.Wait();
        }
    }

    /// <summary>
    /// Represents the configuration file of this app. A configuration file has the form
    /// {
    ///     "UseCBTest": // true or false
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
        /// True if we are using cbtest
        /// </summary>
        public bool UseCBTest { get; set; } = false;
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
                .Append("UseCBTest=").Append(UseCBTest).Append(", ")
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
        /// <summary>
        /// Maximum number of concurrent machine map creation tasks
        /// </summary>
        public int MaxMachineMapTasks { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return new StringBuilder()
                .Append("MaxDownloadTasks=").Append(MaxDownloadTasks).Append(", ")
                .Append("MaxMachineMapTasks=").Append(MaxMachineMapTasks).Append(", ")
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
        /// How many days since the start day will be downloaded. Defaults to 7, for a week
        /// </summary>
        public int Span { get; } = 7;
        /// <summary>
        /// The output directory for downloading builds and saving results
        /// </summary>
        public string OutputDirectory { get; } = null;
        /// <summary>
        /// True if we want to download queue data
        /// </summary>
        public bool QueueDataOnly { get; } = false;
        /// <summary>
        /// True if we want to build a machine map
        /// </summary>
        public bool IncludeMachineMap { get; } = false;
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
                else if (opt.Name.Equals("span", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("sp", StringComparison.OrdinalIgnoreCase))
                {
                    Span = ParseInt32Option(opt, 1, 31);
                }
                else if (opt.Name.Equals("numBuilds", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("nb", StringComparison.OrdinalIgnoreCase))
                {
                    NumBuilds = ParseInt32Option(opt, 1, int.MaxValue);
                }
                else if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("od", StringComparison.OrdinalIgnoreCase))
                {
                    OutputDirectory = ParseStringOption(opt);
                }
                else if (opt.Name.Equals("queueDataOnly", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("qdo", StringComparison.OrdinalIgnoreCase))
                {
                    QueueDataOnly = ParseBooleanOption(opt);
                }
                else if (opt.Name.Equals("includeMachineMap", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("imm", StringComparison.OrdinalIgnoreCase))
                {
                    IncludeMachineMap = ParseBooleanOption(opt);
                }

            }            
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
            writer.WriteOption("span", "Optional. Day span for build download. Starting from <day, d>, <span, sp> days will be downloaded (<numBuilds, nb> builds per day)", shortName: "sp");
            writer.WriteOption("queueDataOnly", "Optional. If set, the queue data will be downloaded (no builds will be downloaded)", shortName: "qdo");
            writer.WriteOption("includeMachineMap", "Optional. Used in conjunction with queueDataOnly. If set, the queue/machine map will be created for that specific list of queues", shortName: "imm");
        }
    }

}
