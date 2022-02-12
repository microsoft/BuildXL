// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Factory;
using BuildXL.PipGraphFragmentGenerator.Tracing;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BxlPipGraphFragmentGenerator;

namespace BuildXL.PipGraphFragmentGenerator
{
    /// <summary>
    /// Main program.
    /// </summary>
    internal sealed class Program : ToolProgram<Args>
    {
        private readonly PathTable m_pathTable = new PathTable();
        private int m_handlingUnhandledFailureInProgress = 0;

        private Program()
            : base("BxlPipGraphFragmentGenerator")
        {
        }

        /// <nodoc />
        public static int Main(string[] arguments)
        {
            return new Program().MainHandler(arguments);
        }


        /// <inheritdoc />
        public override bool TryParse(string[] rawArgs, out Args arguments)
        {
            try
            {
                arguments = new Args(rawArgs, m_pathTable);
                return true;
            }
            catch (Exception e)
            {
                PrintErrorToConsole(e.ToStringDemystified());
                arguments = null;
                return false;
            }
        }

        /// <inheritdoc />
        public override int Run(Args arguments)
        {
            if (arguments.Help)
            {
                return (int)GeneratorExitCode.Success;
            }

            ContentHashingUtilities.SetDefaultHashType();

            using (SetupEventListener(EventLevel.Informational))
            {
                var loggingContext = new LoggingContext(nameof(PipGraphFragmentGenerator));

                AppDomain.CurrentDomain.UnhandledException +=
                   (sender, eventArgs) =>
                   {
                       HandleUnhandledFailure(loggingContext, eventArgs.ExceptionObject as Exception);
                   };
                ExceptionUtilities.UnexpectedException +=
                    (exception) =>
                    {
                        HandleUnhandledFailure(loggingContext, exception);
                    };
                TaskScheduler.UnobservedTaskException +=
                    (sender, eventArgs) =>
                    {
                        HandleUnhandledFailure(loggingContext, eventArgs.Exception);
                    };

                if (!PipGraphFragmentGenerator.TryGeneratePipGraphFragment(
                    loggingContext,
                    m_pathTable,
                    arguments.CommandLineConfig,
                    arguments.PipGraphFragmentGeneratorConfig))
                {
                    return (int)GeneratorExitCode.Failed;
                }

                return (int)GeneratorExitCode.Success;
            }
        }

        private void HandleUnhandledFailure(LoggingContext loggingContext, Exception exception)
        {
            if (Interlocked.CompareExchange(ref m_handlingUnhandledFailureInProgress, 1, comparand: 0) != 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(3));

                ExceptionUtilities.FailFast("Second-chance exception handler has not completed in the allowed time.", new InvalidOperationException());
                return;
            }

            try
            {
                GeneratorExitCode effectiveExitCode = GeneratorExitCode.InternalError;
                ExceptionRootCause rootCause = exception is NullReferenceException
                    ? ExceptionRootCause.FailFast
                    : ExceptionUtilities.AnalyzeExceptionRootCause(exception);
                
                switch (rootCause)
                {
                    case ExceptionRootCause.OutOfDiskSpace:
                    case ExceptionRootCause.DataErrorDriveFailure:
                    case ExceptionRootCause.DeviceAccessError:
                        effectiveExitCode = GeneratorExitCode.InfrastructureError;
                        break;
                    case ExceptionRootCause.MissingRuntimeDependency:
                        effectiveExitCode = GeneratorExitCode.MissingRuntimeDependency;
                        break;
                }

                string failureMessage = exception.ToStringDemystified();

                if (effectiveExitCode == GeneratorExitCode.InfrastructureError)
                {
                    Logger.Log.UnhandledInfrastructureError(loggingContext, failureMessage);
                }
                else
                {
                    Logger.Log.UnhandledFailure(loggingContext, failureMessage);
                }

                if (rootCause == ExceptionRootCause.FailFast)
                {
                    ExceptionUtilities.FailFast("Exception is configured to fail fast", exception);
                }

                Environment.Exit((int)effectiveExitCode);
            }
            catch (Exception e)
            {
                PrintErrorToConsole("Unhandled exception in exception handler");
                PrintErrorToConsole(e.ToStringDemystified());
            }
            finally
            {
                Environment.Exit((int)GeneratorExitCode.InternalError);
            }
        }

        private static void PrintErrorToConsole(string errorMessage)
        {
            ConsoleColor original = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(errorMessage);
            Console.ForegroundColor = original;
        }

        public static IDisposable SetupEventListener(EventLevel level)
        {
            var eventListener = new ConsoleEventListener(Events.Log, DateTime.UtcNow, true ,true, true, false, CancellationToken.None, level: level);

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
