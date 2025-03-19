// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace BuildToolsInstaller.Utilities
{
    /// <summary>
    /// Component that interacts with Azure DevOps, using the REST API or the environment 
    /// Abstracted as an interface for ease of testing
    /// </summary>
    public interface IAdoService
    {
        /// <summary>
        /// Whether ADO interaction is enabled
        /// This is false when not running on an agent, and other methods may throw in this case
        /// </summary>
        public bool IsEnabled { get; } 

        /// <nodoc />
        public string CollectionUri { get; }

        /// <nodoc />
        public string ToolsDirectory { get; }

        /// <nodoc />
        public string AccessToken { get; }

        /// <nodoc />
        public string BuildId { get; }

        /// <nodoc />
        public string RepositoryName { get; }

        /// <nodoc />
        public int PipelineId { get; }

        /// <nodoc />
        string PhaseName { get; }

        /// <nodoc />
        public int JobAttempt { get; }

        /// <summary>
        /// Used by communicating installers to coordinate with each other across job boundaries
        /// It assumes the 'worker' job has the same name than the 'main'/'orchestrator' job
        /// </summary>
        public string DistributedCoordinationKey => $"{PhaseName}_{JobAttempt}";

        /// <summary>
        /// Sets a build property with the specified value using the ADO REST API
        /// </summary>
        public Task SetBuildPropertyAsync(string key, string value);

        /// <summary>
        /// Gets the value of a build property using the REST API, or null if such property is not defined
        /// </summary>
        public Task<string?> GetBuildPropertyAsync(string key);

        /// <summary>
        /// Get the organization name from environment data in the agent
        /// </summary>
        public bool TryGetOrganizationName([NotNullWhen(true)] out string? organizationName);

        /// <summary>
        /// Set a variable that will be visible by subsequent tasks in the running job
        /// </summary>
        public void SetVariable(string variableName, string value, bool isReadOnly = true);
    }
}
