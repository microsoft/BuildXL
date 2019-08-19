using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks.Dataflow;
using BuildXL.ToolSupport;
using ContentPlacementAnalysisTools.Core;
using ContentPlacementAnalysisTools.Extraction.Action;
using ContentPlamentAnalysisTools.Core;
using Kusto.Data;
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
            s_logger.Info($"BuildDownloader starting");
            try
            {
                // parse args
                var arguments = new Args(args);
                s_logger.Info($"Using configuration [{arguments.AppConfig}]");
                // so in here we will create a new network.
                var buildInfoBlock = new TransformManyBlock<GetKustoBuildInput, KustoBuild>(i =>
                {
                    var action = new GetKustoBuild(arguments.AppConfig);
                    var result = action.PerformActionWithResult(i);
                    // its not worth it to continue if this fails
                    Contract.Requires(result.ExecutionStatus, "Could not download builds, check logs for exceptions");
                    return result.Result.KustoBuildData;
                });
                var downloadBlock = new TransformBlock<KustoBuild, TimedActionResult<BuildDownloadOutput>>( i => 
                {
                    var action = new BuildDownload(arguments.AppConfig, arguments.OutputDirectory);
                    return action.PerformActionWithResult(i);
                }, 
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxDownloadTasks
                });
                var decompressBlock = new TransformBlock<TimedActionResult<BuildDownloadOutput>, TimedActionResult<DecompressionOutput>>( i =>
                {
                    // check
                    if (i.ExecutionStatus)
                    {
                        // decompress if build was successfull
                        var action = new Decompression(arguments.AppConfig);
                        return action.PerformActionWithResult(i.Result);
                    }
                    return new TimedActionResult<DecompressionOutput>(i.Exception);
                },
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxDecompressionTasks
                });
                var analysisBlock = new ActionBlock<TimedActionResult<DecompressionOutput>>(i =>
                {
                    // check
                    if (i.ExecutionStatus)
                    {
                        // analyze if decompression succeded
                        var action = new BuildAnalisys(arguments.AppConfig);
                        action.PerformActionWithResult(i.Result);
                    }
                },
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxAnalysisTasks
                });
                // link them
                buildInfoBlock.LinkTo(downloadBlock, new DataflowLinkOptions { PropagateCompletion = true });
                downloadBlock.LinkTo(decompressBlock, new DataflowLinkOptions { PropagateCompletion = true });
                decompressBlock.LinkTo(analysisBlock, new DataflowLinkOptions { PropagateCompletion = true });
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
                .Append("KustoConnectionConfiguration=[").Append(KustoConfig).Append("]")
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
        /// Maximum number of concurrent decompression tasks
        /// </summary>
        public int MaxDecompressionTasks { get; set; }
        /// <summary>
        /// Maximum number of concurrent analysis tasks
        /// </summary>
        public int MaxAnalysisTasks { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return new StringBuilder()
                .Append("MaxDownloadTasks=").Append(MaxDownloadTasks).Append(", ")
                .Append("MaxDecompressionTasks=").Append(MaxDecompressionTasks).Append(", ")
                .Append("MaxAnalysisTasks=").Append(MaxAnalysisTasks).Append(", ")
                .ToString();
        }
    }

    internal sealed partial class Args : CommandLineUtilities
    {
        /// <summary>
        /// This application config, parsed from the ac argument
        /// </summary>
        public readonly ApplicationConfiguration AppConfig;
        /// <summary>
        /// The maximum number of builds to be downloaded
        /// </summary>
        public int NumBuilds { get;}
        /// <summary>
        /// Year, to build the date for downloading build info
        /// </summary>
        public int Year { get;}
        /// <summary>
        /// Month, to build the date for downloading build info
        /// </summary>
        public int Month { get;}
        /// <summary>
        /// Day, to build the date for downloading build info
        /// </summary>
        public int Day { get;}
        /// <summary>
        /// The output directory for downloading builds and saving results
        /// </summary>
        public string OutputDirectory { get; }

        public Args(string[] args) : base(args)
        {
            foreach (var opt in Options)
            {
                if (opt.Name.Equals("applicationConfig", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("ac", StringComparison.OrdinalIgnoreCase))
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
            Contract.Requires(File.Exists(AppConfig.AnalyzerConfig.Exe), "The analyzer executable file must exist");
            Contract.Requires(Directory.Exists(OutputDirectory), "The output directory must exist");
        }
    }

}
