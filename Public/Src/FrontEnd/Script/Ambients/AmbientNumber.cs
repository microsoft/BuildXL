// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for type Number.
    /// </summary>
    public sealed class AmbientNumber : AmbientDefinition<int>
    {
        private static readonly NumberFormatInfo s_numberFormatInfo =
            NumberFormatInfo.ReadOnly(NumberFormatInfo.GetInstance(CultureInfo.InvariantCulture));

        /// <nodoc />
        public AmbientNumber(PrimitiveTypes knownTypes)
            : base("Number", knownTypes)
        {
        }
        
        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<int>> CreateMembers()
        {
            // Number does not have any new functions.
            return new Dictionary<StringId, CallableMember<int>>();
        }

        /// <inheritdoc />
        protected override CallableMember<int> CreateToStringMember()
        {
            // ToString method is special for 'Number' -- it takes an optional 'radix'
            return Create(
                AmbientName,
                Symbol(Constants.Names.ToStringFunction),
(CallableMemberSignature1<int>)NumberToStringMethod,
                rest: false,
                minArity: 0);
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "Number",
                new[]
                {
                    Function("parseInt", ParseInt, ParseIntSignature),
                });
        }

        private static EvaluationResult NumberToStringMethod(Context context, int receiver, EvaluationResult radixAsObject, EvaluationStackFrame captures)
        {
            int? radix = radixAsObject.IsUndefined ? (int?)null : (int) radixAsObject.Value;
            string result;
            if (radix != null)
            {
                ValidateRadix(radix.Value);
                result = Convert.ToString(receiver, radix.Value);
            }
            else
            {
                result = receiver.ToString(CultureInfo.InvariantCulture);
            }
            
            return EvaluationResult.Create(result);
        }

        private static void ValidateRadix(int radix)
        {
            // This is the only valid set of radices.
            if (radix != 2 && radix != 8 && radix != 10 && radix != 16)
            {
                throw new InvalidRadixException(radix);
            }
        }

        private static EvaluationResult ParseInt(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var str = Args.AsString(args, 0);
            var radix = Args.AsIntOptional(args, 1);
            if (radix != null)
            {
                ValidateRadix(radix.Value);
                return EvaluationResult.Create(Convert.ToInt32(str, radix.Value));
            }

            if (int.TryParse(str, NumberStyles.Any, s_numberFormatInfo, out int intValue))
            {
                return EvaluationResult.Create(intValue);
            }

            return EvaluationResult.Undefined;
        }

        private CallSignature ParseIntSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            optional: OptionalParameters(AmbientTypes.NumberType),
            returnType: AmbientTypes.NumberType);
    }
}
