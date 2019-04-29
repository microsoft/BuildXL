// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using static BuildXL.Pips.Operations.PipDataBuilder;

namespace BuildXL.Pips.Builders
{
    /// <summary>
    /// This class encapsulates all the information required by the ProcessBuilder
    /// to make decisions associated with a process' response file
    /// </summary>
    public struct ResponseFileSpecification
    {
        internal ResponseFileSpecification(bool forceCreation, Cursor firstArg, bool requiresArgument, string prefix, AbsolutePath explicitPath, PipData explicitData)
        {
            m_forceCreation = forceCreation;
            m_firstArg = firstArg;
            m_requiresArgument = requiresArgument;
            m_prefix = prefix;
            m_explicitPath = explicitPath;
            m_explicitData = explicitData;
        }

        private readonly bool m_forceCreation;
        private readonly Cursor m_firstArg;
        private readonly bool m_requiresArgument;
        private readonly string m_prefix;
        private readonly AbsolutePath m_explicitPath;
        private readonly PipData m_explicitData;

        /// <summary>
        /// Get a new builder for a ResponseFileSpecification
        /// </summary>
        public static ResponseFileSpecificationBuilder Builder() => new ResponseFileSpecificationBuilder();

        /// <summary>
        /// Configure the processBuilder's ResponseFile and ResponseFileData according to this specification.
        /// This method mutates the processBuilder, changing its argumentsBuilder, ResponseFile and ResponseFileData accordingly.
        /// We return the arguments to be passed to the process.
        /// </summary>
        internal PipData SplitArgumentsAndCreateResponseFileIfNeeded(ProcessBuilder processBuilder, DirectoryArtifact defaultDirectory, PathTable pathTable)
        {
            PipData arguments = default;    // We'll return the actual arguments to be passed to the process. 
            var argumentsBuilder = processBuilder.ArgumentsBuilder;
            var cutoffArg = m_firstArg;
            // We will create a response file in the following cases:
            //  1. If an explicit response file content was specified, either by having m_explicitData or
            //        by having m_forceCreation = true
            //  2. If the argument line is too long (longer than MaxCommandLineLength) and m_firstArg is not the default.
            // An additional argument is added to the process specifying the response file location, prefixed
            // by m_prefix, unless m_requiresArgument was set to false.
            if (!m_firstArg.IsDefault || m_explicitData.IsValid)
            {
                bool argumentLineTooLong = false;
                if (!m_forceCreation)
                {
                    // make a response file only if the command-line is too long
                    arguments = argumentsBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);

                    // Normalize choice to use response file by assuming paths are of length max path with a space. This will
                    // ensure there are no cases where changing the root will change whether a response file is used.
                    int cmdLineLength = arguments.GetMaxPossibleLength(pathTable.StringTable);
                    argumentLineTooLong = cmdLineLength > ProcessBuilder.MaxCommandLineLength;
                }

                if (m_forceCreation || argumentLineTooLong || m_explicitData.IsValid)
                {
                    // add the explicit contents if we have to
                    if (m_explicitData.IsValid)
                    {
                        if (!argumentLineTooLong)
                        {
                            // If there was no overflow, we mark 'here' as the starting
                            // point for the response file before adding the explicit data as an 'argument'.
                            cutoffArg = argumentsBuilder.CreateCursor();
                        }

                        argumentsBuilder.Add(m_explicitData);
                    }

                    // create a pip data for the stuff in the response file
                    processBuilder.ResponseFileData = argumentsBuilder.ToPipData(Environment.NewLine, PipDataFragmentEscaping.CRuntimeArgumentRules, cutoffArg);

                    // generate the file
                    processBuilder.ResponseFile = FileArtifact.CreateSourceFile(GetResponseFilePath(defaultDirectory, pathTable));

                    argumentsBuilder.TrimEnd(cutoffArg);

                    processBuilder.AddUntrackedFile(processBuilder.ResponseFile);

                    if (m_requiresArgument)
                    {
                        if (string.IsNullOrEmpty(m_prefix))
                        {
                            argumentsBuilder.Add(processBuilder.ResponseFile);
                        }
                        else
                        {
                            using (argumentsBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, pathTable.StringTable.Empty))
                            {
                                argumentsBuilder.Add(m_prefix);
                                argumentsBuilder.Add(processBuilder.ResponseFile);
                            }
                        }
                    }
                    arguments = argumentsBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
                }
            }
            else
            {
                arguments = argumentsBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
            }

            return arguments;
        }

        // Returns the explicit path to the response file if it was set, or a default path
        private AbsolutePath GetResponseFilePath(in DirectoryArtifact defaultDirectory, PathTable pathTable)
        {
            if (m_explicitPath.IsValid)
            {
                return m_explicitPath;
            }

            return defaultDirectory.Path.Combine(pathTable, PathAtom.Create(pathTable.StringTable, "args.rsp"));
        }

        /// <summary>
        /// Builder class for <see cref="ResponseFileSpecification"/>
        /// </summary>
        public class ResponseFileSpecificationBuilder
        {
            private bool m_forceCreation;
            private Cursor m_firstArg;
            private bool m_requiresArgument;
            private string m_prefix;
            private AbsolutePath m_explicitPath;
            private PipData m_explicitData;

            internal ResponseFileSpecificationBuilder()
            {
                m_forceCreation = false;
                m_firstArg = Cursor.Default; // indicates "no response file"
                m_requiresArgument = true;     // By default we will add an argument with the response file path
                m_explicitData = PipData.Invalid;   // indicates "no explicit data"
                m_explicitPath = AbsolutePath.Invalid;  // use a default value
            }

            /// <summary>
            /// Force creation of a response file. This should be specified with <see cref="AllowForRemainingArguments"/>
            /// Defaults to false
            /// </summary>
            public ResponseFileSpecificationBuilder ForceCreation(bool force)
            {
                m_forceCreation = force;
                return this;
            }

            /// <summary>
            /// Specifies that the process can use response files for the arguments starting from the specified Cursor.
            /// If this is the case and explicit contents for the response file were also specified, the overflowed
            /// arguments are prepended to the explicit contents.
            /// </summary>
            public ResponseFileSpecificationBuilder AllowForRemainingArguments(Cursor firstArgument)
            {
                m_firstArg = firstArgument;
                return this;
            }

            /// <summary>
            /// If this is set to true, an additional argument for the process will be added.
            /// with the path to the response file prefixed by responseFilePrefix.
            /// This defaults to true.
            /// </summary>
            public ResponseFileSpecificationBuilder RequiresArgument(bool requiresArgument)
            {
                m_requiresArgument = requiresArgument;
                return this;
            }

            /// <summary>
            /// A prefix for the command line argument with the response file location.
            /// </summary>
            public ResponseFileSpecificationBuilder Prefix(string prefix)
            {
                m_prefix = prefix;
                return this;
            }

            /// <summary>
            /// The required contents for the response file
            /// </summary>
            public ResponseFileSpecificationBuilder ExplicitData(PipData data)
            {
                m_explicitData = data;
                return this;
            }

            /// <summary>
            /// An optional path to where the response file should end up
            /// </summary>
            public ResponseFileSpecificationBuilder ExplicitPath(AbsolutePath path)
            {
                m_explicitPath = path;
                return this;
            }

            /// <nodoc />
            public ResponseFileSpecification Build()
            {
                return new ResponseFileSpecification(m_forceCreation, m_firstArg, m_requiresArgument, m_prefix, m_explicitPath, m_explicitData);
            }
        }
    }
}
