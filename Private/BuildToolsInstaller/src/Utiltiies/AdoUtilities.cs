// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

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
