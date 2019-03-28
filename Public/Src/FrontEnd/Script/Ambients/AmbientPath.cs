// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Globalization;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients.Exceptions;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for <code>namespace Path {}</code> and <code>interface Path {}</code>.
    /// </summary>
    public sealed class AmbientPath : AmbientBasePathQueries<AbsolutePath>
    {
        /// <nodoc />
        public AmbientPath(PrimitiveTypes knownTypes)
            : base("Path", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<AbsolutePath>> CreateSpecificMembers()
        {
            return new Dictionary<StringId, CallableMember<AbsolutePath>>
            {
                { NameId("changeExtension"), Create<AbsolutePath>(AmbientName, Symbol("changeExtension"), ChangeExtension) },
                { NameId("relocate"), CreateN<AbsolutePath>(AmbientName, Symbol("relocate"), Relocate, minArity: 2, maxArity: 3) },
                { NameId("combine"), Create<AbsolutePath>(AmbientName, Symbol("combine"), Combine) },
                { NameId("combinePaths"), Create<AbsolutePath>(AmbientName, Symbol("combinePaths"), CombinePaths, rest: true) },
                { NameId("concat"), Create<AbsolutePath>(AmbientName, Symbol("concat"), Concat) },
                // This member is obsolete
                { NameId("extend"), Create<AbsolutePath>(AmbientName, Symbol("extend"), Concat) },
            };
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "Path",
                new[]
                {
                    Function("interpolate", Interpolate, InterpolateSignature),
                });
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1820")]
        private static EvaluationResult ChangeExtension(Context context, AbsolutePath receiver, EvaluationResult extension, EvaluationStackFrame captures)
        {
            var stringTable = context.FrontEndContext.StringTable;
            var pathTable = context.FrontEndContext.PathTable;

            // An empty string is not a valid PathAtom, but in this case it indicates to remove the extension
            // this maps to passing an invalid path atom to PathAtom.ChangeExtension
            var atom = extension.Value as string == string.Empty
                ? PathAtom.Invalid
                : Converter.ExpectPathAtomFromStringOrPathAtom(stringTable, extension, new ConversionContext(pos: 1));

            return EvaluationResult.Create(atom.IsValid ? receiver.ChangeExtension(pathTable, atom) : receiver.RemoveExtension(pathTable));
        }

        private static EvaluationResult Relocate(Context context, AbsolutePath receiver, EvaluationResult[] args, EvaluationStackFrame captures)
        {
            var pathTable = context.FrontEndContext.PathTable;

            Args.AsPathOrDirectory(args, 0, out AbsolutePath sourceContainer, out DirectoryArtifact sourceDirContainer);
            sourceContainer = sourceDirContainer.IsValid ? sourceDirContainer.Path : sourceContainer;

            if (!receiver.IsWithin(pathTable, sourceContainer))
            {
                string message = string.Format(CultureInfo.CurrentCulture, $"Relocated path '{receiver.ToString(pathTable)}' is not inside the relocation source '{sourceContainer.ToString(pathTable)}'");
                throw new InvalidPathOperationException(message, new ErrorContext(pos: 1));
            }

            Args.AsPathOrDirectory(args, 1, out AbsolutePath targetContainer, out DirectoryArtifact targetDirContainer);
            targetContainer = targetDirContainer.IsValid ? targetDirContainer.Path : targetContainer;

            object newExtension = Args.AsIs(args, 2);

            AbsolutePath result;
            if (newExtension == UndefinedValue.Instance)
            {
                result = receiver.Relocate(pathTable, sourceContainer, targetContainer);
            }
            else
            {
                var stringTable = context.FrontEndContext.StringTable;
                PathAtom newExt = Converter.ExpectPathAtomFromStringOrPathAtom(stringTable, args[2], new ConversionContext(pos: 3));

                result = receiver.Relocate(pathTable, sourceContainer, targetContainer, newExt);
            }

            return result.IsValid ? EvaluationResult.Create(result) : EvaluationResult.Undefined;
        }

        private static EvaluationResult Concat(Context context, AbsolutePath receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var pathTable = context.FrontEndContext.PathTable;
            var stringTable = context.FrontEndContext.StringTable;

            PathAtom atom = Converter.ExpectPathAtomFromStringOrPathAtom(stringTable, arg, new ConversionContext(pos: 1));

            return EvaluationResult.Create(receiver.Concat(pathTable, atom));
        }

        /// <summary>
        /// Implements path interpolation
        /// </summary>
        public static EvaluationResult Interpolate(ImmutableContextBase context, ModuleLiteral env, EvaluationStackFrame args)
        {
            // TODO: Path.interpolate(x, y, z) is similar to x.combinePaths(y, z). The latter can be slightly more efficient because no look-up for "Path" identifier.
            Args.CheckArgumentIndex(args, 1);
            var rest = Args.AsArrayLiteral(args, 1);

            return Interpolate(context, args[0], rest.Values);
        }

        /// <summary>
        /// Implements path interpolation
        /// </summary>
        internal static EvaluationResult Interpolate(ImmutableContextBase context, EvaluationResult root, IReadOnlyList<EvaluationResult> pathFragments)
        {
            var pathTable = context.FrontEndContext.PathTable;
            var strTable = context.FrontEndContext.StringTable;

            // Root must have a characteristic as an absolute path.
            var result = Converter.ExpectPath(root, strict: false, context: new ConversionContext(pos: 1));

            // Non-root expressions must be path fragments
            for (int i = 0; i < pathFragments.Count; i++)
            {
                Converter.ExpectPathFragment(
                    strTable,
                    pathFragments[i],
                    out PathAtom pathAtom,
                    out RelativePath relativePath,
                    context: new ConversionContext(objectCtx: pathFragments, pos: i));
                if (pathAtom.IsValid)
                {
                    result = result.Combine(pathTable, pathAtom);
                }
                else if (relativePath.IsValid)
                {
                    result = result.Combine(pathTable, relativePath);
                }
            }

            return EvaluationResult.Create(result);
        }

        /// <summary>
        /// Signature for path interpolation
        /// </summary>
        private CallSignature InterpolateSignature => CreateSignature(
            required: RequiredParameters(
                UnionType(
                    AmbientTypes.PathType,
                    AmbientTypes.FileType,
                    AmbientTypes.DerivedFileType,
                    AmbientTypes.DirectoryType,
                    AmbientTypes.StaticDirectoryType)),
            restParameterType: UnionType(AmbientTypes.StringType, AmbientTypes.PathAtomType, AmbientTypes.RelativePathType),
            returnType: AmbientTypes.PathType);
    }
}
