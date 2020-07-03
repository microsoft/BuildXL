// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.Tracing;
using System.Linq;
using BuildXL.FrontEnd.Factory;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.PipGraphFragmentGenerator
{
    /// <summary>
    /// Main program.
    /// </summary>
    internal sealed class Program : ToolProgram<Args>
    {
        private readonly PathTable m_pathTable = new PathTable();

        private Program()
            : base("BxlPipGraphFragmentGenerator")
        {
        }

        /// <nodoc />
        public static int Main(string[] arguments)
        {
            try
            {
                return new Program().MainHandler(arguments);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Unexpected exception: {e}");
                return -1;
            }
        }


        /// <inheritdoc />
        public override bool TryParse(string[] rawArgs, out Args arguments)
        {
            try
            {
                arguments = new Args(rawArgs, m_pathTable);
                return true;
            }
            catch (Exception ex)
            {
                ConsoleColor original = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.GetLogEventMessage());
                Console.ForegroundColor = original;
                arguments = null;
                return false;
            }
        }

        /// <inheritdoc />
        public override int Run(Args arguments)
        {
            if (arguments.Help)
            {
                return 0;
            }

            ContentHashingUtilities.SetDefaultHashType();

            using (SetupEventListener(EventLevel.Informational))
            {
                if (!PipGraphFragmentGenerator.TryGeneratePipGraphFragment(
                    m_pathTable,
                    arguments.CommandLineConfig,
                    arguments.PipGraphFragmentGeneratorConfig))
                {
                    return 1;
                }

                return 0;
            }
        }

        public static IDisposable SetupEventListener(EventLevel level)
        {
            var eventListener = new ConsoleEventListener(Events.Log, DateTime.UtcNow, true, true, true, false, level: level);

            var primarySource = BxlPipGraphFragmentGenerator.ETWLogger.Log;
            if (primarySource.ConstructionException != null)
            {
                throw primarySource.ConstructionException;
            }

            eventListener.RegisterEventSource(primarySource);

            eventListener.EnableTaskDiagnostics(BuildXL.Tracing.ETWLogger.Tasks.CommonInfrastructure);

            var eventSources = new EventSource[]
                               {
                                   BxlPipGraphFragmentGenerator.ETWLogger.Log,
                                   BuildXL.Engine.Cache.ETWLogger.Log,
                                   BuildXL.Engine.ETWLogger.Log,
                                   BuildXL.Scheduler.ETWLogger.Log,
                                   BuildXL.Pips.ETWLogger.Log,
                                   BuildXL.Tracing.ETWLogger.Log,
                                   BuildXL.Storage.ETWLogger.Log,
                               }.Concat(FrontEndControllerFactory.GeneratedEventSources);

            using (var dummy = new TrackingEventListener(Events.Log))
            {
                foreach (var eventSource in eventSources)
                {
                    Events.Log.RegisterMergedEventSource(eventSource);
                }
            }

            return eventListener;
        }
    }
}
