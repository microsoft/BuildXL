// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Interface for building argument data.
    /// </summary>
    internal interface IArgumentsDataBuilder
    {
        /// <summary>
        /// Add string option.
        /// </summary>
        IArgumentsDataBuilder AddStringOption(string optionName, string value);

        /// <summary>
        /// Adds input file option.
        /// </summary>
        IArgumentsDataBuilder AddPathOption(string optionName, AbsolutePath path);

        /// <summary>
        /// Adds IPC moniker.
        /// </summary>
        IArgumentsDataBuilder AddIpcMonikerOption(string optionName, IIpcMoniker value);

        /// <summary>
        /// Adds file id.
        /// </summary>
        IArgumentsDataBuilder AddFileIdOption(string optionName, FileArtifact file);

        /// <summary>
        /// Adds directory id.
        /// </summary>
        IArgumentsDataBuilder AddDirectoryIdOption(string optionName, DirectoryArtifact directory);

        /// <summary>
        /// Adds VSO hash.
        /// </summary>
        IArgumentsDataBuilder AddVsoHashOption(string optionName, FileArtifact file);

        /// <summary>
        /// Completes data builder and gets the resulting pip data.
        /// </summary>
        PipData Finish();
    }

    /// <summary>
    /// Class for building pip data for arguments.
    /// </summary>
    internal sealed class ArgumentsDataBuilder : IArgumentsDataBuilder
    {
        private readonly PipDataBuilder m_builder;
        private bool m_finished;

        /// <summary>
        /// Creates an instance of <see cref="ArgumentsDataBuilder"/>
        /// </summary>
        public ArgumentsDataBuilder(PipDataBuilder builder)
        {
            Contract.Requires(builder != null);

            m_builder = builder;
            m_finished = false;
        }

        /// <summary>
        /// Creates an instance of <see cref="ArgumentsDataBuilder"/>
        /// </summary>
        public ArgumentsDataBuilder(StringTable stringTable)
            : this(new PipDataBuilder(stringTable))
        {
            Contract.Requires(stringTable != null);
        }

        /// <inheritdoc />
        public IArgumentsDataBuilder AddDirectoryIdOption(string optionName, DirectoryArtifact directory)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(directory.IsValid);
            Contract.Assert(!m_finished);

            AddOption(optionName, directory, (b, v) => b.AddDirectoryId(v));
            return this;
        }

        /// <inheritdoc />
        public IArgumentsDataBuilder AddFileIdOption(string optionName, FileArtifact file)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(file.IsValid);
            Contract.Assert(!m_finished);

            AddOption(optionName, file, (b, v) => b.AddFileId(v));
            return this;
        }

        /// <inheritdoc />
        public IArgumentsDataBuilder AddPathOption(string optionName, AbsolutePath path)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(path.IsValid);
            Contract.Assert(!m_finished);

            AddOption(optionName, path, (b, v) => b.Add(v));
            return this;
        }

        /// <inheritdoc />
        public IArgumentsDataBuilder AddIpcMonikerOption(string optionName, IIpcMoniker value)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Assert(!m_finished);

            AddOption(optionName, value, (b, v) => b.AddIpcMoniker(v));
            return this;
        }

        /// <inheritdoc />
        public IArgumentsDataBuilder AddStringOption(string optionName, string value)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(!string.IsNullOrEmpty(value));
            Contract.Assert(!m_finished);

            AddOption(optionName, value, (b, v) => b.Add(v));
            return this;
        }

        /// <inheritdoc />
        public IArgumentsDataBuilder AddVsoHashOption(string optionName, FileArtifact file)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(file.IsValid);
            Contract.Assert(!m_finished);

            AddOption(optionName, file, (b, v) => b.AddVsoHash(v));
            return this;
        }

        /// <summary>
        /// Completes this instance of <see cref="ArgumentsDataBuilder"/>.
        /// </summary>
        public PipData Finish()
        {
            m_finished = true;

            return m_builder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
        }

        private void AddOption<TValue>(string optionName, TValue value, Action<PipDataBuilder, TValue> writeValue)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(writeValue != null);
            Contract.Assert(!m_finished);

            using (m_builder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, string.Empty))
            {
                m_builder.Add(optionName);
                writeValue(m_builder, value);
            }
        }
    }

    /// <summary>
    /// Helper class for building argument for <see cref="ProcessBuilder"/>.
    /// </summary>
    internal sealed class ArgumentsBuilder
    {
        private readonly ProcessBuilder m_processBuilder;
        private readonly ArgumentsDataBuilder m_dataBuilder;
        private bool m_finished;

        /// <summary>
        /// Creates an instance of <see cref="ArgumentsBuilder"/>.
        /// </summary>
        public ArgumentsBuilder(ProcessBuilder processBuilder)
        {
            Contract.Requires(processBuilder != null);

            m_processBuilder = processBuilder;
            m_dataBuilder = new ArgumentsDataBuilder(processBuilder.ArgumentsBuilder);
            m_finished = false;
        }

        /// <summary>
        /// Add string option.
        /// </summary>
        public ArgumentsBuilder AddStringOption(string optionName, string value)
        {
            Contract.Requires(!string.IsNullOrEmpty(value));
            Contract.Assert(!m_finished);

            m_dataBuilder.AddStringOption(optionName, value);
            return this;
        }

        /// <summary>
        /// Adds input file option.
        /// </summary>
        public ArgumentsBuilder AddInputFileOption(string optionName, FileArtifact inputFile)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(inputFile.IsValid);
            Contract.Assert(!m_finished);

            m_dataBuilder.AddPathOption(optionName, inputFile.Path);
            m_processBuilder.AddInputFile(inputFile);
            return this;
        }

        /// <summary>
        /// Adds output file option.
        /// </summary>
        public ArgumentsBuilder AddOutputFileOption(string optionName, AbsolutePath outputPath)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(outputPath.IsValid);
            Contract.Assert(!m_finished);

            m_dataBuilder.AddPathOption(optionName, outputPath);
            m_processBuilder.AddOutputFile(outputPath);
            return this;
        }

        /// <summary>
        /// Adds IPC moniker.
        /// </summary>
        public ArgumentsBuilder AddIpcMonikerOption(string optionName, IIpcMoniker value)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(value != null);
            Contract.Assert(!m_finished);

            m_dataBuilder.AddIpcMonikerOption(optionName, value);
            return this;
        }

        /// <summary>
        /// Adds output directory.
        /// </summary>
        public ArgumentsBuilder AddOutputDirectoryOption(string optionName, AbsolutePath outputDirectory)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(outputDirectory.IsValid);
            Contract.Assert(!m_finished);

            m_dataBuilder.AddPathOption(optionName, outputDirectory);
            m_processBuilder.AddOutputDirectory(outputDirectory, SealDirectoryKind.Opaque);
            return this;
        }

        /// <summary>
        /// Adds input directory.
        /// </summary>
        public ArgumentsBuilder AddInputDirectoryOption(string optionName, DirectoryArtifact inputDirectory)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(inputDirectory.IsValid);
            Contract.Assert(!m_finished);

            m_dataBuilder.AddPathOption(optionName, inputDirectory.Path);
            m_processBuilder.AddInputDirectory(inputDirectory);
            return this;
        }

        /// <summary>
        /// Adds file id.
        /// </summary>
        public ArgumentsBuilder AddFileIdOption(string optionName, FileArtifact file)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(file.IsValid);
            Contract.Assert(!m_finished);

            m_dataBuilder.AddFileIdOption(optionName, file);
            return this;
        }

        /// <summary>
        /// Adds directory id.
        /// </summary>
        public ArgumentsBuilder AddDirectoryIdOption(string optionName, DirectoryArtifact directory)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(directory.IsValid);
            Contract.Assert(!m_finished);

            m_dataBuilder.AddDirectoryIdOption(optionName, directory);
            return this;
        }

        /// <summary>
        /// Adds VSO hash.
        /// </summary>
        public ArgumentsBuilder AddVsoHashOption(string optionName, FileArtifact file)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(file.IsValid);
            Contract.Assert(!m_finished);

            m_dataBuilder.AddVsoHashOption(optionName, file);
            return this;
        }

        /// <summary>
        /// Completes this instance of <see cref="ArgumentsBuilder"/>.
        /// </summary>
        public void Finish()
        {
            // Don't call m_dataBuilder.Finish() here because that's the responsibility of ProcessBuilder.
            m_finished = true;
        }
    }
}
