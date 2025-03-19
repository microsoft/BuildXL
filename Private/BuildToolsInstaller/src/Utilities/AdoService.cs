// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace BuildToolsInstaller.Utilities
{
    internal sealed partial class AdoService : IAdoService
    {
        // Make this class a singleton
        private AdoService() { }

        public static AdoService Instance { get; } = s_instance ??= new();
        // Keep as lazily initialized for the sake of testing outside of ADO (where we need to modify the environment first)
        private static AdoService? s_instance;

        /// <summary>
        /// True if the process is running in an ADO build. 
        /// The other methods and properties in this class are meaningful if this is true.
        /// </summary>
        public bool IsEnabled => m_isAdoBuild;
        private readonly bool m_isAdoBuild = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"));

        [GeneratedRegex(@"^(?:https://(?<oldSchoolAccountName>[a-zA-Z0-9-]+)\.(?:vsrm\.)?visualstudio\.com/|https://(?:vsrm\.)?dev\.azure\.com/(?<newSchoolAccountName>[a-zA-Z0-9-]+)/)$", RegexOptions.CultureInvariant)]
        private static partial Regex CollectionUriRegex();

        private void EnsureAdo()
        {
            if (!IsEnabled)
            {
                throw new InvalidOperationException($"This operation in {nameof(AdoService)} is only available in an ADO build");
            }
        }

        private T EnsuringAdo<T>(T? ret)
        {
            EnsureAdo();
            return ret!;
        }

        #region Predefined variables - see https://learn.microsoft.com/en-us/azure/devops/pipelines/build/variables
        /// <nodoc />
        public string CollectionUri => EnsuringAdo(Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI"));

        /// <nodoc />
        public string ToolsDirectory => EnsuringAdo(Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY"));

        /// <nodoc />
        public string AccessToken => EnsuringAdo(Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN"));

        /// <nodoc />
        private string ServerUri => EnsuringAdo(Environment.GetEnvironmentVariable("SYSTEM_TEAMFOUNDATIONSERVERURI"));

        /// <nodoc />
        private string ProjectId => EnsuringAdo(Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECTID"));

        /// <nodoc />
        public string BuildId => EnsuringAdo(Environment.GetEnvironmentVariable("BUILD_BUILDID"));

        /// <nodoc />
        public string RepositoryName => Environment.GetEnvironmentVariable("BUILD_REPOSITORY_NAME")!;

        /// <nodoc />
        public int PipelineId => int.Parse(Environment.GetEnvironmentVariable("SYSTEM_DEFINITIONID")!);

        /// <nodoc />
        public string PhaseName => Environment.GetEnvironmentVariable("SYSTEM_PHASENAME")!;

        /// <nodoc />
        public int JobAttempt => int.Parse(Environment.GetEnvironmentVariable("SYSTEM_JOBATTEMPT")!);
        #endregion

        private BuildHttpClient BuildClient => m_httpClient ??= new BuildHttpClient(new Uri(ServerUri), new VssBasicCredential(string.Empty, AccessToken));
        private BuildHttpClient? m_httpClient;

        /// <inheritdoc /> 
        public async Task<string?> GetBuildPropertyAsync(string key)
        {
            EnsureAdo();
            var props = await IdempotentWithRetry(() => BuildClient.GetBuildPropertiesAsync(ProjectId, int.Parse(BuildId)));
            return props.ContainsKey(key) ? props.GetValue(key, string.Empty) : null;
        }

        /// <inheritdoc />
        public async Task SetBuildPropertyAsync(string key, string value)
        {
            EnsureAdo();
            // UpdateBuildProperties is ultimately an HTTP PATCH: the new properties specified will be added to the existing ones
            // in an atomic fashion. So we don't have to worry about multiple builds concurrently calling UpdateBuildPropertiesAsync
            // as long as the keys don't clash.
            PropertiesCollection patch = new()
            {
                { key, value }
            };

            await IdempotentWithRetry(() => BuildClient.UpdateBuildPropertiesAsync(patch, ProjectId, int.Parse(BuildId)));
        }

        /// <summary>
        /// A naive retry to be a liitle bit robust around flaky network issues and such
        /// </summary>
        private static async Task<T> IdempotentWithRetry<T>(Func<Task<T>> taskFactory)
        {
            try
            {
                return await taskFactory();
            }
            catch (Exception)
            {
                // Back off for a sec
                await Task.Delay(1000);
                return await taskFactory();
            }
        }

        /// <summary>
        /// Get the organization name from environment data in the agent
        /// </summary>
        public bool TryGetOrganizationName([NotNullWhen(true)] out string? organizationName)
        {
            EnsureAdo();
            organizationName = null;
            string collectionUri = CollectionUri;
            if (collectionUri == null)
            {
                return false;
            }

            Match match = CollectionUriRegex().Match(collectionUri);
            if (match.Success)
            {
                organizationName = match.Groups["oldSchoolAccountName"].Success
                    ? match.Groups["oldSchoolAccountName"].Value
                    : match.Groups["newSchoolAccountName"].Value;
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <inheritdoc />
        public void SetVariable(string variableName, string value, bool isReadOnly = true)
        {
            EnsureAdo();
            if (!IsEnabled)
            {
                return;
            }

            Console.WriteLine($"Setting $({variableName})={value}");
            Console.WriteLine($"##vso[task.setvariable variable={variableName};]{value}");
        }
    }
}
