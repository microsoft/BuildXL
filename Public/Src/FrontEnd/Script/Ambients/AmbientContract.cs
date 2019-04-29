// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for Contract namespace.
    /// </summary>
    public sealed class AmbientContract : AmbientDefinitionBase
    {
        internal const string ContractName = "Contract";
        internal const string PreconditionFunctionName = "precondition";
        internal const string RequiresFunctionName = "requires";
        internal const string AssertFunctionName = "assert";
        internal const string FailFunctionName = "fail";
        internal const string WarnFunctionName = "warn";

        /// <nodoc />
        public AmbientContract(PrimitiveTypes knownTypes)
            : base(ContractName, knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                ContractName,
                new[]
                {
                    Function(PreconditionFunctionName, Requires, RequiresSignature),
                    Function(RequiresFunctionName, Requires, RequiresSignature),
                    Function(AssertFunctionName, Assert, AssertSignature),
                    Function(FailFunctionName, Fail, FailSignature),
                    Function(WarnFunctionName, Warn, WarnSignature),
                });
        }

        private static EvaluationResult Requires(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            bool predicate = Args.AsBool(args, 0);
            string message = Args.AsStringOptional(args, 1) ?? string.Empty;

            if (!predicate)
            {
                throw new ContractRequireException(message);
            }

            return EvaluationResult.Undefined;
        }

        private static EvaluationResult Assert(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            bool predicate = Args.AsBool(args, 0);
            string message = Args.AsStringOptional(args, 1) ?? string.Empty;

            if (!predicate)
            {
                throw new ContractAssertException(message);
            }

            return EvaluationResult.Undefined;
        }

        private static EvaluationResult Fail(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            string message = Args.AsString(args, 0);

            throw new ContractFailException(message);
        }

        private static EvaluationResult Warn(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var message = Convert.ToString(args[0].Value, CultureInfo.InvariantCulture);
            var location = context.TopStack.InvocationLocation.AsLoggingLocation(env, context);

            context.Logger.ReportContractWarn(context.FrontEndContext.LoggingContext, location, message);

            return EvaluationResult.Undefined;
        }

        private static CallSignature RequiresSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.BooleanType),
            optional: OptionalParameters(PrimitiveType.StringType),
            returnType: PrimitiveType.VoidType);

        private static CallSignature AssertSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.BooleanType),
            optional: OptionalParameters(PrimitiveType.StringType),
            returnType: PrimitiveType.VoidType);

        private static CallSignature FailSignature => CreateSignature(
           required: RequiredParameters(PrimitiveType.StringType),
           returnType: PrimitiveType.VoidType);

        private static CallSignature WarnSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.StringType),
            returnType: PrimitiveType.VoidType);
    }
}
