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
    /// Helper class for building argument for <see cref="ProcessBuilder"/>.
    /// </summary>
    internal sealed class ArgumentsBuilder
    {
        private readonly ProcessBuilder m_processBuilder;

        private PipDataBuilder Builder => m_processBuilder.ArgumentsBuilder;

        /// <summary>
        /// Creates an instance of <see cref="ArgumentsBuilder"/>.
        /// </summary>
        public ArgumentsBuilder(ProcessBuilder processBuilder)
        {
            Contract.Requires(processBuilder != null);
            m_processBuilder = processBuilder;
        }

        /// <summary>
        /// Add string option.
        /// </summary>
        public ArgumentsBuilder AddOption(string optionName, string value)
        {
            Contract.Requires(!string.IsNullOrEmpty(value));
            AddOption(optionName, value, (b, v) => b.Add(v));
            return this;
        }

        /// <summary>
        /// Adds input file option.
        /// </summary>
        public ArgumentsBuilder AddInputOption(string optionName, FileArtifact inputFile)
        {
            Contract.Requires(inputFile.IsValid);
            AddOption(optionName, inputFile, (b, v) => b.Add(inputFile.Path));
            m_processBuilder.AddInputFile(inputFile);
            return this;
        }

        /// <summary>
        /// Adds output file option.
        /// </summary>
        public ArgumentsBuilder AddOutputOption(string optionName, AbsolutePath outputPath)
        {
            Contract.Requires(outputPath.IsValid);
            AddOption(optionName, outputPath, (b, v) => b.Add(outputPath));
            m_processBuilder.AddOutputFile(outputPath);
            return this;
        }

        /// <summary>
        /// Adds IPC moniker.
        /// </summary>
        public ArgumentsBuilder AddIpcMonikerOption(string optionName, IIpcMoniker value)
        {
            Contract.Requires(value != null);
            AddOption(optionName, value, (b, v) => b.AddIpcMoniker(value));
            return this;
        }

        private void AddOption<TValue>(string optionName, TValue value, Action<PipDataBuilder, TValue> writeValue)
        {
            Contract.Requires(!string.IsNullOrEmpty(optionName));
            Contract.Requires(writeValue != null);

            using (Builder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, string.Empty))
            {
                Builder.Add(optionName);
                writeValue(Builder, value);
            }
        }
    }
}
