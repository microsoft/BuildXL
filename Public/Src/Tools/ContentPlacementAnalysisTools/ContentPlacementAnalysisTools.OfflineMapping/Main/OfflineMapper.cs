using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.ToolSupport;
using ContentPlacementAnalysisTools.Core.ML.Classifier;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlacementAnalysisTools.ML.Action;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using NLog;

namespace ContentPlacementAnalysisTools.OfflineMapping.Main
{
    /// <nodoc />
    public class OfflineMapper
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
            s_logger.Info($"OfflineMapper starting");
            try
            {
                s_logger.Info($"Using configuration [{arguments.AppConfig}]");
                ClassifyInstances(arguments);
            }
            finally
            {
                s_logger.Info("Done...");
            }
        }

        private static void ClassifyInstances(Args arguments)
        {
            // and a couple of checks here
            Contract.Requires(arguments.OutputDirectory != null, "You must specify an output directory");
            Contract.Requires(arguments.InputDirectory != null, "You must specify an input directory");
            Contract.Requires(Directory.Exists(arguments.OutputDirectory), "The output directory must exist");
            Contract.Requires(Directory.Exists(arguments.InputDirectory), "The input directory must exist");
            var sharedCount = 0;
            var nonSharedCount = 0;
            try
            {
                // load the classifier first
                s_logger.Info("Loading classifier...");
                // work on the content store
                s_logger.Info($"Initializing store at [{arguments.OutputDirectory}]");
                var store = new RocksDbContentPlacementPredictionStore(arguments.OutputDirectory, true);
                var opContext = new OperationContext(new Context(new LogWrapper(s_logger)));
                // init it
                var initialized = store.StartupAsync(opContext);
                initialized.Wait();
                // and check
                if (!initialized.Result)
                {
                    s_logger.Error($"Could not initialize RocksDbContentPlacementPredictionStore at [{arguments.OutputDirectory}]");
                }
                var classifier = new ContentPlacementClassifier(arguments.AppConfig.ClassifierConfiguration);
                // create the pipeline. The first step here is to parse the input files, and we can do this in parallel
                var buildArtifactParsingBlock = new TransformManyBlock<ParseBuildArtifactsInput, KeyValuePair<string, IReadOnlyList<ArtifactWithBuildMeta>>>(i =>
                {
                    var action = new ParseBuildArtifacts();
                    var result = action.PerformAction(i);
                    if (result.ExecutionStatus)
                    {
                        return result.Result.ArtifactsByHash.ToList();
                    }
                    else
                    {
                        s_logger.Error(result.Exception, $"Error when parsing [{i.BuildArtifactsFile}]");
                        throw result.Exception;
                    }
                },
                     new ExecutionDataflowBlockOptions()
                     {
                         MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxBuildParsingTasks
                     }
                );
                // then, when we have one, we linearize it
                var linearizeBlock = new TransformBlock<KeyValuePair<string, IReadOnlyList<ArtifactWithBuildMeta>>, TimedActionResult<LinearizeArtifactsOutput>>(i =>
                {
                    var action = new LinearizeArtifacts();
                    return action.PerformAction(new LinearizeArtifactsInput(i.Key, i.Value));

                },
                    new ExecutionDataflowBlockOptions()
                    {
                        MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxArtifactLinearizationTasks
                    }
                );

                // and we classify them
                var classifyBlock = new ActionBlock<TimedActionResult<LinearizeArtifactsOutput>>(i =>
                {
                    // i have an ml instance here
                    if (i.ExecutionStatus)
                    {
                        var cpInstance = new ContentPlacementInstance()
                        {
                            Artifact = i.Result.Linear.AsInstance(), // using the default utility method
                            QueueName = i.Result.Linear.Queues.First() // the first here is enough, since its always one!
                        };
                        var result = classifier.Classify(cpInstance);
                        if (result.Succeeded)
                        {
                            var selectedMachines = result.Value;
                            foreach (var path in i.Result.Linear.ReportedPaths)
                            {
                                store.StoreResult(opContext, path, selectedMachines);
                                Interlocked.Add(ref sharedCount, 1);
                            }
                        }
                        else
                        {
                            Interlocked.Add(ref nonSharedCount, 1);
                        }
                    }
                },
                    new ExecutionDataflowBlockOptions()
                    {
                        MaxDegreeOfParallelism = arguments.AppConfig.ConcurrencyConfig.MaxArtifactClassificationTasks
                    }
                );
                // link them
                var numParsingTasks = 0;
                buildArtifactParsingBlock.LinkTo(linearizeBlock, new DataflowLinkOptions { PropagateCompletion = true });
                linearizeBlock.LinkTo(classifyBlock, new DataflowLinkOptions { PropagateCompletion = true });
                // do now we can post to the initial queue
                foreach (var file in Directory.EnumerateFiles(arguments.InputDirectory, "*.json"))
                {
                    buildArtifactParsingBlock.Post(new ParseBuildArtifactsInput(file));
                    ++numParsingTasks;
                }
                s_logger.Info($"Posted {numParsingTasks} parsing tasks, processing");
                // now wait
                buildArtifactParsingBlock.Complete();
                classifyBlock.Completion.Wait();
                // and now we should snapshot
                var snapshotDir = Path.Combine(arguments.OutputDirectory, "Snap");
                Directory.CreateDirectory(snapshotDir);
                s_logger.Info($"Done, snapshoting to [{snapshotDir}]");
                var result = store.CreateSnapshot(opContext, snapshotDir);
                // done
            }
            finally
            {
                var total = 1.0 * (sharedCount + nonSharedCount); 
                var percentage = (1.0 * sharedCount) / total;
                s_logger.Info($"Stats: shared={sharedCount} ({percentage}), nonShared={nonSharedCount}, total={total}");
            }
        }

    }

    internal class LogWrapper : BuildXL.Cache.ContentStore.Interfaces.Logging.ILogger
    {
        internal NLog.Logger Logger { get; set; }

        public LogWrapper(NLog.Logger l)
        {
            Logger = l;
        }

        public Severity CurrentSeverity => Severity.Info;

        public int ErrorCount => 0;

        public void Always(string messageFormat, params object[] messageArgs)
        {
            Logger.Debug(string.Format(messageFormat, messageArgs));
        }

        public void Debug(string messageFormat, params object[] messageArgs)
        {
            Logger.Debug(string.Format(messageFormat, messageArgs));
        }

        public void Debug(Exception exception)
        {
            Logger.Error(exception);
        }

        public void Diagnostic(string messageFormat, params object[] messageArgs)
        {
            Logger.Debug(string.Format(messageFormat, messageArgs));
        }

        public void Dispose()
        {
            // noop
        }

        public void Error(string messageFormat, params object[] messageArgs)
        {
            Logger.Error(string.Format(messageFormat, messageArgs));
        }

        public void Error(Exception exception, string messageFormat, params object[] messageArgs)
        {
            Logger.Error(exception, string.Format(messageFormat, messageArgs));
        }

        public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs)
        {
            Logger.Error(exception, string.Format(messageFormat, messageArgs));
            throw exception;
        }

        public void Fatal(string messageFormat, params object[] messageArgs)
        {
            Logger.Fatal(string.Format(messageFormat, messageArgs));
        }

        public void Flush()
        {
            // noop
        }

        public void Info(string messageFormat, params object[] messageArgs)
        {
            Logger.Info(string.Format(messageFormat, messageArgs));
        }

        public void Log(Severity severity, string message)
        {
            Logger.Info(message);
        }

        public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs)
        {
            Logger.Info(string.Format(messageFormat, messageArgs));
        }

        public void Warning(string messageFormat, params object[] messageArgs)
        {
            Logger.Warn(string.Format(messageFormat, messageArgs));
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
        /// Classifier settings
        /// </summary>
        public ContentPlacementClassifierConfiguration ClassifierConfiguration { get; set; }
        /// <summary>
        /// Build from a json string
        /// </summary>
        public static ApplicationConfiguration FromJson(string json) => JsonConvert.DeserializeObject<ApplicationConfiguration>(File.ReadAllText(json));
        /// <inheritdoc />
        public override string ToString()
        {
            return new StringBuilder()
                .Append("ConcurrencyConfig=[").Append(ConcurrencyConfig).Append("], ")
                .Append("ClassifierConfiguration=[").Append(ClassifierConfiguration).Append("]")
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
        /// Maximum number of concurrent linearization tasks
        /// </summary>
        public int MaxArtifactLinearizationTasks { get; set; }
        /// <summary>
        /// Maximum number of concurrent store tasks
        /// </summary>
        public int MaxArtifactClassificationTasks { get; set; }
        /// <inheritdoc />
        public override string ToString()
        {
            return new StringBuilder()
                .Append("MaxBuildParsingTasks=").Append(MaxBuildParsingTasks).Append(", ")
                .Append("MaxArtifactClassificationTasks=").Append(MaxArtifactClassificationTasks).Append(", ")
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
               
            }
            Contract.Requires(AppConfig != null, "You must specify a configuration file");
        }

        private static void WriteHelp()
        {
            var writer = new HelpWriter();
            writer.WriteBanner("cptools.ml.offlineMapping - Tool for offline shared/nonshared prediction");
            writer.WriteLine("");
            writer.WriteOption("applicationConfig", "Required. File containing the application config parameters (json)", shortName: "ac");
            writer.WriteOption("inputDirectory", "Required. The directory where the inputs will be taken from", shortName: "id");
            writer.WriteOption("outputDirectory", "Required. The directory where the outputs will be stored", shortName: "od");
        }
    }
}
