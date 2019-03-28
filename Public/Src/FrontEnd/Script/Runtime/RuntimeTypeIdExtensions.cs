// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Ambients.Set;
using BuildXL.FrontEnd.Script.Values;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Runtime
{
    /// <summary>
    /// Helpers for RuntimeTypeId
    /// </summary>
    public static class RuntimeTypeIdExtensions
    {
        /// <nodoc />
        private static readonly string[] s_runtimeStrings = CreateRuntimeStrings();

        /// <summary>
        /// Maps the RuntimeTypeId to a the runtime string representation
        /// </summary>
        public static string ToRuntimeString(this RuntimeTypeId runtimeTypeId)
        {
            Contract.Requires(runtimeTypeId != RuntimeTypeId.Unknown);

            return s_runtimeStrings[(int)runtimeTypeId];
        }

        /// <nodoc />
        private static string[] CreateRuntimeStrings()
        {
            var result = new string[24];
            result[(int)RuntimeTypeId.Unknown] = "<unknown>";
            result[(int)RuntimeTypeId.Undefined] = "undefined";
            result[(int)RuntimeTypeId.String] = "string";
            result[(int)RuntimeTypeId.Boolean] = "boolean";
            result[(int)RuntimeTypeId.Number] = "number";
            result[(int)RuntimeTypeId.Array] = "array";
            result[(int)RuntimeTypeId.Object] = "object";
            result[(int)RuntimeTypeId.Enum] = "enum";
            result[(int)RuntimeTypeId.Function] = "function";
            result[(int)RuntimeTypeId.ModuleLiteral] = "object";
            result[(int)RuntimeTypeId.Map] = "Map";
            result[(int)RuntimeTypeId.Set] = "Set";
            result[(int)RuntimeTypeId.Path] = "Path";
            result[(int)RuntimeTypeId.File] = "File";
            result[(int)RuntimeTypeId.Directory] = "Directory";
            result[(int)RuntimeTypeId.StaticDirectory] = "StaticDirectory";
            result[(int)RuntimeTypeId.SharedOpaqueDirectory] = "SharedOpaqueDirectory";
            result[(int)RuntimeTypeId.ExclusiveOpaqueDirectory] = "ExclusiveOpaqueDirectory";
            result[(int)RuntimeTypeId.SourceTopDirectory] = "SourceTopDirectory";
            result[(int)RuntimeTypeId.SourceAllDirectory] = "SourceAllDirectory";
            result[(int)RuntimeTypeId.FullStaticContentDirectory] = "FullStaticContentDirectory";
            result[(int)RuntimeTypeId.PartialStaticContentDirectory] = "PartialStaticContentDirectory";
            result[(int)RuntimeTypeId.RelativePath] = "RelativePath";
            result[(int)RuntimeTypeId.PathAtom] = "PathAtom";

#if DEBUG
            // In debug builds do some quick validation. This is only done once per app so okay to embed in product code.
            Contract.Assert(result.Length == Enum.GetValues(typeof(RuntimeTypeId)).Length, "Must have the same number of values in the enum as in the array we build.");
            for (int i = 0; i < result.Length; i++)
            {
                Contract.Assert(!string.IsNullOrEmpty(result[i]), "Each entry must be set.");
            }
#endif

            return result;
        }

        /// <summary>
        /// Dynamically inspects the object and returns the RuntimeTypeId
        /// </summary>
        public static RuntimeTypeId ComputeTypeOfKind(object value)
        {
            Contract.Assume(value != null);

            // In the future we'd like to use C# 7's Switch statements with patterns https://blogs.msdn.microsoft.com/dotnet/2016/08/24/whats-new-in-csharp-7-0/
            // Checking runtime type per pattern of: https://blogs.msdn.microsoft.com/vancem/2006/10/01/drilling-into-net-runtime-microbenchmarks-typeof-optimizations/
            if (value == UndefinedValue.Instance)
            {
                return RuntimeTypeId.Undefined;
            }

            var runtimeType = value.GetType();

            // Need to handle both cases: ArrayLiteral itself, and generic version.
            // In latter case, checking that runtimeType is assignable to ArrayLiteral.
            if (runtimeType == typeof(ArrayLiteral) || typeof(ArrayLiteral).IsAssignableFrom(runtimeType))
            {
                return RuntimeTypeId.Array;
            }

            if (runtimeType.IsObjectLikeLiteralType())
            {
                return RuntimeTypeId.Object;
            }

            if (runtimeType.IsModuleLiteral())
            {
                // We want to expand this to proper namespace node in the future when we deprecate moduleliteral
                return RuntimeTypeId.ModuleLiteral;
            }

            if (runtimeType == typeof(AbsolutePath))
            {
                return RuntimeTypeId.Path;
            }

            if (runtimeType == typeof(FileArtifact))
            {
                return RuntimeTypeId.File;
            }

            if (runtimeType == typeof(DirectoryArtifact))
            {
                return RuntimeTypeId.Directory;
            }

            if (runtimeType == typeof(StaticDirectory))
            {
                return GetRuntimeTypeForStaticDirectory(value as StaticDirectory);
            }

            if (runtimeType == typeof(RelativePath))
            {
                return RuntimeTypeId.RelativePath;
            }

            if (runtimeType == typeof(PathAtom))
            {
                return RuntimeTypeId.PathAtom;
            }

            if (runtimeType == typeof(string))
            {
                return RuntimeTypeId.String;
            }

            if (runtimeType == typeof(bool))
            {
                return RuntimeTypeId.Boolean;
            }

            if (runtimeType == typeof(int))
            {
                // We only support integers during evaluation, we'll have to expand this when we support more.
                return RuntimeTypeId.Number;
            }

            if (runtimeType == typeof(Closure))
            {
                return RuntimeTypeId.Function;
            }

            if (runtimeType == typeof(EnumValue))
            {
                return RuntimeTypeId.Enum;
            }

            if (runtimeType == typeof(OrderedSet))
            {
                return RuntimeTypeId.Set;
            }

            if (runtimeType == typeof(OrderedMap))
            {
                return RuntimeTypeId.Map;
            }

            throw Contract.AssertFailure(I($"Unexpected runtime value with type '{runtimeType}' encountered."));
        }

        private static RuntimeTypeId GetRuntimeTypeForStaticDirectory(StaticDirectory staticDirectory)
        {
            Contract.Requires(staticDirectory != null);

            switch (staticDirectory.SealDirectoryKind)
            {
                case SealDirectoryKind.Full:
                    return RuntimeTypeId.FullStaticContentDirectory;
                case SealDirectoryKind.Partial:
                    return RuntimeTypeId.PartialStaticContentDirectory;
                case SealDirectoryKind.Opaque:
                    return RuntimeTypeId.ExclusiveOpaqueDirectory;
                case SealDirectoryKind.SharedOpaque:
                    return RuntimeTypeId.SharedOpaqueDirectory;
                case SealDirectoryKind.SourceAllDirectories:
                    return RuntimeTypeId.SourceAllDirectory;
                case SealDirectoryKind.SourceTopDirectoryOnly:
                    return RuntimeTypeId.SourceTopDirectory;
                default:
                    throw Contract.AssertFailure(I($"Unexpected runtime kind '{staticDirectory.SealDirectoryKind}' encountered."));
            }
        }

        private static bool IsModuleLiteral(this Type runtimeType)
        {
            return runtimeType == typeof(FileModuleLiteral) ||
                   runtimeType == typeof(TypeOrNamespaceModuleLiteral);
        }

        /// <summary>
        /// CHecks if the runtimeType is an object literal or not.
        /// </summary>
        private static bool IsObjectLikeLiteralType(this Type runtimeType)
        {
            return runtimeType == typeof(ObjectLiteral) ||
                   runtimeType == typeof(ObjectLiteral0) ||
                   typeof(ObjectLiteralSlim).IsAssignableFrom(runtimeType) ||
                   runtimeType == typeof(ObjectLiteralN);
        }
    }
}
