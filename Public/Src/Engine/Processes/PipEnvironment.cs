// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Interop.Unix;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.BuildParameters;

namespace BuildXL.Processes
{
    /// <summary>
    /// Helper class that defines the environment variables used when executing pips
    /// </summary>
    public sealed class PipEnvironment
    {
        /// <summary>
        /// Path to temp directory where no access is allowed
        /// </summary>
        public static readonly string RestrictedTemp =
            Path.Combine(
                SpecialFolderUtilities.GetFolderPath(OperatingSystemHelper.IsUnixOS ? Environment.SpecialFolder.UserProfile : Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "BuildXL",
                "RestrictedTemp");

        /// <summary>
        /// Environment variables in the current machine
        /// </summary>
        public IBuildParameters FullEnvironmentVariables { get; }

        /// <summary>
        /// Environment variables in the orchestrator machine
        /// </summary>
        /// <remarks>
        /// Null if the build is not distributed and the current node is orchestrator.
        /// </remarks>
        public IReadOnlyDictionary<string, string> OrchestratorEnvironmentVariables { get; set; }

        private readonly IBuildParameters m_baseEnvironmentVariables;

        private LoggingContext m_loggingContext;

        /// <summary>
        /// Constructor
        /// </summary>
        public PipEnvironment(LoggingContext loggingContext)
        {
            m_loggingContext = loggingContext;

            FullEnvironmentVariables = 
                GetFactory(ReportDuplicateVariable)
                .PopulateFromEnvironment();

#if PLATFORM_WIN
            var comspec = Path.Combine(SpecialFolderUtilities.SystemDirectory, "cmd.exe");
            var pathExt = ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC";
#endif

            var path =
                string.Join(
                    OperatingSystemHelper.IsUnixOS ? ":" : ";",
                    SpecialFolderUtilities.SystemDirectory,
#if PLATFORM_WIN
                    SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.Windows),
                    Path.Combine(SpecialFolderUtilities.SystemDirectory, "wbem")
#else
                    UnixPaths.UsrBin,
                    UnixPaths.UsrSbin,
                    UnixPaths.Bin,
                    UnixPaths.Sbin
#endif
                );

            // the environment variable names below should use the casing appropriate for the target OS
            // (on Windows it won't matter, but on Unix-like systems, including Cygwin environment on Windows,
            // it matters, and has to be all upper-cased). See also doc comment for IBuildParameters.Select
            m_baseEnvironmentVariables = FullEnvironmentVariables
                .Select(new[]
                {
                    "NUMBER_OF_PROCESSORS",
                    "OS",
                    "PROCESSOR_ARCHITECTURE",
                    "PROCESSOR_IDENTIFIER",
                    "PROCESSOR_LEVEL",
                    "PROCESSOR_REVISION",
                    "SystemDrive",
                    "SystemRoot",
                    "SYSTEMTYPE",
                })
                .Override(new Dictionary<string, string>()
                {
                    { "PATH", path },
#if PLATFORM_WIN
                    { "ComSpec", comspec },
                    { "PATHEXT", pathExt }
#endif
                })
                .Override(DisallowedTempVariables
                    .Select(tmp => new KeyValuePair<string, string>(tmp, RestrictedTemp)));
        }

        /// <summary>
        /// Gets the effective environment variables, taking into account default and machine-specific values
        /// </summary>
        public IBuildParameters GetEffectiveEnvironmentVariables(Pips.Operations.Process pip, PipFragmentRenderer pipFragmentRenderer, int currentUserRetryCount, IReadOnlyList<string> globalUnsafePassthroughEnvironmentVariables = null)
        {
            Contract.Requires(pipFragmentRenderer != null);
            Contract.Requires(pip != null);
            Contract.Requires(currentUserRetryCount >= 0);

            // Capture the env variables whose values are set (as opposed to being taken from the process environment)
            // Observe these variables can be passthrough or not
            var setEnvironmentVars = pip.EnvironmentVariables.Where(envVar => envVar.Value.IsValid);
            // Now take the env var names that are not set, and therefore are taken from the process environment. Observe that by contract there are always passthrough
            var unsetEnvNames = pip.EnvironmentVariables.Where(envVar => !envVar.Value.IsValid).Select(envVar => pipFragmentRenderer.Render(envVar.Name));

            // Append any passthrough environment variables if they're specified
            unsetEnvNames = globalUnsafePassthroughEnvironmentVariables != null ? unsetEnvNames.Union(globalUnsafePassthroughEnvironmentVariables) : unsetEnvNames;

            IBuildParameters fullEnvironmentForPassThrough = OrchestratorEnvironmentVariables != null ?

                // We first look at the env variables from the worker,
                // then if the pass through variable is unset, we look at the env variables from the orchestrator.
                // That's why, if OrchestratorEnvironmentVariables is not null, it is overridden by the current environment variables.
                GetFactory(ReportDuplicateVariable).PopulateFromDictionary(OrchestratorEnvironmentVariables).Override(FullEnvironmentVariables.ToDictionary()) :
                FullEnvironmentVariables;

            IBuildParameters effectiveVariables = m_baseEnvironmentVariables
                .Override(setEnvironmentVars.ToDictionary(
                    envVar => pipFragmentRenderer.Render(envVar.Name),
                    envVar => envVar.Value.ToString(pipFragmentRenderer)))
                .Override(fullEnvironmentForPassThrough.Select(unsetEnvNames).ToDictionary());

            // If the variable to indicate the retry attempt is set, make sure it gets populated
            if (pip.RetryAttemptEnvironmentVariable.IsValid)
            {
                effectiveVariables = effectiveVariables.Override(
                    new[] { new KeyValuePair<string, string>(pipFragmentRenderer.Render(pip.RetryAttemptEnvironmentVariable), currentUserRetryCount.ToString()) });
            }

            return effectiveVariables;
        }

        /// <summary>
        /// Gets the effective environment variables, taking into account default and machine-specific values
        /// </summary>
        public IBuildParameters GetEffectiveEnvironmentVariables(PathTable pathTable, Pips.Operations.Process pip)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(pip != null);

            return GetEffectiveEnvironmentVariables(pip, new PipFragmentRenderer(pathTable), currentUserRetryCount: 0);
        }


        private void ReportDuplicateVariable(string key, string existingValue, string ignoredValue)
        {
            ReportDuplicateVariable(m_loggingContext, key, existingValue, ignoredValue);
        }

        /// <summary>
        /// Logs a message saying that a duplicate environment variable was encountered.
        /// </summary>
        public static void ReportDuplicateVariable(LoggingContext loggingContext, string key, string existingValue, string ignoredValue)
        {
            Tracing.Logger.Log.DuplicateWindowsEnvironmentVariableEncountered(
                loggingContext,
                key,
                existingValue,
                ignoredValue);
        }
    }
}
