// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.Tracing;
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
        public static int Main(string[] arguments) => new Program().MainHandler(arguments);

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
                                   bxl.ETWLogger.Log,
                                   BxlPipGraphFragmentGenerator.ETWLogger.Log,
                                   BuildXL.Engine.Cache.ETWLogger.Log,
                                   BuildXL.Engine.ETWLogger.Log,
                                   BuildXL.Scheduler.ETWLogger.Log,
                                   BuildXL.Tracing.ETWLogger.Log,
                                   BuildXL.Storage.ETWLogger.Log,
                                   BuildXL.FrontEnd.Core.ETWLogger.Log,
                                   BuildXL.FrontEnd.Script.ETWLogger.Log,
                                   BuildXL.FrontEnd.Nuget.ETWLogger.Log,
                                   BuildXL.FrontEnd.Download.ETWLogger.Log,
                               };

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
