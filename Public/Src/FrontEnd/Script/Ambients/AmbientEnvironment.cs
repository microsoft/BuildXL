// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;
using Type = System.Type;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for <code>namespace Environment {}</code>
    /// </summary>
    public sealed class AmbientEnvironment : AmbientDefinitionBase
    {
        /// <nodoc />
        public AmbientEnvironment(PrimitiveTypes knownTypes)
            : base("Environment", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "Environment",
                new[]
                {
                    Function("getBooleanValue", GetBooleanValue, GetBooleanValueSignature),
                    Function("getFlag", GetFlag, GetFlagSignature),
                    Function("getNumberValue", GetNumberValue, GetNumberValueSignature),
                    Function("getStringValue", GetStringValue, GetStringValueSignature),
                    Function("getPathValue", GetPathValue, GetPathValueSignature),
                    Function("getPathValues", GetPathValues, GetPathValuesSignature),
                    Function("getFileValue", GetFileValue, GetFileValueSignature),
                    Function("getFileValues", GetFileValues, GetFileValuesSignature),
                    Function("getDirectoryValue", GetDirectoryValue, GetDirectoryValueSignature),
                    Function("getDirectoryValues", GetDirectoryValues, GetDirectoryValuesSignature),
                    Function("hasVariable", HasVariable, HasVariableSignature),
                    Function("newLine", NewLine, NewLineSignature),
                });
        }

        private static EvaluationResult GetBooleanValue(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var name = Args.AsString(args, 0);
            string strValue = GetRawValue(context, name);

            if (string.IsNullOrWhiteSpace(strValue) || !bool.TryParse(strValue, out bool value))
            {
                return ThrowInvalidFormatException<EvaluationResult>(name, strValue, pos: 1);
            }

            return EvaluationResult.Create(value);
        }

        private static EvaluationResult GetFlag(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var name = Args.AsString(args, 0);
            string strValue = GetRawValue(context, name);

            if (string.IsNullOrWhiteSpace(strValue))
            {
                return EvaluationResult.False;
            }

            switch (strValue.ToLowerInvariant())
            {
                case "0":
                case "false":
                    return EvaluationResult.False;
                case "1":
                case "true":
                    return EvaluationResult.True;
                default:
                    return ThrowInvalidFormatException<EvaluationResult>(name, strValue, pos: 1);
            }
        }


        private static EvaluationResult GetFileValue(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            AbsolutePath path = GetPathValue(context, args);
            return path.IsValid
                ? EvaluationResult.Create(FileArtifact.CreateSourceFile(path))
                : EvaluationResult.Undefined;
        }

        private static EvaluationResult GetFileValues(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return GetPathValues(context, args, typeof(FileArtifact));
        }

        private static EvaluationResult GetDirectoryValue(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            AbsolutePath path = GetPathValue(context, args);
            return path.IsValid
                ? EvaluationResult.Create(DirectoryArtifact.CreateWithZeroPartialSealId(path))
                : EvaluationResult.Undefined;
        }

        private static EvaluationResult GetDirectoryValues(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return GetPathValues(context, args, typeof(DirectoryArtifact));
        }

        private static EvaluationResult GetNumberValue(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var name = Args.AsString(args, 0);
            string strValue = GetRawValue(context, name);

            if (string.IsNullOrWhiteSpace(strValue) || !int.TryParse(strValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return ThrowInvalidFormatException<EvaluationResult>(name, strValue, pos: 1);
            }

            return EvaluationResult.Create(value);
        }

        private static EvaluationResult GetPathValue(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var path = GetPathValue(context, args);
            return path.IsValid ? EvaluationResult.Create(path) : EvaluationResult.Undefined;
        }

        private static EvaluationResult GetPathValues(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return GetPathValues(context, args, typeof(AbsolutePath));
        }

        private static AbsolutePath GetPathValue(Context context, EvaluationStackFrame args)
        {
            var name = Args.AsString(args, 0);
            string strValue = GetRawValue(context, name);

            if (string.IsNullOrWhiteSpace(strValue))
            {
                return AbsolutePath.Invalid;
            }

            return ParsePath(context, name, strValue, 1);
        }

        private static EvaluationResult GetPathValues(Context context, EvaluationStackFrame args, Type type)
        {
            var name = Args.AsString(args, 0);
            var separator = Args.AsString(args, 1);
            string strValue = GetRawValue(context, name);

            var entry = context.TopStack;

            if (string.IsNullOrWhiteSpace(strValue))
            {
                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(CollectionUtilities.EmptyArray<EvaluationResult>(), entry.InvocationLocation, entry.Path));
            }

            var values = separator.Length == 0 ? new[] { strValue } : strValue.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            var pathsOrFiles = new List<EvaluationResult>();

            for (int i = 0; i < values.Length; ++i)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    AbsolutePath path = ParsePath(context, name, values[i], 1);

                    EvaluationResult result =
                        type == typeof(AbsolutePath)      ? EvaluationResult.Create(path) :
                        type == typeof(FileArtifact)      ? EvaluationResult.Create(FileArtifact.CreateSourceFile(path)) :
                        type == typeof(DirectoryArtifact) ? EvaluationResult.Create(DirectoryArtifact.CreateWithZeroPartialSealId(path)) :
                        EvaluationResult.Undefined;

                    if (result.IsUndefined)
                    {
                        throw Contract.AssertFailure(I($"Cannot convert paths to typeof({type.Name})"));
                    }

                    pathsOrFiles.Add(result);
                }
            }

            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(pathsOrFiles.ToArray(), entry.InvocationLocation, entry.Path));
        }

        private static AbsolutePath ParsePath(Context context, string name, string value, int pos)
        {
            if (string.IsNullOrWhiteSpace(value) || !AbsolutePath.TryCreate(context.FrontEndContext.PathTable, value, out AbsolutePath path))
            {
                return ThrowInvalidFormatException<AbsolutePath>(name, value, pos);
            }

            return path;
        }

        private static EvaluationResult GetStringValue(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var name = Args.AsString(args, 0);
            var result = GetRawValue(context, name);

            return result != null ? EvaluationResult.Create(result) : EvaluationResult.Undefined;
        }

        private static EvaluationResult HasVariable(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var name = Args.AsString(args, 0);
            return EvaluationResult.Create(GetRawValue(context, name) != null);
        }

        private static EvaluationResult NewLine(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(Environment.NewLine);
        }

        private static T ThrowInvalidFormatException<T>(string name, string value, int pos)
        {
            Contract.Requires(name != null);

            throw new InvalidFormatException(
                    typeof(T),
                    I($"value of '{name}' is '{value ?? "undefined"}'"),
                    new ErrorContext(pos: pos));
        }

        private static string GetRawValue(Context context, string name)
        {
            string value;
            if (context.IsGlobalConfigFile)
            {
                var tuple = context.FrontEndHost.EnvVariablesUsedInConfig.AddOrUpdate(
                    name,
                    n => (true, Environment.GetEnvironmentVariable(name)),
                    (n, t) => (true, t.value));
                value = tuple.value;
            }
            else
            {
                context.FrontEndHost.Engine.TryGetBuildParameter(name, "DScript", out value);
            }

            return value;
        }

        private CallSignature NewLineSignature => CreateSignature(
            returnType: AmbientTypes.StringType);

        private CallSignature HasVariableSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.BooleanType);

        private CallSignature GetBooleanValueSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.BooleanType);

        private CallSignature GetFlagSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.BooleanType);

        private CallSignature GetFileValueSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.FileType);

        private CallSignature GetFileValuesSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType, AmbientTypes.StringType),
            returnType: AmbientTypes.FileType);

        private CallSignature GetNumberValueSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.NumberType);

        private CallSignature GetPathValueSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.PathType);

        private CallSignature GetPathValuesSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType, AmbientTypes.StringType),
            returnType: AmbientTypes.ArrayType);

        private CallSignature GetDirectoryValueSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.DirectoryType);

        private CallSignature GetDirectoryValuesSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType, AmbientTypes.StringType),
            returnType: AmbientTypes.ArrayType);

        private CallSignature GetStringValueSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.StringType);
    }
}
