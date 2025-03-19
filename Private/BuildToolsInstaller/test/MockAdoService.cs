// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BuildToolsInstaller.Utilities;

namespace BuildToolsInstaller.Tests
{
    internal class MockAdoService : IAdoService
    {
        public static readonly string OrgName = "testOrg";
        public bool IsEnabled => true;

        public string CollectionUri => $"https://dev.azure.com/{OrgName}/";

        public required string ToolsDirectory { get; init; }

        public string AccessToken { get; init; } = "<ACCESSTOKEN>";

        public string BuildId { get; init; } = "212121";

        public string RepositoryName { get; init; } = "TestRepo";

        public int PipelineId { get; init; } = 13012;
        public string PhaseName { get; init; } = "MyJob";
        public int JobAttempt { get; init; } = 1;

        public ConcurrentDictionary<string, string> Properties = new();
        public Task<string?> GetBuildPropertyAsync(string key)
        {
            return Task.FromResult(Properties.TryGetValue(key, out var value) ? value : null);
        }

        public Task SetBuildPropertyAsync(string key, string value)
        {
            Properties[key] = value;
            return Task.CompletedTask;
        }

        public ConcurrentDictionary<string, string> Variables = new();
        public void SetVariable(string variableName, string value, bool isReadOnly = true)
        {
            Variables[variableName] = value;
        }

        public bool TryGetOrganizationName([NotNullWhen(true)] out string? organizationName)
        {
            organizationName = OrgName;
            return true;
        }
    }

    internal class DisabledMockAdoService : IAdoService
    {
        public bool IsEnabled => false;

        private Exception Error => new InvalidOperationException("AdoService is disabled");

        public string CollectionUri => throw Error;

        public string ToolsDirectory => throw Error;

        public string AccessToken => throw Error;

        public string BuildId => throw Error;

        public string RepositoryName => throw Error;

        public int PipelineId => throw Error;

        public string PhaseName => throw Error;

        public int JobAttempt => throw Error;

        public Task<string?> GetBuildPropertyAsync(string key) => throw Error;

        public Task SetBuildPropertyAsync(string key, string value) => throw Error;

        public void SetVariable(string variableName, string value, bool isReadOnly = true) => throw Error;

        public bool TryGetOrganizationName([NotNullWhen(true)] out string? organizationName) => throw Error;
    }
}
