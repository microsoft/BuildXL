// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Core;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for type Unit.
    /// </summary>
    public sealed class AmbientUnit : AmbientDefinition<UnitValue>
    {
        internal const string UnitName = "Unit";

        /// <nodoc />
        public AmbientUnit(PrimitiveTypes knownTypes)
            : base(UnitName, knownTypes)
        {
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<UnitValue>> CreateMembers()
        {
            // Unit does not have any member functions.
            return new Dictionary<StringId, CallableMember<UnitValue>>();
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                UnitName,
                new[]
                {
                    Function("unit", Get, GetSignature),
                });
        }

        private CallSignature GetSignature => CreateSignature(
            returnType: AmbientTypes.UnitType);

        private static EvaluationResult Get(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(UnitValue.Unit);
        }
    }
}
