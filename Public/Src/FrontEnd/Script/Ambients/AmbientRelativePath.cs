// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for <code>namespace RelativePath {}</code> and <code>interface RelativePath {}</code>..
    /// </summary>
    public sealed class AmbientRelativePath : AmbientDefinition<RelativePath>
    {
        /// <nodoc />
        public AmbientRelativePath(PrimitiveTypes knownTypes)
            : base("RelativePath", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "RelativePath",
                new[]
                {
                    Function("create", Create, CreateRelativePathSignature),
                    Function("fromPathAtoms", FromPathAtoms, FromPathAtomsSignature),
                    Function("interpolate", Interpolate, InterpolateSignature),
                });
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<RelativePath>> CreateMembers()
        {
            return new[]
            {
                Create<RelativePath>(AmbientName, Symbol("changeExtension"), ChangeExtension),
                Create<RelativePath>(AmbientName, Symbol("combine"), Combine),
                Create<RelativePath>(AmbientName, Symbol("combinePaths"), CombinePaths, rest: true),
                Create<RelativePath>(AmbientName, Symbol("concat"), Concat),
                Create<RelativePath>(AmbientName, Symbol("equals"), Equals, requiredNumberOfArguments: 1),

                // extension method/property
                CreateProperty<RelativePath>(AmbientName, Symbol("extension"), GetExtension),
                CreateProperty<RelativePath>(AmbientName, Symbol("hasExtension"), HasExtension),

                // parent method/property
                CreateProperty<RelativePath>(AmbientName, Symbol("parent"), GetParent),
                CreateProperty<RelativePath>(AmbientName, Symbol("hasParent"), HasParent),

                // name method/property
                CreateProperty<RelativePath>(AmbientName, Symbol("name"), GetName),

                // nameWithoutExtension method/property
                CreateProperty<RelativePath>(AmbientName, Symbol("nameWithoutExtension"), GetNameWithoutExtension),

                // other methods
                Create<RelativePath>(AmbientName, Symbol("toPathAtoms"), ToPathAtoms),

                // TODO: This is dengerous, but needed to unblock Office conversion, at least temporarily.
                Create<RelativePath>(AmbientName, Symbol("toProperString"), ToProperString),
            }.ToDictionary(m => m.Name.StringId, m => m);
        }

        private static EvaluationResult ToProperString(Context context, RelativePath receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.ToString(context.StringTable));
        }

        private static EvaluationResult ToPathAtoms(Context context, RelativePath receiver, EvaluationStackFrame captures)
        {
            PathAtom[] atoms = receiver.GetAtoms();
            EvaluationResult[] result = new EvaluationResult[atoms.Length];

            for (int i = 0; i < atoms.Length; i++)
            {
                result[i] = EvaluationResult.Create(atoms[i]);
            }

            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult Create(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var fragment = Args.AsString(args, 0);

            var stringTable = context.FrontEndContext.StringTable;

            if (!RelativePath.TryCreate(stringTable, fragment, out RelativePath result))
            {
                throw new InvalidRelativePathException(fragment, new ErrorContext(pos: 1));
            }

            return EvaluationResult.Create(result);
        }

        private static EvaluationResult FromPathAtoms(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var arrayLit = Args.AsArrayLiteral(args, 0);
            var arrayOfAtoms = new PathAtom[arrayLit.Length];

            for (int i = 0; i < arrayLit.Length; ++i)
            {
                arrayOfAtoms[i] = Converter.ExpectPathAtomFromStringOrPathAtom(
                    context.FrontEndContext.StringTable,
                    arrayLit[i],
                    new ConversionContext(pos: i, objectCtx: arrayLit));
            }

            return EvaluationResult.Create(RelativePath.Create(arrayOfAtoms));
        }

        private CallSignature CreateRelativePathSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.RelativePathType);

        private CallSignature FromPathAtomsSignature => CreateSignature(
            restParameterType: AmbientTypes.PathAtomType,
            returnType: AmbientTypes.RelativePathType);

        private CallSignature InterpolateSignature => CreateSignature(
            required: RequiredParameters(UnionType(AmbientTypes.StringType, AmbientTypes.PathAtomType, AmbientTypes.RelativePathType)),
            restParameterType: UnionType(AmbientTypes.StringType, AmbientTypes.PathAtomType, AmbientTypes.RelativePathType),
            returnType: AmbientTypes.RelativePathType);

        private static EvaluationResult GetExtension(Context context, RelativePath receiver, EvaluationStackFrame captures)
        {
            if (receiver.IsEmpty)
            {
                return EvaluationResult.Undefined;
            }

            var stringTable = context.FrontEndContext.StringTable;
            PathAtom result = receiver.GetExtension(stringTable);

            return result.IsValid ? EvaluationResult.Create(result) : EvaluationResult.Undefined;
        }

        private static EvaluationResult HasExtension(Context context, RelativePath receiver, EvaluationStackFrame captures)
        {
            return receiver.IsEmpty ? EvaluationResult.Undefined : EvaluationResult.Create(receiver.GetExtension(context.FrontEndContext.StringTable).IsValid);
        }

        private static EvaluationResult ChangeExtension(Context context, RelativePath receiver, EvaluationResult extension, EvaluationStackFrame captures)
        {
            if (receiver.IsEmpty)
            {
                return EvaluationResult.Undefined;
            }

            var stringTable = context.FrontEndContext.StringTable;

            // An empty string is not a valid PathAtom, but in this case it indicates to remove the extension
            // this maps to passing an invalid path atom to PathAtom.ChangeExtension
            var atom = extension.Value as string == string.Empty
                ? PathAtom.Invalid
                : Converter.ExpectPathAtomFromStringOrPathAtom(stringTable, extension, new ConversionContext(pos: 1));

            return EvaluationResult.Create(atom.IsValid ? receiver.ChangeExtension(stringTable, atom) : receiver.RemoveExtension(stringTable));
        }

        private static EvaluationResult Combine(Context context, RelativePath receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var stringTable = context.FrontEndContext.StringTable;

            Converter.ExpectPathFragment(stringTable, arg, out PathAtom pathAtom, out RelativePath relativePath, new ConversionContext(pos: 1));

            return EvaluationResult.Create(relativePath.IsValid ? receiver.Combine(relativePath) : receiver.Combine(pathAtom));
        }

        private static EvaluationResult CombinePaths(Context context, RelativePath receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var stringTable = context.FrontEndContext.StringTable;
            var argArray = Converter.ExpectArrayLiteral(arg, new ConversionContext(pos: 1));

            RelativePath currentRelativePath = receiver;

            for (int i = 0; i < argArray.Length; i++)
            {
                Converter.ExpectPathFragment(
                    stringTable,
                    argArray[i],
                    out PathAtom pathAtom,
                    out RelativePath relativePath,
                    new ConversionContext(pos: i + 1, objectCtx: argArray));

                currentRelativePath = relativePath.IsValid ? currentRelativePath.Combine(relativePath) : currentRelativePath.Combine(pathAtom);
            }

            return EvaluationResult.Create(currentRelativePath);
        }

        private static EvaluationResult Concat(Context context, RelativePath receiver, EvaluationResult fragment, EvaluationStackFrame captures)
        {
            if (receiver.IsEmpty)
            {
                return EvaluationResult.Undefined;
            }

            PathAtom atom = Converter.ExpectPathAtomFromStringOrPathAtom(context.StringTable, fragment, new ConversionContext(pos: 1));

            return EvaluationResult.Create(receiver.Concat(context.StringTable, atom));
        }

        private static EvaluationResult HasParent(Context context, RelativePath receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(!receiver.IsEmpty);
        }

        private static EvaluationResult GetNameWithoutExtension(Context context, RelativePath receiver, EvaluationStackFrame captures)
        {
            return receiver.IsEmpty ? EvaluationResult.Undefined : EvaluationResult.Create(receiver.GetName().RemoveExtension(context.FrontEndContext.StringTable));
        }

        private static EvaluationResult GetName(Context context, RelativePath receiver, EvaluationStackFrame captures)
        {
            return receiver.IsEmpty ? EvaluationResult.Undefined : EvaluationResult.Create(receiver.GetName());
        }

        private static EvaluationResult GetParent(Context context, RelativePath receiver, EvaluationStackFrame captures)
        {
            return receiver.IsEmpty ? EvaluationResult.Undefined : EvaluationResult.Create(receiver.GetParent());
        }

        private static EvaluationResult Equals(Context context, RelativePath receiver, EvaluationResult fragment, EvaluationResult ignoreCase, EvaluationStackFrame captures)
        {
            var stringTable = context.FrontEndContext.StringTable;

            Converter.ExpectPathFragment(stringTable, fragment, out PathAtom pathAtom, out RelativePath relativePath, new ConversionContext(pos: 1));
            relativePath = pathAtom.IsValid ? RelativePath.Create(pathAtom) : relativePath;

            if (!ignoreCase.IsUndefined)
            {
                bool ignoreCaseBool = Converter.ExpectBool(ignoreCase, new ConversionContext(pos: 2));
                if (ignoreCaseBool)
                {
                    return EvaluationResult.Create(receiver.CaseInsensitiveEquals(stringTable, relativePath));
                }
            }

            return EvaluationResult.Create(receiver.Equals(relativePath));
        }

        /// <summary>
        /// Implements relative path interpolation
        /// </summary>
        internal static EvaluationResult Interpolate(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            Args.CheckArgumentIndex(args, 1);
            var rest = Args.AsArrayLiteral(args, 1);

            return Interpolate(context, args[0], rest.Values);
        }

        /// <summary>
        /// Implements relative path interpolation
        /// </summary>
        internal static EvaluationResult Interpolate(Context context, EvaluationResult root, IReadOnlyList<EvaluationResult> pathFragments)
        {
            var stringTable = context.FrontEndContext.StringTable;

            Converter.ExpectPathFragment(
                stringTable,
                root,
                out PathAtom pathAtom,
                out RelativePath relativePath,
                new ConversionContext(pos: 1));

            RelativePath currentRelativePath = relativePath.IsValid ? relativePath : RelativePath.Create(pathAtom);

            for (int i = 0; i < pathFragments.Count; i++)
            {
                Converter.ExpectPathFragment(
                    stringTable,
                    pathFragments[i],
                    out pathAtom,
                    out relativePath,
                    new ConversionContext(pos: i + 1, objectCtx: pathFragments));

                currentRelativePath = relativePath.IsValid ? currentRelativePath.Combine(relativePath) : currentRelativePath.Combine(pathAtom);
            }

            return EvaluationResult.Create(currentRelativePath);
        }
    }
}
