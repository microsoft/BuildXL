// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using Xunit;
using Xunit.Sdk;

namespace BuildXL.FrontEnd.Script.Testing.Helper.Ambients
{
    /// <summary>
    /// Ambient implementation for asserts of unit testing
    /// </summary>
    public sealed class AmbientAssert : AmbientDefinitionBase
    {
        /// <nodoc />
        public AmbientAssert(PrimitiveTypes knownTypes)
            : base("Assert", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                AmbientHack.GetName("Assert"),
                new[]
                {
                    Function("fail", Fail, FailSignature),

                    Function("isTrue", IsTrue, IsTrueSignature),
                    Function("isFalse", IsFalse, IsFalseSignature),

                    Function("areEqual", AreEqual, AreEqualSignature),
                    Function("notEqual", NotEqual, NotEqualSignature),
                });
        }

        private static EvaluationResult Fail(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var message = Args.AsString(args, 0);

            Assert.True(false, message);

            return EvaluationResult.Undefined;
        }

        private static EvaluationResult IsTrue(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var condition = Args.AsBool(args, 0);
            var message = Args.AsStringOptional(args, 1);

            Assert.True(condition, message);

            return EvaluationResult.Undefined;
        }

        private static EvaluationResult IsFalse(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var condition = Args.AsBool(args, 0);
            var message = Args.AsStringOptional(args, 1);

            Assert.False(condition, message);

            return EvaluationResult.Undefined;
        }

        private static EvaluationResult AreEqual(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return NotEqualOrEqual(context, true, env, args);
        }

        private static EvaluationResult NotEqual(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return NotEqualOrEqual(context, false, env, args);
        }

        private static EvaluationResult NotEqualOrEqual(Context context, bool expectedEqual, ModuleLiteral env, EvaluationStackFrame args)
        {
            var expected = args[0];
            Args.CheckArgumentIndex(args, 0);
            var actual = args[1];
            Args.CheckArgumentIndex(args, 1);

            if (expectedEqual != expected.Equals(actual))
            {
                var expectedString = ToStringConverter.ObjectToString(context, expected);
                var actualString = ToStringConverter.ObjectToString(context, actual);

                if (expectedEqual)
                {
                    throw new EqualException(expectedString, actualString);
                }
                else
                {
                    throw new NotEqualException(expectedString, actualString);
                }
            }

            return EvaluationResult.Undefined;
        }

        private CallSignature FailSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType, AmbientTypes.StringType),
            returnType: PrimitiveType.VoidType);

        private CallSignature IsTrueSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.BooleanType),
            optional: OptionalParameters(AmbientTypes.StringType),
            returnType: PrimitiveType.VoidType);

        private CallSignature IsFalseSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.BooleanType),
            optional: OptionalParameters(AmbientTypes.StringType),
            returnType: PrimitiveType.VoidType);

        private CallSignature AreEqualSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.AnyType, PrimitiveType.AnyType),
            optional: OptionalParameters(AmbientTypes.StringType),
            returnType: PrimitiveType.VoidType);

        private CallSignature NotEqualSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.AnyType, PrimitiveType.AnyType),
            optional: OptionalParameters(AmbientTypes.StringType),
            returnType: PrimitiveType.VoidType);
    }
}
