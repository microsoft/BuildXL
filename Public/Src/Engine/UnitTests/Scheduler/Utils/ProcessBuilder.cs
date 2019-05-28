// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace Test.BuildXL.Scheduler.Utils
{
    /// <summary>
    /// Helper class for creating <see cref="Process"/> objects in tests using Builder pattern.
    /// </summary>
    internal sealed class ProcessBuilder
    {
        private FileArtifact m_executable;
        private AbsolutePath m_workingDirectory;
        private PipData? m_arguments;
        private AbsolutePath m_standardDirectory;
        private IEnumerable<FileArtifact> m_dependencies;
        private IEnumerable<FileArtifact> m_regularOutputs;
        private IEnumerable<FileArtifactWithAttributes> m_attributedOutputs;
        private Process.Options m_options = Process.Options.None;

        private BuildXLContext m_context;
        private EnvironmentVariable[] m_environmentVariables;
        private IEnumerable<DirectoryArtifact> m_directoryDependencies;
        private IEnumerable<PipId> m_orderDependencies;
        private ServiceInfo m_serviceInfo;
        private IEnumerable<AbsolutePath> m_untrackedPathes;
        private IEnumerable<AbsolutePath> m_untrackedScopes;
        private IEnumerable<StringId> m_tags;
        private PipProvenance m_pipProvenance;
        private IEnumerable<FileArtifact> m_directoryDependenciesToConsume;
        private Func<ProcessBuilder, PipData> m_argumentsFactory;
        private IEnumerable<AbsolutePath> m_preserveOutputWhitelist;

        public IEnumerable<FileArtifactWithAttributes> Outputs
        {
            get
            {
                Contract.Ensures(Contract.Result<IEnumerable<FileArtifactWithAttributes>>() != null);

                return m_attributedOutputs ?? (m_regularOutputs ?? Enumerable.Empty<FileArtifact>()).Select(a => a.WithAttributes());
            }
        }

        public IEnumerable<FileArtifact> Dependencies => m_dependencies;

        public IEnumerable<DirectoryArtifact> DirectoryDependencies => m_directoryDependencies;

        public IEnumerable<FileArtifact> DirectoryDependenciesToConsume => m_directoryDependenciesToConsume;

        public ProcessBuilder WithExecutable(FileArtifact executable)
        {
            m_executable = executable;
            return this;
        }

        public ProcessBuilder WithWorkingDirectory(AbsolutePath workingDirectory)
        {
            m_workingDirectory = workingDirectory;
            return this;
        }

        public ProcessBuilder WithArguments(PipData arguments)
        {
            Contract.Assert(m_argumentsFactory == null, "Only WithArguments or WithArgumentsFactory method could be called, but not both.");

            m_arguments = arguments;
            return this;
        }

        public ProcessBuilder WithArgumentsFactory(Func<ProcessBuilder, PipData> argumentsFactory)
        {
            Contract.Requires(argumentsFactory != null);
            Contract.Assert(m_arguments == null, "Only WithArguments or WithArgumentsFactory method could be called, but not both.");
            
            m_argumentsFactory = argumentsFactory;
            return this;
        }

        public ProcessBuilder WithEnvironmentVariables(params EnvironmentVariable[] environmentVariables)
        {
            m_environmentVariables = environmentVariables;
            return this;
        }

        public ProcessBuilder WithStandardDirectory(AbsolutePath standardDirectory)
        {
            m_standardDirectory = standardDirectory;
            return this;
        }

        public ProcessBuilder WithDependencies(params FileArtifact[] dependencies)
        {
            m_dependencies = dependencies;
            return this;
        }
        
        public ProcessBuilder WithDependencies(IEnumerable<FileArtifact> dependencies)
        {
            m_dependencies = dependencies;
            return this;
        }

        public ProcessBuilder WithDirectoryDependencies(IEnumerable<DirectoryArtifact> directoryDependencies)
        {
            m_directoryDependencies = directoryDependencies;
            return this;
        }

        public ProcessBuilder WithPreserveOutputWhitelist(params AbsolutePath[] paths)
        {
            m_preserveOutputWhitelist = paths;
            return this;
        }

        public ProcessBuilder WithDirectoryDependenciesToConsume(IEnumerable<FileArtifact> directoryDependenciesToConsume)
        {
            m_directoryDependenciesToConsume = directoryDependenciesToConsume;
            return this;
        }

        public ProcessBuilder WithOutputs(params FileArtifact[] outputs)
        {
            Contract.Assert(m_attributedOutputs == null, "Only one version of WithOutputs method should be called");
            Contract.Assert(m_regularOutputs == null, "Only one version of WithOutputs method should be called");

            m_regularOutputs = outputs;
            return this;
        }
        
        public ProcessBuilder WithOutputs(IEnumerable<FileArtifact> outputs)
        {
            Contract.Assert(m_attributedOutputs == null, "Only one version of WithOutputs method should be called");
            Contract.Assert(m_regularOutputs == null, "Only one version of WithOutputs method should be called");

            m_regularOutputs = outputs;
            return this;
        }

        public ProcessBuilder WithOutputs(params FileArtifactWithAttributes[] outputs)
        {
            Contract.Assert(m_attributedOutputs == null, "Only one version of WithOutputs method should be called");
            Contract.Assert(m_regularOutputs == null, "Only one version of WithOutputs method should be called");

            m_attributedOutputs = outputs;
            return this;
        }
        
        public ProcessBuilder WithOutputs(IEnumerable<FileArtifactWithAttributes> outputs)
        {
            Contract.Assert(m_attributedOutputs == null, "Only one version of WithOutputs method should be called");
            Contract.Assert(m_regularOutputs == null, "Only one version of WithOutputs method should be called");

            m_attributedOutputs = outputs;
            return this;
        }

        public ProcessBuilder WithContext(BuildXLContext context)
        {
            m_context = context;
            return this;
        }

        public ProcessBuilder WithOrderDependencies(IEnumerable<PipId> orderDependencies)
        {
            m_orderDependencies = orderDependencies;
            return this;
        }

        public ProcessBuilder WithOptions(Process.Options options)
        {
            m_options |= options;
            return this;
        }

        public ProcessBuilder WithServiceInfo(ServiceInfo serviceInfo)
        {
            m_serviceInfo = serviceInfo;
            return this;
        }

        public Process Build()
        {
            Contract.Assert(m_executable != null, "WithExecutable method should be called before calling Build() method");

            return new Process(
                    executable: m_executable,
                    workingDirectory: m_workingDirectory,
                    arguments: m_arguments ?? m_argumentsFactory(this),
                    responseFile: FileArtifact.Invalid,
                    responseFileData: PipData.Invalid,
                    environmentVariables: ReadOnlyArray<EnvironmentVariable>.From(m_environmentVariables ?? Enumerable.Empty<EnvironmentVariable>()),
                    standardInput: FileArtifact.Invalid,
                    standardOutput: FileArtifact.Invalid,
                    standardError: FileArtifact.Invalid,
                    standardDirectory: m_standardDirectory,
                    warningTimeout: null,
                    timeout: null,
                    dependencies: ReadOnlyArray<FileArtifact>.From(CreateDependencies()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.From(Outputs), 
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.From(m_directoryDependencies ?? Enumerable.Empty<DirectoryArtifact>()),
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.From(m_orderDependencies ?? Enumerable.Empty<PipId>()),
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(m_untrackedPathes ?? Enumerable.Empty<AbsolutePath>()),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.From(m_untrackedScopes ?? Enumerable.Empty<AbsolutePath>()),
                    tags: ReadOnlyArray<StringId>.From(m_tags ?? Enumerable.Empty<StringId>()),
                    successExitCodes: ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: m_pipProvenance ?? PipProvenance.CreateDummy(m_context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                    options: m_options, 
                    serviceInfo: m_serviceInfo,
                    preserveOutputWhitelist: ReadOnlyArray<AbsolutePath>.From(m_preserveOutputWhitelist ?? Enumerable.Empty<AbsolutePath>()));
        }

        private IEnumerable<FileArtifact> CreateDependencies()
        {
            List<FileArtifact> dependencies = (m_dependencies ?? Enumerable.Empty<FileArtifact>()).ToList();
            if (!dependencies.Contains(m_executable))
            {
                return new[] { m_executable }.Concat(dependencies);
            }

            return dependencies;
        }

        public ProcessBuilder WithUntrackedPaths(IEnumerable<AbsolutePath> untrackedPathes)
        {
            m_untrackedPathes = untrackedPathes;
            return this;
        }

        public ProcessBuilder WithUntrackedScopes(IEnumerable<AbsolutePath> untrackedScopes)
        {
            m_untrackedScopes = untrackedScopes;
            return this;
        }

        public ProcessBuilder WithTags(IEnumerable<StringId> tags)
        {
            m_tags = tags;
            return this;
        }

        public ProcessBuilder WithProvenance(PipProvenance pipProvenance)
        {
            m_pipProvenance = pipProvenance;
            return this;
        }
    }
}
