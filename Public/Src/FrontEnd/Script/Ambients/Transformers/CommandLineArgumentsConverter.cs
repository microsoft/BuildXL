// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Class that converts object literals to custom argument instances.
    /// </summary>
    internal sealed class CommandLineArgumentsConverter
    {
        private readonly Names m_names;

        private CommandLineArgumentsConverter(Names names)
        {
            Contract.Requires(names != null);
            m_names = names;
        }

        /// <summary>
        /// Factory that converts array literal to an array of <see cref="Argument" /> instances.
        /// </summary>
        public static IEnumerable<Argument> ArrayLiteralToListOfArguments(Names names, ArrayLiteral literal)
        {
            Contract.Requires(names != null);
            Contract.Requires(literal != null);

            return GetInstance(names).ConvertArrayOfArguments(literal);
        }

        /// <summary>
        /// Factory that converts object literal to an <see cref="Argument" /> instance.
        /// </summary>
        public static Argument ObjectLiteralToArgument(Names names, ObjectLiteral literal)
        {
            Contract.Requires(names != null);
            Contract.Requires(literal != null);

            return GetInstance(names).ConvertArgument(EvaluationResult.Create(literal), -1);
        }

        private static CommandLineArgumentsConverter GetInstance(Names names)
        {
            Contract.Requires(names != null);
            Contract.Ensures(Contract.Result<CommandLineArgumentsConverter>() != null);

            return new CommandLineArgumentsConverter(names);
        }

        private IEnumerable<Argument> ConvertArrayOfArguments(ArrayLiteral literal)
        {
            Contract.Requires(literal != null);
            for (int i = 0; i < literal.Length; ++i)
            {
                var l = literal[i];
                if (!l.IsUndefined)
                {
                    var x = ConvertArgument(l, i, literal);

                    if (x.IsDefined)
                    {
                        yield return x;
                    }
                }
            }
        }

        private Argument ConvertArgument(EvaluationResult argumentValue, int index, object origin = null)
        {
            Contract.Requires(argumentValue != null);

            var literal = Converter.ExpectObjectLiteral(argumentValue, new ConversionContext(allowUndefined: false, objectCtx: origin, pos: index));

            var name = Converter.ExtractString(literal, m_names.CmdArgumentNameField, allowUndefined: true);
            var value = ConvertCommandLineValue(literal);

            return new Argument(name, value);
        }

        private CommandLineValue ConvertCommandLineValue(ObjectLiteral literal)
        {
            // Argument interface definition:
            // interface Argument {name?: string; value: ArgumentValue | ArgumentValue[];}
            var value = literal[m_names.CmdArgumentValueField];

            // value is not required field but can be null. In this case the argument would be skipped, no error should be emitted.
            if (value.IsUndefined)
            {
                return default(CommandLineValue);
            }

            if (value.Value is ArrayLiteral arrayValue)
            {
                // case: ArgumentValue[]
                var arrayOfScalarArguments = ConvertArrayOfScalarArguments(arrayValue);
                return new CommandLineValue(arrayOfScalarArguments);
            }
            
            // case: ArgumentValue
            var scalarArgument = TryConvertScalarArgument(value);
            if (scalarArgument.IsDefined)
            {
                return new CommandLineValue(scalarArgument);
            }

            throw Converter.UnexpectedTypeException(
                m_names.CmdArgumentValueField,
                value,
                literal,
                typeof(ArgumentValue),
                typeof(ArgumentValue[]));
        }

        private static PrimitiveValue ConvertPrimitiveValue(PropertyValue value)
        {
            var result = TryConvertPrimitiveValue(value.Value);
            if (result.IsDefined)
            {
                return result;
            }

            throw Converter.UnexpectedTypeException(
                value.PropertyName,
                value.Value,
                value.Literal,
                typeof(int),
                typeof(string),
                typeof(AbsolutePath),
                typeof(RelativePath),
                typeof(PathAtom));
        }

        private static PrimitiveValue TryConvertPrimitiveValue(EvaluationResult result)
        {
            object value = result.Value;
            if (value == null)
            {
                return default(PrimitiveValue);
            }

            if (value is string stringValue)
            {
                return new PrimitiveValue(stringValue);
            }

            if (value is AbsolutePath path)
            {
                return new PrimitiveValue(path);
            }

            if (value is RelativePath relativePath)
            {
                return new PrimitiveValue(relativePath);
            }

            if (value is PathAtom atom)
            {
                return new PrimitiveValue(atom);
            }

            if (value is int i)
            {
                return new PrimitiveValue(i);
            }

            if (value is IIpcMoniker moniker)
            {
                return new PrimitiveValue(moniker);
            }

            return default(PrimitiveValue);
        }

        private ArgumentValue TryConvertScalarArgument(EvaluationResult value)
        {
            // ScalarArgument is PrimitiveValue | Artifact | PrimitiveArgument | CompoundArgumentValue

            if (value.Value is ObjectLiteral objValue)
            {
                // case: CompoundArgumentValue; distinguishing fields: 'separator', 'values'
                if (!objValue[m_names.CmdListArgumentSeparatorField].IsUndefined ||
                    !objValue[m_names.CmdListArgumentValuesField].IsUndefined)
                {
                    return new ArgumentValue(ConvertCompoundArgument(objValue));
                }

                // case: Artifact; distinguishing field: 'path'
                if (!objValue[m_names.CmdArtifactPathField].IsUndefined)
                {
                    return new ArgumentValue(ConvertArtifact(objValue));
                }

                // case: PrimitiveArgument;
                if (!objValue[m_names.CmdArtifactKindField].IsUndefined)
                {
                    return new ArgumentValue(ConvertPrimitiveArgument(objValue));
                }
            }
            else
            {
                var primitiveValue = TryConvertPrimitiveValue(value);
                if (primitiveValue.IsDefined)
                {
                    return new ArgumentValue(primitiveValue);
                }
            }

            return default(ArgumentValue);
        }

        private ArgumentValue ConvertScalarArgument(PropertyValue value)
        {
            var result = TryConvertScalarArgument(value.Value);
            if (result.IsDefined)
            {
                return result;
            }

            throw Converter.UnexpectedTypeException(
                value.PropertyName,
                value.Value,
                value.Literal,
                typeof(ArgumentValue),
                typeof(Artifact),
                typeof(PrimitiveArgument));
        }

        private ArgumentValue[] ConvertArrayOfScalarArguments(ArrayLiteral scalarArguments)
        {
            Contract.Requires(scalarArguments != null);

            var results = new ArgumentValue[scalarArguments.Length];

            for (int i = 0; i < scalarArguments.Length; ++i)
            {
                results[i] = ConvertScalarArgument(new PropertyValue(SymbolAtom.Invalid, scalarArguments, scalarArguments[i]));
            }

            return results;
        }

        private CompoundArgumentValue ConvertCompoundArgument(ObjectLiteral value)
        {
            Contract.Requires(value != null);

            // 'separator' is a required field. Fail if it is missing or has wrong type.
            var separator = Converter.ExpectString(
                value[m_names.CmdListArgumentSeparatorField],
                new ConversionContext(name: m_names.CmdListArgumentSeparatorField, objectCtx: value));

            // 'values' is a required field.
            var values = Converter.ExpectArrayLiteral(
                value[m_names.CmdListArgumentValuesField],
                new ConversionContext(name: m_names.CmdListArgumentValuesField, objectCtx: value));

            var scalarArguments = ConvertArrayOfScalarArguments(values);
            return new CompoundArgumentValue(separator, scalarArguments);
        }

        private PrimitiveArgument ConvertPrimitiveArgument(ObjectLiteral objectLiteral)
        {
            var kindEnum = Converter.ExpectEnumValue(
                objectLiteral[m_names.CmdPrimitiveArgumentKindField],
                new ConversionContext(name: m_names.CmdPrimitiveArgumentKindField, objectCtx: objectLiteral));

            // Values should be in sync, so cast is safe
            var argumentKind = (ArgumentKind)kindEnum.Value;

            var fieldValue = objectLiteral[m_names.CmdPrimitiveArgumentValueField];

            var value = fieldValue.IsUndefined
                ? default(PrimitiveValue)
                : ConvertPrimitiveValue(new PropertyValue(m_names.CmdPrimitiveArgumentValueField, objectLiteral, fieldValue));

            return new PrimitiveArgument(argumentKind, value);
        }

        private static void ThrowConvertExceptionIf<TExpectedType>(bool condition, EvaluationResult value, ConversionContext convContext, string errorDescription)
        {
            if (condition)
            {
                throw new ConvertException(new[] { typeof(TExpectedType) }, value, convContext.ErrorContext, errorDescription);
            }
        }

        private Artifact ConvertArtifact(ObjectLiteral literal)
        {
            // interface Artifact { value: File | Directory | StaticDirectory; kind: ArtifactKind; }
            // const enum ArtifactKind { input = 1, output, rewritten, none, vsoHash, fileId };
            var kind = Converter.ExpectEnumValue(
                literal[m_names.CmdArtifactKindField],
                new ConversionContext(name: m_names.CmdArtifactKindField, objectCtx: literal));

            // Values should be in sync, so freely convert numbers.
            var artifactKind = (ArtifactKind)kind.Value;
            var value = literal[m_names.CmdArtifactPathField];
            var original = literal[m_names.CmdArtifactOriginalField];
            var convContext = new ConversionContext(name: m_names.CmdArtifactPathField, objectCtx: literal);

            Converter.ExpectPathOrFileOrDirectory(value, out FileArtifact file, out DirectoryArtifact directory, out AbsolutePath path, convContext);

            ThrowConvertExceptionIf<AbsolutePath>(artifactKind == ArtifactKind.Output && !path.IsValid && !directory.IsValid, value, convContext, "Output artifacts must be specified as paths or directories.");
            ThrowConvertExceptionIf<AbsolutePath>(artifactKind == ArtifactKind.Rewritten && !path.IsValid && !file.IsValid, value, convContext, "Rewritten artifacts must be specified as files if in-place rewrite or as paths otherwise.");
            ThrowConvertExceptionIf<FileArtifact>(artifactKind == ArtifactKind.VsoHash && !file.IsValid, value, convContext, "VsoHash artifacts must be specified as files.");
            ThrowConvertExceptionIf<FileArtifact>(artifactKind == ArtifactKind.FileId && !file.IsValid, value, convContext, "FileId artifacts must be specified as files.");
            ThrowConvertExceptionIf<DirectoryArtifact>(artifactKind == ArtifactKind.SharedOpaque && !directory.IsValid, value, convContext, "Shared opaque must be specified as directories.");
            ThrowConvertExceptionIf<DirectoryArtifact>(artifactKind == ArtifactKind.DirectoryId && !directory.IsValid, value, convContext, "DirectoryId artifacts must be specified as directories.");

            var originalFile = original.IsUndefined
                ? FileArtifact.Invalid
                : Converter.ExpectFile(original, false, new ConversionContext(name: m_names.CmdArtifactOriginalField, objectCtx: literal));

            return file.IsValid
                ? new Artifact(artifactKind, file)
                : (directory.IsValid
                    ? new Artifact(artifactKind, directory)
                    : new Artifact(artifactKind, path, originalFile));
        }

        /// <summary>
        /// Simple struct that contains property value of the literal object.
        /// </summary>
        internal readonly struct PropertyValue
        {
            public SymbolAtom PropertyName { get; }

            public ObjectLiteral Literal { get; }

            public EvaluationResult Value { get; }

            public PropertyValue(SymbolAtom propertyName, ObjectLiteral literal, EvaluationResult value)
            {
                PropertyName = propertyName;
                Literal = literal;
                Value = value;
            }
        }
    }
}
