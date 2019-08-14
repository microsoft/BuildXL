// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using Xunit;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Testing.Helper.Ambients
{
    /// <nodoc />
    public sealed class AmbientTesting : AmbientDefinitionBase
    {
        private TestEngineAbstraction m_engineAbstraction;

        private Func<IEnumerable<Diagnostic>> m_getDiagnostics;

        /// <summary>
        /// Prevents checking the created pips
        /// </summary>
        public bool DontValidatePipsEnabled { get; private set; }


        private SymbolAtom m_testingSetMountPointName;
        private SymbolAtom m_testingSetMountPointPath;
        private SymbolAtom m_testingSetMountPointTrackSourceFileChanges;
        private SymbolAtom m_testingSetMountPointIsWritable;
        private SymbolAtom m_testingSetMountPointIsReadable;
        private SymbolAtom m_testingSetMountPointIsSystem;
        private SymbolAtom m_testingSetMountPointIsScrubbable;
        private SymbolAtom m_testingExpectedMessageCode;
        private SymbolAtom m_testingExpectedMessageContent;
        private SymbolAtom m_testingExpectedMessageLevel;
        
        /// <nodoc />
        public AmbientTesting(TestEngineAbstraction engineAbstraction, Func<IEnumerable<Diagnostic>> getDiagnostics, PrimitiveTypes knownTypes)
            : base("Testing", knownTypes)
        {
            Contract.Requires(engineAbstraction != null);

            m_engineAbstraction = engineAbstraction;
            m_getDiagnostics = getDiagnostics;

            m_testingSetMountPointName = Symbol("name");
            m_testingSetMountPointPath = Symbol("path");
            m_testingSetMountPointTrackSourceFileChanges = Symbol("trackSourceFileChanges");
            m_testingSetMountPointIsWritable = Symbol("isWritable");
            m_testingSetMountPointIsReadable = Symbol("isReadable");
            m_testingSetMountPointIsSystem = Symbol("isSystem");
            m_testingSetMountPointIsScrubbable = Symbol("isScrubbable");
            m_testingExpectedMessageCode = Symbol("code");
            m_testingExpectedMessageContent = Symbol("content");
            m_testingExpectedMessageLevel = Symbol("level");
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                AmbientHack.GetName("Testing"),
                new[]
                {
                    Function("setBuildParameter", SetBuildParameter, SetBuildParameterSignature),
                    Function("removeBuildParameter", RemoveBuildParameter, RemoveBuildParameterSignature),

                    Function("setMountPoint", SetMountPoint, SetMountPointSignature),
                    Function("removeMountPoint", RemoveMountPoint, RemoveMountPointSignature),

                    Function("expectFailure", ExpectFailure, ExpectFailureSignature),

                    Function("dontValidatePips", DontValidatePips, DontValidatePipsSignature),
                });
        }

        private EvaluationResult SetBuildParameter(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var name = Args.AsString(args, 0);
            var value = Args.AsString(args, 1);

            m_engineAbstraction.SetBuildParameter(name, value);

            return EvaluationResult.Undefined;
        }

        private EvaluationResult RemoveBuildParameter(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var name = Args.AsString(args, 0);

            m_engineAbstraction.RemoveBuildParameter(name);

            return EvaluationResult.Undefined;
        }

        private EvaluationResult SetMountPoint(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var obj = Args.AsObjectLiteral(args, 0);

            var name = Converter.ExtractPathAtom(obj, m_testingSetMountPointName);
            var path = Converter.ExtractPathLike(obj, m_testingSetMountPointPath);
            var trackSourceFileChanges = Converter.ExtractOptionalBoolean(obj, m_testingSetMountPointTrackSourceFileChanges) ?? false;
            var isWritable = Converter.ExtractOptionalBoolean(obj, m_testingSetMountPointIsWritable) ?? false;
            var isReadable = Converter.ExtractOptionalBoolean(obj, m_testingSetMountPointIsReadable) ?? false;
            var isSystem = Converter.ExtractOptionalBoolean(obj, m_testingSetMountPointIsSystem) ?? false;
            var isScrubbable = Converter.ExtractOptionalBoolean(obj, m_testingSetMountPointIsScrubbable) ?? false;

            var mount = new Mount
                        {
                            Name = name,
                            Path = path,
                            TrackSourceFileChanges = trackSourceFileChanges,
                            IsWritable = isWritable,
                            IsReadable = isReadable,
                            IsSystem = isSystem,
                            IsScrubbable = isScrubbable,
                        };

            m_engineAbstraction.SetMountPoint(mount);

            return EvaluationResult.Undefined;
        }

        private EvaluationResult RemoveMountPoint(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var name = Args.AsString(args, 0);

            m_engineAbstraction.RemoveMountPoint(name);

            return EvaluationResult.Undefined;
        }

        private EvaluationResult ExpectFailure(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var closure = Args.AsClosure(args, 0);
            var expectedResults = Args.AsArrayLiteral(args, 1);

            using (var frame = EvaluationStackFrame.Create(closure.Function, args.Frame))
            {
                var result = context.InvokeClosure(closure, frame);
                if (!result.IsErrorValue)
                {
                    Assert.True(false, "Expected the code under test to throw a failure, but none was returned.");
                }

                if (!m_getDiagnostics().Any(diagnostic => diagnostic.Level == EventLevel.Error))
                {
                    Assert.True(false, "Expected to see at least one reported error, but none encountered.");
                }

                for (int i = 0; i < expectedResults.Count; i++)
                {
                    if (expectedResults[i].Value is string expectedContent)
                    {
                        // String case
                        Assert.False(string.IsNullOrEmpty(expectedContent), "Empty strings are not supported as expected error messages");
                        ValidateExpectedMessageLogged(null, expectedContent);
                    }
                    else
                    {
                        // Object case
                        var obj = Converter.ExpectObjectLiteral(
                            expectedResults[i],
                            new ConversionContext(pos: i, objectCtx: expectedResults));

                        var code = Converter.ExtractInt(obj, m_testingExpectedMessageCode);
                        expectedContent = Converter.ExtractString(obj, m_testingExpectedMessageContent);

                        ValidateExpectedMessageLogged(code, expectedContent);
                    }
                }

                return EvaluationResult.Undefined;
            }
        }

        private void ValidateExpectedMessageLogged(int? code, string expectedContent)
        {
            bool found = false;
            Diagnostic? foundWithCode = null;
            Diagnostic? foundWithContent = null;
            foreach (var diagnostic in m_getDiagnostics())
            {
                bool matchCode = !code.HasValue || diagnostic.ErrorCode == code;
                bool matchContent = diagnostic.Message.IndexOf(expectedContent, StringComparison.Ordinal) >= 0;
                if (matchContent && matchCode)
                {
                    found = true;
                    break;
                }

                if (code.HasValue)
                {
                    // if we have a code, we need helpers
                    if (matchCode)
                    {
                        foundWithCode = diagnostic;
                    }

                    if (matchContent)
                    {
                        foundWithContent = diagnostic;
                    }
                }
            }

            if (!found)
            {
                var builder = new StringBuilder();
                builder.Append(C($"Did not find expected message with content: '{expectedContent}'"));
                if (code.HasValue)
                {
                    builder.Append(C($"and code '{code.Value}'"));
                }
                else
                {
                    builder.Append(".");
                }

                if (foundWithCode != null)
                {
                    builder.Append(C($" Yet found a message matching the code without the content: {foundWithCode.Value.FullMessage}."));
                }
                else if (foundWithContent != null)
                {
                    builder.Append(C($" Yet found a message matching the content with no matching code: {foundWithContent.Value.FullMessage}."));
                }

                builder.AppendLine(" Messages encountered:");

                foreach (var diagnostic in m_getDiagnostics())
                {
                    builder.AppendLine("\t" + diagnostic.FullMessage);
                }

                Assert.True(false, builder.ToString());
            }
        }

        private EvaluationResult DontValidatePips(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            DontValidatePipsEnabled = true;

            return EvaluationResult.Undefined;
        }

        private CallSignature SetBuildParameterSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType, AmbientTypes.StringType),
            returnType: PrimitiveType.VoidType);

        private CallSignature RemoveBuildParameterSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: PrimitiveType.VoidType);

        private CallSignature SetMountPointSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.ObjectType),
            returnType: PrimitiveType.VoidType);

        private CallSignature RemoveMountPointSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: PrimitiveType.VoidType);

        private CallSignature ExpectFailureSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.ClosureType, new ArrayType(AmbientTypes.StringType)),
            returnType: PrimitiveType.VoidType);

        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        private CallSignature DontValidatePipsSignature => CreateSignature(
            required: RequiredParameters(),
            returnType: PrimitiveType.VoidType);
}
}
