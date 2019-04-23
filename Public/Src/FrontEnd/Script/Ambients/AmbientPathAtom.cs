// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for <code>namespace PathAtom {}</code> and <code>interface PathAtom {}</code>..
    /// </summary>
    public sealed class AmbientPathAtom : AmbientDefinition<PathAtom>
    {
        /// <nodoc />
        public AmbientPathAtom(PrimitiveTypes knownTypes)
            : base("PathAtom", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "PathAtom",
                new[]
                {
                    Function("create", Create, CreatePathAtomSignature),
                    Function("interpolate", Interpolate, InterpolateSignature),
                });
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<PathAtom>> CreateMembers()
        {
            return new[]
            {
                // extension method/property
                CreateProperty<PathAtom>(AmbientName, Symbol("extension"), GetExtension),
                CreateProperty<PathAtom>(AmbientName, Symbol("hasExtension"), HasExtension),

                Create<PathAtom>(AmbientName, Symbol("concat"), Concat),
                Create<PathAtom>(AmbientName, Symbol("changeExtension"), ChangeExtension),
                Create<PathAtom>(AmbientName, Symbol("equals"), Equals, requiredNumberOfArguments: 1),
            }.ToDictionary(m => m.Name.StringId, m => m);
        }

        private static EvaluationResult Create(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var atom = Args.AsString(args, 0);

            var stringTable = context.FrontEndContext.StringTable;

            if (!PathAtom.TryCreate(stringTable, atom, out PathAtom result))
            {
                throw new InvalidPathAtomException(atom, new ErrorContext(pos: 1));
            }

            return EvaluationResult.Create(result);
        }

        private CallSignature CreatePathAtomSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.PathAtomType);

        private static EvaluationResult GetExtension(Context context, PathAtom receiver, EvaluationStackFrame captures)
        {
            var stringTable = context.FrontEndContext.StringTable;

            PathAtom result = receiver.GetExtension(stringTable);

            return result.IsValid ? EvaluationResult.Create(result) : EvaluationResult.Undefined;
        }

        private static EvaluationResult HasExtension(Context context, PathAtom receiver, EvaluationStackFrame captures)
        {
            var stringTable = context.FrontEndContext.StringTable;

            return EvaluationResult.Create(receiver.GetExtension(stringTable).IsValid);
        }

        private static EvaluationResult ChangeExtension(Context context, PathAtom receiver, EvaluationResult extension, EvaluationStackFrame captures)
        {
            var stringTable = context.FrontEndContext.StringTable;

            // An empty string is not a valid PathAtom, but in this case it indicates to remove the extension
            // this maps to passing an invalid path atom to PathAtom.ChangeExtension
            var atom = extension.Value as string == string.Empty
                ? PathAtom.Invalid
                : Converter.ExpectPathAtomFromStringOrPathAtom(stringTable, extension, new ConversionContext(pos: 1));

            return EvaluationResult.Create(atom.IsValid ? receiver.ChangeExtension(stringTable, atom) : receiver.RemoveExtension(stringTable));
        }

        private static EvaluationResult Concat(Context context, PathAtom receiver, EvaluationResult fragment, EvaluationStackFrame captures)
        {
            var stringTable = context.FrontEndContext.StringTable;

            PathAtom atom = Converter.ExpectPathAtomFromStringOrPathAtom(stringTable, fragment, new ConversionContext(pos: 1));

            return EvaluationResult.Create(receiver.Concat(stringTable, atom));
        }

        private static EvaluationResult Equals(Context context, PathAtom receiver, EvaluationResult fragment, EvaluationResult ignoreCase, EvaluationStackFrame captures)
        {
            var stringTable = context.FrontEndContext.StringTable;
            PathAtom otherAtom = Converter.ExpectPathAtomFromStringOrPathAtom(stringTable, fragment, new ConversionContext(pos: 1));

            if (!ignoreCase.IsUndefined)
            {
                bool ignoreCaseBool = Converter.ExpectBool(ignoreCase, new ConversionContext(pos: 2));
                if (ignoreCaseBool)
                {
                    return EvaluationResult.Create(receiver.CaseInsensitiveEquals(stringTable, otherAtom));
                }
            }

            // By default comparison is case sensitive
            return EvaluationResult.Create(receiver.Equals(otherAtom));
        }

        private CallSignature InterpolateSignature => CreateSignature(
            required: RequiredParameters(UnionType(AmbientTypes.StringType, AmbientTypes.PathAtomType)),
            restParameterType: UnionType(AmbientTypes.StringType, AmbientTypes.PathAtomType),
            returnType: AmbientTypes.PathAtomType);

        /// <summary>
        /// Implements relative path interpolation
        /// </summary>
        private static EvaluationResult Interpolate(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            Args.CheckArgumentIndex(args, 1);

            var stringTable = context.FrontEndContext.StringTable;

            PathAtom pathAtom = Converter.ExpectPathAtomFromStringOrPathAtom(stringTable, args[0], new ConversionContext(pos: 1));
            var rest = Args.AsArrayLiteral(args, 1);

            for (int i = 0; i < rest.Length; i++)
            {
                pathAtom = pathAtom.Concat(
                    stringTable,
                    Converter.ExpectPathAtomFromStringOrPathAtom(
                        stringTable,
                        rest[i],
                        new ConversionContext(pos: i + 1, objectCtx: rest)));
            }

            return EvaluationResult.Create(pathAtom);
        }
    }
}
