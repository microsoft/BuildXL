// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Base class for ambients of values that support path query operations (e.g. FileArtifact, DirectoryArtifact).
    /// </summary>
    public abstract class AmbientBasePathQueries<T> : AmbientDefinition<T>
        where T : IImplicitPath
    {
        /// <nodoc />
        protected AmbientBasePathQueries(string ambientName, PrimitiveTypes knownTypes)
            : base(ambientName, knownTypes)
        {
        }

        /// <inheritdoc />
        protected sealed override Dictionary<StringId, CallableMember<T>> CreateMembers()
        {
            var result = new[]
            {
                // extension method/property
                CreateProperty<T>(AmbientName, Symbol("extension"), GetExtension),
                CreateProperty<T>(AmbientName, Symbol("hasExtension"), HasExtension),

                // parent method/property
                CreateProperty<T>(AmbientName, Symbol("parent"), GetParent),
                CreateProperty<T>(AmbientName, Symbol("hasParent"), HasParent),

                // name method/property
                CreateProperty<T>(AmbientName, Symbol("name"), GetName),

                // nameWithoutExtension method/property
                CreateProperty<T>(AmbientName, Symbol("nameWithoutExtension"), GetNameWithoutExtension),

                // isWithin method
                Create<T>(AmbientName, Symbol("isWithin"), IsWithin),

                // getRelative method
                Create<T>(AmbientName, Symbol("getRelative"), GetRelative),

                // path method/property
                CreateProperty<T>(AmbientName, Symbol("path"), GetPath),

                // pathRoot method/property
                CreateProperty<T>(AmbientName, Symbol("pathRoot"), GetPathRoot),

                // toDiagnosticString method
                Create<T>(AmbientName, Symbol("toDiagnosticString"), ToDiagnosticString),
            }.ToDictionary(m => m.Name.StringId, m => m);

            // Using Template method design pattern, when derived type only responsible for specific members
            foreach (var kvp in CreateSpecificMembers())
            {
                result.Add(kvp.Key, kvp.Value);
            }

            return result;
        }

        /// <summary>
        /// Factory method that provides specific members for the derived type.
        /// </summary>
        protected virtual Dictionary<StringId, CallableMember<T>> CreateSpecificMembers()
        {
            return new Dictionary<StringId, CallableMember<T>>();
        }

        /// <summary>
        /// Combines two paths: 'receiver' and 'arg as AbsolutePath'
        /// </summary>
        protected static EvaluationResult Combine(ImmutableContextBase context, AbsolutePath receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            Contract.Requires(context != null);
            Contract.Requires(receiver.IsValid);
            Contract.Requires(captures != null);

            var stringTable = context.FrontEndContext.StringTable;
            var pathTable = context.FrontEndContext.PathTable;

            Converter.ExpectPathFragment(stringTable, arg, out PathAtom pathAtom, out RelativePath relativePath, context: new ConversionContext(pos: 1));

            return EvaluationResult.Create(pathAtom.IsValid ? receiver.Combine(pathTable, pathAtom) : receiver.Combine(pathTable, relativePath));
        }

        /// <summary>
        /// Combines multiple paths: 'receiver' and 'arg as AbsolutePath[]'
        /// </summary>
        protected static EvaluationResult CombinePaths(ImmutableContextBase context, AbsolutePath receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            Contract.Requires(context != null);
            Contract.Requires(receiver.IsValid);
            Contract.Requires(captures != null);

            var pathTable = context.FrontEndContext.PathTable;
            var stringTable = context.FrontEndContext.StringTable;

            var argArray = Converter.ExpectArrayLiteral(arg, new ConversionContext(pos: 1));

            AbsolutePath currentPath = receiver;

            for (int i = 0; i < argArray.Length; i++)
            {
                Converter.ExpectPathFragment(
                    stringTable,
                    argArray[i],
                    out PathAtom pathAtom,
                    out RelativePath relativePath,
                    new ConversionContext(pos: i + 1, objectCtx: argArray));

                currentPath = pathAtom.IsValid ? currentPath.Combine(pathTable, pathAtom) : currentPath.Combine(pathTable, relativePath);
            }

            return EvaluationResult.Create(currentPath);
        }

        private static EvaluationResult GetExtension(Context context, T receiver, EvaluationStackFrame captures)
        {
            var pathTable = context.FrontEndContext.PathTable;
            PathAtom result = receiver.Path.GetExtension(pathTable);

            return result.IsValid ? EvaluationResult.Create(result) : EvaluationResult.Undefined;
        }

        private static EvaluationResult HasExtension(Context context, T receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Path.GetExtension(context.FrontEndContext.PathTable).IsValid);
        }

        private static EvaluationResult GetParent(Context context, T receiver, EvaluationStackFrame captures)
        {
            var pathTable = context.FrontEndContext.PathTable;
            var parent = receiver.Path.GetParent(pathTable);

            return parent.IsValid ? EvaluationResult.Create(parent) : EvaluationResult.Undefined;
        }

        private static EvaluationResult HasParent(Context context, T receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Path.GetParent(context.FrontEndContext.PathTable).IsValid);
        }

        private static EvaluationResult GetName(Context context, T receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Path.GetName(context.FrontEndContext.PathTable));
        }

        private static EvaluationResult GetNameWithoutExtension(Context context, T receiver, EvaluationStackFrame captures)
        {
            var pathTable = context.FrontEndContext.PathTable;
            var stringTable = context.FrontEndContext.StringTable;

            return EvaluationResult.Create(receiver.Path.GetName(pathTable).RemoveExtension(stringTable));
        }

        private static EvaluationResult IsWithin(Context context, T receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            Converter.ExpectPathOrDirectory(arg, out AbsolutePath pathContainer, out DirectoryArtifact directoryContainer, new ConversionContext(pos: 1));
            pathContainer = directoryContainer.IsValid ? directoryContainer.Path : pathContainer;

            return EvaluationResult.Create(receiver.Path.IsWithin(context.FrontEndContext.PathTable, pathContainer));
        }

        private static EvaluationResult GetRelative(Context context, T receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            AbsolutePath relativeToPath = Converter.ExpectPath(arg, strict: false, context: new ConversionContext(pos: 1));
            var pathTable = context.FrontEndContext.PathTable;

            if (!receiver.Path.TryGetRelative(pathTable, relativeToPath, out RelativePath relativePath))
            {
                return EvaluationResult.Undefined;
            }

            return relativePath.IsValid ? EvaluationResult.Create(relativePath) : EvaluationResult.Undefined;
        }

        private static EvaluationResult GetPath(Context context, T receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Path);
        }

        private static EvaluationResult GetPathRoot(Context context, T receiver, EvaluationStackFrame captures)
        {
            var pathTable = context.FrontEndContext.PathTable;

            var path = receiver.Path;
            return EvaluationResult.Create(path.GetRoot(pathTable));
        }

        private static EvaluationResult ToDiagnosticString(Context context, T receiver, EvaluationStackFrame captures)
        {
            var pathTable = context.FrontEndContext.PathTable;

            var path = receiver.Path;
            if (!path.IsValid)
            {
                return EvaluationResult.Create(string.Empty);
            }

            return EvaluationResult.Create(path.ToString(pathTable, PathFormat.HostOs));
        }
    }
}
