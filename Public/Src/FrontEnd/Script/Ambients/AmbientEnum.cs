// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for all enums.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public sealed class AmbientEnum : AmbientDefinition<EnumValue>
    {
        /// <nodoc />
        public AmbientEnum(PrimitiveTypes knownTypes)
            : base("Enum", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<EnumValue>> CreateMembers()
        {
            return new Dictionary<StringId, CallableMember<EnumValue>>
            {
                // TODO: prelude doesn't have this member.
                { NameId("hasFlag"), Create<EnumValue>(AmbientName, Symbol("hasFlag"), HasFlag) },
                { NameId("valueOf"), Create<EnumValue>(AmbientName, Symbol("valueOf"), ValueOf) },
            };
        }

        /// <inheritdoc />
        protected override EvaluationResult ToStringMethod(Context context, EnumValue receiver, EvaluationStackFrame captures)
        {
            // Technically, this is the violation from the typescript behavior.
            // In TypeScript enumValue.toString() returns the number, but DScript returns a string representation of the enum
            SymbolAtom name = receiver.Name;
            return EvaluationResult.Create(
                name.IsValid
                    ? name.ToString(context.FrontEndContext.StringTable)
                    : receiver.Value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// This method will be deprecated soon.
        /// </summary>
        private static EvaluationResult HasFlag(Context context, EnumValue receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var enumValueArg = Converter.ExpectEnumValue(arg);

            return EvaluationResult.Create((enumValueArg.Value & receiver.Value) == enumValueArg.Value);
        }

        private static EvaluationResult ValueOf(Context context, EnumValue receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Value);
        }
    }
}
