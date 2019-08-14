// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Special helper class that processes 'arguments' field for Transformer.execution method.
    /// </summary>
    internal sealed class TransformerExecuteArgumentsProcessor
    {
        private readonly PipDataBuilder ArgumentsBuilder;
        private readonly ProcessBuilder m_processBuilder;
        private readonly Context m_context;
        private readonly StringId EmptyStringId;

        private TransformerExecuteArgumentsProcessor(Context context, ProcessBuilder processBuilder)
        {
            Contract.Requires(processBuilder != null);
            Contract.Requires(context != null);

            ArgumentsBuilder = processBuilder.ArgumentsBuilder;
            m_processBuilder = processBuilder;
            m_context = context;
            EmptyStringId = context.StringTable.Empty;
    }

        /// <summary>
        /// Helper function that processes <paramref name="arguments"/> and adds appropriate inputs and outputs to the <paramref name="processBuilder"/>.
        /// </summary>
        public static void ProcessArguments(Context context, ProcessBuilder processBuilder, ArrayLiteral arguments)
        {
            Contract.Requires(context != null);
            Contract.Requires(processBuilder != null);
            Contract.Requires(arguments != null);
            new TransformerExecuteArgumentsProcessor(context, processBuilder).ProcessArguments(arguments);
        }

        private void ProcessArguments(ArrayLiteral arguments)
        {
            var processedArguments = CommandLineArgumentsConverter.ArrayLiteralToListOfArguments(m_context.StringTable, arguments);

            foreach(var argument in processedArguments)
            {
                // Not every arguments are valid.
                // Cmd.option function will return invalid argument if the argument value is undefined.
                if (!argument.IsDefined)
                {
                    continue;
                }

                // It could be just a switch argument, i.e., argument name without a value
                if (!argument.Value.IsDefined)
                {
                    AddOption(argument.Name, (string)null);
                    continue;
                }

                switch (argument.Value.Type)
                {
                    case CommandLineValueType.ScalarArgument:
                        AddScalarArgument(argument.Name, argument.Value.ScalarArgument);
                        break;
                    case CommandLineValueType.ScalarArgumentArray:
                        AddScalarArguments(argument.Name, argument.Value.ScalarArguments);
                        break;
                }
            }
        }

        private void AddOption(string prefix, string value)
        {
            AddOption(prefix, value, valueIsEmpty: string.IsNullOrEmpty(value), writeValue: (b, v) => b.Add(v));
        }

        private void AddOption(string prefix, AbsolutePath value)
        {
            AddOption(prefix, value, valueIsEmpty: value == AbsolutePath.Invalid, writeValue: (b, v) => b.Add(v));
        }

        private void AddOption(string prefix, PathAtom value)
        {
            AddOption(prefix, value, valueIsEmpty: value == PathAtom.Invalid, writeValue: (b, v) => b.Add(v));
        }

        private void AddOption(string prefix, RelativePath value)
        {
            AddOption(prefix, value, valueIsEmpty: value == RelativePath.Invalid, writeValue: (b, v) => b.Add(v));
        }

        private void AddOption(string prefix, IIpcMoniker value)
        {
            AddOption(prefix, value, valueIsEmpty: value == null, writeValue: (b, v) => b.AddIpcMoniker(v));
        }

        private void AddVsoHashOption(string prefix, FileArtifact value)
        {
            AddOption(prefix, value, valueIsEmpty: value == FileArtifact.Invalid, writeValue: (b, v) => b.AddVsoHash(v));
        }

        private void AddFileId(string prefix, FileArtifact value)
        {
            AddOption(
                prefix,
                value,
                valueIsEmpty: value == FileArtifact.Invalid,
                writeValue: (b, v) => b.AddFileId(v));
        }

        private void AddOption<TValue>(string prefix, TValue value, bool valueIsEmpty, Action<PipDataBuilder, TValue> writeValue)
        {
            // prefix and value are null -> skip
            if (string.IsNullOrEmpty(prefix) && valueIsEmpty)
            {
                return;
            }

            if (string.IsNullOrEmpty(prefix))
            {
                // This is unnamed argument
                writeValue(ArgumentsBuilder, value);
                return;
            }

            if (valueIsEmpty)
            {
                // This is a flag (or switch) kind of arguments
                ArgumentsBuilder.Add(prefix);
                return;
            }

            // both prefix and value are non-empty
            //   - handle the special case when prefix ends with space
            //       -> (1) add trimmed prefix, (2) add raw space, (3) add value
            if (prefix.EndsWith(" ", StringComparison.OrdinalIgnoreCase))
            {
                AddRawText(prefix.TrimEnd(' '));
                writeValue(ArgumentsBuilder, value);
                return;
            }

            using (ArgumentsBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, EmptyStringId))
            {
                ArgumentsBuilder.Add(prefix);
                writeValue(ArgumentsBuilder, value);
            }
        }

        private void AddRawText(string text)
        {
            using (ArgumentsBuilder.StartFragment(PipDataFragmentEscaping.NoEscaping, EmptyStringId))
            {
                ArgumentsBuilder.Add(text);
            }
        }

        private void AddArg(AbsolutePath path)
        {
            using (ArgumentsBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, EmptyStringId))
            {
                ArgumentsBuilder.Add(path);
            }
        }

        private void AddScalarArgument(string argumentName, in ArgumentValue scalar)
        {
            if (!scalar.IsDefined)
            {
                return;
            }

            switch (scalar.Type)
            {
                case ArgumentValueKind.PrimitiveValue:
                    AddPrimitiveValue(argumentName, ArgumentKind.Regular, scalar.PrimitiveValue);
                    break;
                case ArgumentValueKind.PrimitiveArgument:
                    AddPrimitiveValue(argumentName, scalar.PrimitiveArgument.Kind, scalar.PrimitiveArgument.Value);
                    break;
                case ArgumentValueKind.Artifact:
                    AddArtifact(argumentName, scalar.Artifact);
                    break;
                case ArgumentValueKind.CompoundValue:
                    AddCompoundValue(argumentName, scalar.CompoundValue);
                    break;
            }
        }

        private void AddPrimitiveValue(string argumentName, ArgumentKind kind, in PrimitiveValue value)
        {
            var stringValue = TryGetStringValue(value);

            switch (kind)
            {
                case ArgumentKind.RawText:
                    if (!string.IsNullOrEmpty(stringValue))
                    {
                        AddRawText(stringValue);
                    }

                    // TODO: Throw an exception for else case. Right now silently ignore the argument.
                    // TODO: Having contract exception because our API is not bullet-proof is incovenient for users.
                    break;
                case ArgumentKind.Regular:
                    // Regular command is not a flag (switch). If the value is missing, nothing should happen.
                    switch (value.Type)
                    {
                        case PrimitiveValueType.String:
                        case PrimitiveValueType.Number:
                            if (!string.IsNullOrEmpty(stringValue))
                            {
                                AddOption(argumentName, stringValue);
                            }

                            break;
                        case PrimitiveValueType.Path:
                            AddOption(argumentName, value.Path);
                            break;
                        case PrimitiveValueType.RelativePath:
                            AddOption(argumentName, value.RelativePath);
                            break;
                        case PrimitiveValueType.PathAtom:
                            AddOption(argumentName, value.PathAtom);
                            break;
                        case PrimitiveValueType.IpcMoniker:
                            AddOption(argumentName, value.IpcMoniker);
                            break;
                    }

                    break;
                case ArgumentKind.Flag:
                    AddOption(argumentName, (string)null);
                    break;
                case ArgumentKind.StartUsingResponseFile:
                    var forceUsingResponseFile = value.Type == PrimitiveValueType.String &&
                                                 value.String != null &&
                                                 value.String.Equals("true", StringComparison.OrdinalIgnoreCase);

                    var rspFileSpec =  ResponseFileSpecification.Builder()
                        .AllowForRemainingArguments(ArgumentsBuilder.CreateCursor())
                        .ForceCreation(forceUsingResponseFile)
                        .Prefix(argumentName ?? "@")
                        .Build();
                    m_processBuilder.SetResponseFileSpecification(rspFileSpec);
                    break;
                default:
                    Contract.Assert(
                        false,
                        I($"Unsupported argument kind '{kind}'"));
                    break;
            }

            string TryGetStringValue(PrimitiveValue v)
            {
                switch (v.Type)
                {
                    case PrimitiveValueType.Number:
                        return v.Number.ToString(CultureInfo.InvariantCulture);
                    case PrimitiveValueType.String:
                        return v.String;
                    default:
                        return null;
                }
            }
        }

        private void AddArtifact(string argumentName, in Artifact artifact)
        {
            if (!artifact.IsDefined)
            {
                // TODO: think about error message here! But maybe validation should be performed in a separate step.
                return;
            }

            if (artifact.Kind == ArtifactKind.Input)
            {
                switch (artifact.Type)
                {
                    case ArtifactValueType.File:
                        AddOption(argumentName, artifact.File.Path);
                        m_processBuilder.AddInputFile(artifact.File);
                        break;
                    case ArtifactValueType.Directory:
                        AddOption(argumentName, artifact.Directory.Path);
                        m_processBuilder.AddInputDirectory(artifact.Directory);
                        break;
                    case ArtifactValueType.AbsolutePath:
                        // For input dependencies AbsolutePath is equivalent to File.
                        AddOption(argumentName, artifact.Path);
                        m_processBuilder.AddInputFile(FileArtifact.CreateSourceFile(artifact.Path));
                        break;
                }
            }
            else if (artifact.Kind == ArtifactKind.Output)
            {
                switch (artifact.Type)
                {
                    case ArtifactValueType.File:
                        Contract.Assert(false); // should never happen because of preconditions in CommandLineArgumentsConverter
                        break;
                    case ArtifactValueType.Directory:
                        AddOption(argumentName, artifact.Directory.Path);
                        m_processBuilder.AddOutputDirectory(artifact.Directory, SealDirectoryKind.Opaque);
                        break;
                    case ArtifactValueType.AbsolutePath:
                        AddOption(argumentName, artifact.Path);
                        m_processBuilder.AddOutputFile(artifact.Path, FileExistence.Required);
                        break;
                }
            }
            else if (artifact.Kind == ArtifactKind.Rewritten)
            {
                switch (artifact.Type)
                {
                    case ArtifactValueType.File:
                        AddOption(argumentName, artifact.File.Path);
                        m_processBuilder.AddRewrittenFileInPlace(artifact.File);
                        break;
                    case ArtifactValueType.Directory:
                        Contract.Assert(false); // should never happen because of preconditions in CommandLineArgumentsConverter
                        break;
                    case ArtifactValueType.AbsolutePath:
                        AddOption(argumentName, artifact.Path);
                        m_processBuilder.AddRewrittenFileWithCopy(artifact.Path, artifact.Original);
                        break;
                }
            }
            else if (artifact.Kind == ArtifactKind.None)
            {
                switch (artifact.Type)
                {
                    case ArtifactValueType.File:
                        AddOption(argumentName, artifact.File.Path);
                        break;
                    case ArtifactValueType.Directory:
                        AddOption(argumentName, artifact.Directory.Path);
                        break;
                    case ArtifactValueType.AbsolutePath:
                        AddOption(argumentName, artifact.Path);
                        break;
                }
            }
            else if (artifact.Kind == ArtifactKind.VsoHash)
            {
                switch (artifact.Type)
                {
                    case ArtifactValueType.File:
                        AddVsoHashOption(argumentName, artifact.File);
                        m_processBuilder.AddInputFile(artifact.File);
                        break;
                    case ArtifactValueType.Directory:
                        Contract.Assert(false); // should never happen because of preconditions in CommandLineArgumentsConverter
                        break;
                    case ArtifactValueType.AbsolutePath:
                        Contract.Assert(false); // should never happen because of preconditions in CommandLineArgumentsConverter
                        break;
                }
            }
            else if (artifact.Kind == ArtifactKind.FileId)
            {
                switch (artifact.Type)
                {
                    case ArtifactValueType.File:
                        // AddOption(argumentName, FileId.ToString(artifact.File));
                        AddFileId(argumentName, artifact.File);
                        m_processBuilder.AddInputFile(artifact.File);
                        break;
                    case ArtifactValueType.Directory:
                        Contract.Assert(false); // should never happen because of preconditions in CommandLineArgumentsConverter
                        break;
                    case ArtifactValueType.AbsolutePath:
                        Contract.Assert(false); // should never happen because of preconditions in CommandLineArgumentsConverter
                        break;
                }
            }
            else if (artifact.Kind == ArtifactKind.SharedOpaque)
            {
                switch (artifact.Type)
                {
                    case ArtifactValueType.File:
                        Contract.Assert(false); // should never happen because of preconditions in CommandLineArgumentsConverter
                        break;
                    case ArtifactValueType.Directory:
                        AddOption(argumentName, artifact.Directory);
                        m_processBuilder.AddOutputDirectory(artifact.Directory, SealDirectoryKind.SharedOpaque);
                        break;
                    case ArtifactValueType.AbsolutePath:
                        Contract.Assert(false); // should never happen because of preconditions in CommandLineArgumentsConverter
                        break;
                }
            }
            else if (artifact.Kind == ArtifactKind.DirectoryId)
            {
                switch (artifact.Type)
                {
                    case ArtifactValueType.File:
                        Contract.Assert(false); // should never happen because of preconditions in CommandLineArgumentsConverter
                        break;
                    case ArtifactValueType.Directory:
                        AddOption(argumentName, DirectoryId.ToString(artifact.Directory));
                        m_processBuilder.AddInputDirectory(artifact.Directory);
                        break;
                    case ArtifactValueType.AbsolutePath:
                        Contract.Assert(false); // should never happen because of preconditions in CommandLineArgumentsConverter
                        break;
                }
            }
        }

        private void AddScalarArguments(string argumentName, ArgumentValue[] scalarArguments)
        {
            Contract.Requires(scalarArguments != null);

            // Array of arguments acts like a multiplicator
            foreach (ArgumentValue scalar in scalarArguments)
            {
                AddScalarArgument(argumentName, scalar);
            }
        }

        private void AddCompoundValue(string argument, CompoundArgumentValue compoundValue)
        {
            // Argument list is actually a single argument that has multiple values.
            if (!compoundValue.IsDefined)
            {
                return;
            }

            compoundValue.TraverseAllArtifacts(this,
                (@this, artifact) =>
                {
                    switch (artifact.Type)
                    {
                        case ArtifactValueType.File:
                            var file = artifact.File;
                            if (artifact.Kind == ArtifactKind.Input || artifact.Kind == ArtifactKind.VsoHash)
                            {
                                m_processBuilder.AddInputFile(file);
                            }
                            else if (artifact.Kind == ArtifactKind.Output)
                            {
                                m_processBuilder.AddOutputFile(file, FileExistence.Required);
                            }
                            else if (artifact.Kind == ArtifactKind.Rewritten)
                            {
                                m_processBuilder.AddRewrittenFileInPlace(file);
                            }
                            break;
                        case ArtifactValueType.Directory:
                            var dir = artifact.Directory;
                            if (artifact.Kind == ArtifactKind.Input)
                            {
                                m_processBuilder.AddInputDirectory(dir);
                            }
                            else if (artifact.Kind == ArtifactKind.Output)
                            {
                                m_processBuilder.AddOutputDirectory(dir, SealDirectoryKind.Opaque);
                            }
                            break;
                        case ArtifactValueType.AbsolutePath:
                            var path = artifact.Path;
                            if (artifact.Kind == ArtifactKind.Input)
                            {
                                m_processBuilder.AddInputFile(FileArtifact.CreateSourceFile(path));
                            }
                            else if (artifact.Kind == ArtifactKind.Output)
                            {
                                m_processBuilder.AddOutputFile(path, FileExistence.Required);
                            }
                            else if (artifact.Kind == ArtifactKind.Rewritten)
                            {
                                m_processBuilder.AddRewrittenFileWithCopy(path, artifact.Original);
                            }
                            break;
                    }
                });

            using (var pipDataBuilderWrapper = m_context.FrontEndContext.GetPipDataBuilder())
            {
                // Previously if we have
                //      Cmd.option("--platform ", "x64"),
                //      Cmd.option("--opt ", Cmd.join(";", ["a", "b"]))
                // and that option gets written into rsp file  then we will have
                //      --platform
                //      x64
                //      --opt a; b
                // The last one is not consistent.
                //
                // With the code below, we will have
                //      --platform
                //      x64
                //      --opt
                //      a; b
                //
                // However, if we have
                //      Cmd.option("--platform:", "x64"),
                //      Cmd.option("--opt:", Cmd.join(";", ["a", "b"]))
                // then we still have in the rsp file
                //      --platform: x64
                //      --opt: a; b
                if (!string.IsNullOrEmpty(argument))
                {
                    if (argument.EndsWith(" ", StringComparison.OrdinalIgnoreCase))
                    {
                        AddRawText(argument.TrimEnd(' '));
                    }
                    else
                    {
                        pipDataBuilderWrapper.Instance.Add(argument);
                    }
                }

                CompoundValueToPipData(pipDataBuilderWrapper.Instance, compoundValue);

                var pipData = pipDataBuilderWrapper.Instance.ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping);
                ArgumentsBuilder.Add(pipData);
            }
        }

        private void ScalarToPipData(PipDataBuilder pipDataBuilder, in ArgumentValue scalar)
        {
            switch (scalar.Type)
            {
                
                case ArgumentValueKind.PrimitiveValue:
                    PrimitiveValueToPipData(pipDataBuilder, scalar.PrimitiveValue);
                    break;
                case ArgumentValueKind.PrimitiveArgument:
                    PrimitiveValueToPipData(pipDataBuilder, scalar.PrimitiveArgument.Value);
                    break;
                case ArgumentValueKind.Artifact:
                    ArtifactToPipData(pipDataBuilder, scalar.Artifact);
                    break;
                case ArgumentValueKind.CompoundValue:
                    CompoundValueToPipData(pipDataBuilder, scalar.CompoundValue);
                    break;
            }
        }

        private static void PrimitiveValueToPipData(PipDataBuilder pipDataBuilder, in PrimitiveValue primitiveValue)
        {
            switch (primitiveValue.Type)
            {
                case PrimitiveValueType.String:
                    pipDataBuilder.Add(primitiveValue.String);
                    break;
                case PrimitiveValueType.Number:
                    pipDataBuilder.Add(primitiveValue.Number.ToString(CultureInfo.InvariantCulture));
                    break;
                case PrimitiveValueType.Path:
                    pipDataBuilder.Add(primitiveValue.Path);
                    break;
                case PrimitiveValueType.RelativePath:
                    pipDataBuilder.Add(primitiveValue.RelativePath);
                    break;
                case PrimitiveValueType.PathAtom:
                    pipDataBuilder.Add(primitiveValue.PathAtom);
                    break;
            }
        }

        private static void ArtifactToPipData(PipDataBuilder pipDataBuilder, in Artifact artifact)
        {
            switch (artifact.Type)
            {
                case ArtifactValueType.File:
                    if (artifact.Kind == ArtifactKind.VsoHash)
                    {
                        pipDataBuilder.AddVsoHash(artifact.File);
                    }
                    else
                    {
                        pipDataBuilder.Add(artifact.File.Path);
                    }

                    break;
                case ArtifactValueType.Directory:
                    pipDataBuilder.Add(artifact.Directory.Path);
                    break;
                case ArtifactValueType.AbsolutePath:
                    pipDataBuilder.Add(artifact.Path);
                    break;
            }
        }

        private void CompoundValueToPipData(PipDataBuilder pipDataBuilder, CompoundArgumentValue compoundValue)
        {
            using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.NoEscaping, compoundValue.Separator))
            {
                foreach (var value in compoundValue.Values.AsStructEnumerable())
                {
                    ScalarToPipData(pipDataBuilder, value);
                }
            }
        }
    }
}
