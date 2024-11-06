// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using NuGet.Protocol.Plugins;

namespace BuildToolsInstaller.Utiltiies
{
    internal sealed partial class AdoUtilities
    {
        /// <summary>
        /// True if the process is running in an ADO build. 
        /// The other methods and properties in this class are meaningful if this is true.
        /// </summary>
        public static bool IsAdoBuild => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"));

        [GeneratedRegex(@"^(?:https://(?<oldSchoolAccountName>[a-zA-Z0-9-]+)\.(?:vsrm\.)?visualstudio\.com/|https://(?:vsrm\.)?dev\.azure\.com/(?<newSchoolAccountName>[a-zA-Z0-9-]+)/)$", RegexOptions.CultureInvariant)]
        private static partial Regex CollectionUriRegex();

        /// <nodoc />
        public static string CollectionUri => Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI")!;
        /// <nodoc />
        public static string ToolsDirectory => Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY")!;

        /// <nodoc />
        public static string AccessToken => Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN")!;

        /// <nodoc />
        private static string ServerUri => Environment.GetEnvironmentVariable("SYSTEM_TEAMFOUNDATIONSERVERURI")!;

        /// <nodoc />
        private static string ProjectId => Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECTID")!;

        /// <nodoc />
        public static string BuildId => Environment.GetEnvironmentVariable("BUILD_BUILDID")!;

        private static BuildHttpClient BuildClient => s_httpClient ??= new BuildHttpClient(new Uri(ServerUri), new VssBasicCredential(string.Empty, AccessToken));
        private static BuildHttpClient? s_httpClient;

        public static async Task<string?> GetBuildPropertyAsync(string key)
        {
            var props = await IdempotentWithRetry(() => BuildClient.GetBuildPropertiesAsync(ProjectId, int.Parse(BuildId)));
            return props.ContainsKey(key) ? props.GetValue(key, string.Empty) : null;
        }

        public static async Task SetBuildPropertyAsync(string key, string value)
        {

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
        public static bool TryGetOrganizationName([NotNullWhen(true)] out string? organizationName)
        {
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

        /// <summary>
        /// Set a variable that will be visible by subsequent tasks in the running job
        /// </summary>
        public static void SetVariable(string variableName, string value, bool isReadOnly = true)
        {
            if (!IsAdoBuild)
            {
                return;
            }

            Console.WriteLine($"Setting $({variableName})={value}");
            Console.WriteLine($"##vso[task.setvariable variable={variableName};]{value}");
        }
    }
}
