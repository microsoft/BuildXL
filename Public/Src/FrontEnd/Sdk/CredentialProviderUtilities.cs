// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Tracked credential-provider discovery shared by resolvers.
    /// </summary>
    public static class CredentialProviderUtilities
    {
        private const string CredentialProviderEnv = "NUGET_CREDENTIALPROVIDERS_PATH";

        /// <summary>
        /// Retrieves the configured credential-provider paths as a tracked build parameter, or an empty string if unset.
        /// </summary>
        public static string GetCredentialProviderPaths(FrontEndEngineAbstraction engine, string frontEnd)
        {
            Contract.Requires(engine != null);
            Contract.Requires(!string.IsNullOrEmpty(frontEnd));

            return engine.TryGetBuildParameter(CredentialProviderEnv, frontEnd, out string paths) ? paths : string.Empty;
        }

        /// <summary>
        /// Resolves the first credential-provider executable from the configured platform-separated list of directories or files.
        /// </summary>
        /// <remarks>
        /// When <paramref name="allowMissing"/> is true, returns an invalid path without failure if no provider paths are configured.
        /// </remarks>
        public static Possible<AbsolutePath> TryResolveCredentialProvider(
            FrontEndContext context,
            FrontEndEngineAbstraction engine,
            string frontEnd,
            bool allowMissing = false)
        {
            Contract.Requires(context != null);
            Contract.Requires(engine != null);
            Contract.Requires(!string.IsNullOrEmpty(frontEnd));

            string credentialProviderPaths = GetCredentialProviderPaths(engine, frontEnd);
            if (string.IsNullOrEmpty(credentialProviderPaths))
            {
                return allowMissing
                    ? new Possible<AbsolutePath>(AbsolutePath.Invalid)
                    : new Failure<string>($"Environment variable {CredentialProviderEnv} is not set");
            }

            foreach (string path in credentialProviderPaths.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!AbsolutePath.TryCreate(context.PathTable, path, out var providerPath))
                {
                    return new Failure<string>($"Invalid {CredentialProviderEnv} entry '{path}': the path must be absolute.");
                }

                var directory = engine.FileExists(providerPath) ? providerPath.GetParent(context.PathTable) : providerPath;
                var executable = engine.EnumerateFiles(directory, "CredentialProvider*.exe").FirstOrDefault();
                if (!executable.IsValid)
                {
                    continue;
                }

                // Record the executable's contents as a graph input, not just its presence in the directory.
                if (!engine.TryGetFrontEndFile(executable, frontEnd, out var stream))
                {
                    return new Failure<string>($"Unable to read credential provider '{executable.ToString(context.PathTable)}'.");
                }

                stream.Dispose();
                return engine.Translate(executable);
            }

            return new Failure<string>($"Unable to authenticate using a credential provider: A credential provider named 'CredentialProvider*.exe' was not found under '{credentialProviderPaths}'.");
        }
    }
}
