// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk;
using Xunit;
using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Builder class for constructing inputs for evaluation.
    /// </summary>
    public sealed class SpecEvaluationBuilder
    {
        private readonly DsTest m_test;

        public const string DefaultSpecName = "build.dsc";
        public const string PreludeDir = "Sdk.Prelude";
        public const string PreludePackageConfigFileName = "package.config.dsc";
        public readonly string PreludePackageConfigRelativePath = PreludeDir + Path.DirectorySeparatorChar + PreludePackageConfigFileName;
        public const string PreludePackageConfigRelativePathDsc = PreludeDir + "/" + PreludePackageConfigFileName;
        public static string PreludePackageMainSpecRelativePath = PreludeDir + Path.DirectorySeparatorChar + "prelude.dsc";

        public const string SdkTransformers = "Sdk.Transformers";
        public readonly string SdkTransformersPackageConfigRelativePath = SdkTransformers + Path.DirectorySeparatorChar + PreludePackageConfigFileName;
        public const string SdkTransformersPackageConfigRelativePathDsc = SdkTransformers + "/" + PreludePackageConfigFileName;
        public static string SdkTransformersPackageMainSpecRelativePath = SdkTransformers + Path.DirectorySeparatorChar + "package.dsc";

        private readonly List<BuildSpec> m_specs = new List<BuildSpec>();

        private bool m_preludeAdded;
        private bool m_sdkTransformersAdded;

        private string m_specFileName;
        private string m_qualifier;
        private bool m_isDebugged;
        private bool m_useSerializedAst;
        private bool m_parseOnly;

        /// <summary>
        /// Constructor that captures <see cref="DsTest"/>.
        /// </summary>
        public SpecEvaluationBuilder(DsTest test)
        {
            Contract.Requires(test != null);
            m_test = test;
        }

        /// <summary>
        /// Adds spec content with default spec name.
        /// </summary>
        public SpecEvaluationBuilder Spec(string specContent)
        {
            return AddSpec(DefaultSpecName, specContent);
        }

        /// <summary>
        /// Adds content of the config.dsc
        /// </summary>
        public SpecEvaluationBuilder LegacyConfiguration(string specContent)
        {
            return AddSpec(Names.ConfigDsc, specContent);
        }

        /// <summary>
        /// Adds content of the config.dc
        /// </summary>
        public SpecEvaluationBuilder Configuration(string specContent)
        {
            return AddSpec(Names.ConfigBc, specContent);
        }

        /// <summary>
        /// Adds content of the config.dsc
        /// </summary>
        public SpecEvaluationBuilder EmptyLegacyConfiguration()
        {
            return LegacyConfiguration(@"config({});");
        }

        /// <summary>
        /// Adds content of the config.bc
        /// </summary>
        public SpecEvaluationBuilder EmptyConfiguration()
        {
            return Configuration(@"config({});");
        }

        /// <summary>
        /// Sets a root spec that would be evaluated. Relevant when more then one spec is presented.
        /// </summary>
        public SpecEvaluationBuilder RootSpec(string fileName)
        {
            m_specFileName = fileName.Replace('\\', '/');
            return this;
        }

        /// <summary>
        /// Adds spec with file name and content for parsing/evaluation.
        /// </summary>
        public SpecEvaluationBuilder AddSpec(string specFileName, string specContent)
        {
            m_specs.Add(BuildSpec.Create(specFileName.Replace('\\', '/'), specContent));
            return this;
        }

        /// <summary>
        /// Adds spec with default name and given content for parsing/evaluation.
        /// </summary>
        public SpecEvaluationBuilder AddSpec(string specContent)
        {
            return AddSpec(GetSpecFileName(), specContent);
        }

        public SpecEvaluationBuilder AddFile(string fileName, string content)
        {
            m_specs.Add(BuildSpec.Create(fileName.Replace('\\', '/'), content));
            return this;
        }

        /// <summary>
        /// Sets the test root directory
        /// </summary>
        /// <remarks>
        /// The test root directory is a temporary directory otherwise
        /// </remarks>
        public SpecEvaluationBuilder TestRootDirectory(string testRoot)
        {
            m_test.TestRoot = testRoot;
            return this;
        }

        public static readonly string FullPreludeContent = string.Join(Environment.NewLine,
            File.ReadAllText("Libs/lib.core.d.ts"),
            File.ReadAllText("Libs/Prelude.AmbientHacks.ts"),
            File.ReadAllText("Libs/Prelude.Assert.ts"),
            File.ReadAllText("Libs/Prelude.Context.ts"),
            File.ReadAllText("Libs/Prelude.Contract.ts"),
            File.ReadAllText("Libs/Prelude.Collections.ts"),
            File.ReadAllText("Libs/Prelude.Configuration.ts"),
            File.ReadAllText("Libs/Prelude.Configuration.Resolvers.ts"),
            File.ReadAllText("Libs/Prelude.Debug.ts"),
            File.ReadAllText("Libs/Prelude.Math.ts"),
            File.ReadAllText("Libs/Prelude.Environment.ts"),
            File.ReadAllText("Libs/Prelude.IO.ts"),
            File.ReadAllText("Libs/Prelude.Unsafe.ts"),
            File.ReadAllText("Libs/Prelude.Transformer.Arguments.ts"));

        public static readonly string SdkTransformersContent = string.Join(Environment.NewLine,
            File.ReadAllText("Libs/Sdk.Transformers.ts"));

        public SpecEvaluationBuilder AddFullPrelude()
        {
            AddPrelude()
            .AddSdkTransformers();
            return this;
        }

        public SpecEvaluationBuilder AddPrelude()
        {
            if (m_preludeAdded)
            {
                return this;
            }

            m_preludeAdded = true;
            AddSpec(PreludePackageMainSpecRelativePath, FullPreludeContent);
            AddSpec(PreludePackageConfigRelativePath, V1Module(FrontEndHost.PreludeModuleName, "prelude.dsc"));

            return this;
        }
        public SpecEvaluationBuilder AddSdkTransformers()
        {
            if (m_sdkTransformersAdded)
            {
                return this;
            }

            m_sdkTransformersAdded = true;

            AddSpec(SdkTransformersPackageMainSpecRelativePath, SdkTransformersContent);
            AddSpec(SdkTransformersPackageConfigRelativePath, V2Module("Sdk.Transformers"));
            return this;
        }

        public SpecEvaluationBuilder AddExtraPreludeSpec(string content)
        {
            AddSpec(PreludeDir + "//" + GetSpecFileName(), content);
            return this;
        }

        /// <summary>
        /// Builds the workspace.
        /// </summary>
        public Workspace BuildWorkspace()
        {
            return m_test.BuildWorkspace(m_specs);
        }

        /// <summary>
        /// Parse current spec file with expected failure.
        /// </summary>
        public Diagnostic ParseWithFirstError()
        {
            var dsResult = m_test.ParseSpecs(GetSpecFileName(), m_specs);
            Assert.True(dsResult.Errors.Count != 0, "Expecting parse errors but got none!");

            return dsResult.Errors.First();
        }

        /// <summary>
        /// Parse current spec file with expected failures.
        /// </summary>
        public IReadOnlyList<Diagnostic> ParseWithDiagnostics()
        {
            var dsResult = m_test.ParseSpecs(GetSpecFileName(), m_specs);
            return dsResult.Diagnostics;
        }

        /// <summary>
        /// Evaluates expression and expects a given diagnostic id.
        /// </summary>
        public Diagnostic ParseWithDiagnosticId<T>(T diagnosticId) where T : struct
        {
            var diagnostics = ParseWithDiagnostics();

            return Utilities.GetDiagnosticWithId(diagnostics, diagnosticId);
        }

        /// <summary>
        /// Evalautes everything and expects a given typechecker diagnostic id;
        /// </summary>
        public Diagnostic[] EvaluateWithCheckerDiagnostic(TypeScript.Net.Diagnostics.IDiagnosticMessage expected, params string[] args)
        {
            var result = Evaluate();
            return result.ExpectCheckerDiagnostic(expected, args);
        }

        /// <summary>
        /// Parse current spec file with expected success.
        /// </summary>
        public EvaluatedValues ParseWithNoErrors()
        {
            var dsResult = m_test.ParseSpecs(GetSpecFileName(), m_specs);
            return dsResult.NoErrors();
        }

        /// <summary>
        /// Evaluates file name.
        /// Expects successful result.
        /// </summary>
        public object EvaluateWithNoErrors(string filename)
        {
            var testResult = RootSpec(filename).Evaluate();
            return testResult.NoErrors();
        }

        /// <summary>
        /// Evaluates default file name.
        /// Expects successful result.
        /// </summary>
        public object EvaluateWithNoErrors()
        {
            var testResult = Evaluate();
            return testResult.NoErrors();
        }

        /// <summary>
        /// Persists configured specs to disk and return a configuration object that can be passed to the BuildXL engine to run
        /// </summary>
        public ICommandLineConfiguration PersistSpecsAndGetConfiguration(bool cleanExistingDirectory = true, bool enableSpecCache = false)
        {
            return m_test.WriteSpecsAndGetConfiguration(GetSpecFileName(), m_specs, isDebugged: false, cleanExistingDirectory: cleanExistingDirectory, enableSpecCache: enableSpecCache);
        }

        /// <summary>
        /// Evaluates a number of expressions and returns underlying <see cref="TestResult"/>.
        /// </summary>
        public TestResult Evaluate(params string[] expressions)
        {
            return m_test.EvaluateSpecs(
                GetSpecFileName(),
                m_specs,
                expressions,
                m_qualifier,
                isDebugged: m_isDebugged,
                useSerializedAst: m_useSerializedAst,
                parseOnly: m_parseOnly);
        }

        /// <summary>
        /// Evaluates one expression for default file name.
        /// Expects successful result.
        /// </summary>
        public object EvaluateExpressionWithNoErrors(string expression)
        {
            var testResult = Evaluate(expression);
            var result = testResult.NoErrors(expression);
            return result[expression];
        }

        /// <summary>
        /// Evaluates one expression for default file name.
        /// Expects successful result.
        /// </summary>
        public T EvaluateExpressionWithNoErrors<T>(string expression)
        {
            return (T)EvaluateExpressionWithNoErrors(expression);
        }

        /// <summary>
        /// Evaluates one expression for specified file name.
        /// Expects successful result.
        /// </summary>
        public object EvaluateExpressionWithNoErrors(string fileName, string expression)
        {
            var testResult = RootSpec(fileName).Evaluate(expression);
            var result = testResult.NoErrors(expression);
            return result[expression];
        }

        /// <summary>
        /// Evaluates expressions for default file name.
        /// Expects successful result.
        /// </summary>
        public EvaluatedValues EvaluateExpressionsWithNoErrors(string expression1, string expression2)
        {
            var expressions = new[] { expression1, expression2 };
            var testResult = Evaluate(expressions);
            return testResult.NoErrors(expressions);
        }

        /// <summary>
        /// Evaluates expressions for default file name.
        /// Expects successful result.
        /// </summary>
        public EvaluatedValues EvaluateExpressionsWithNoErrors(string expression1, string expression2, string expression3)
        {
            var expressions = new[] { expression1, expression2, expression3 };
            var testResult = Evaluate(expressions);
            return testResult.NoErrors(expressions);
        }

        /// <summary>
        /// Evaluates expressions for default file name.
        /// Expects successful result.
        /// </summary>
        public EvaluatedValues EvaluateExpressionsWithNoErrors(params string[] expressions)
        {
            var testResult = Evaluate(expressions);
            return testResult.NoErrors(expressions);
        }

        /// <summary>
        /// Evaluates expression and expects at least one evaluation failure.
        /// </summary>
        public Diagnostic EvaluateWithFirstError(string expression = null)
        {
            var expressions = expression == null ? new string[0] : new[] { expression };
            var testResult = Evaluate(expressions);

            Assert.True(testResult.Errors.Count > 0, "Zero errors found!");
            return testResult.Errors.First();
        }

        /// <summary>
        /// Evaluates expression and expects at least one evaluation failure.
        /// </summary>
        public IReadOnlyList<Diagnostic> EvaluateWithDiagnostics(string expression = null)
        {
            var expressions = expression == null ? new string[0] : new[] { expression };
            var testResult = Evaluate(expressions);
            return testResult.Diagnostics;
        }

        /// <summary>
        /// Evaluates expression and expects a given diagnostic id.
        /// </summary>
        public Diagnostic EvaluateWithDiagnosticId<T>(T diagnosticId, string expression = null) where T : struct
        {
            var diagnostics = EvaluateWithDiagnostics(expression);

            return Utilities.GetDiagnosticWithId(diagnostics, diagnosticId);
        }

        private string GetSpecFileName()
        {
            // If spec was not specified explicitly via RootSpec method and there is only one spec, then pick it
            return m_specFileName == null && m_specs.Count == 1
                ? m_specs[0].FileName
                : m_specFileName ?? DefaultSpecName;
        }

        public SpecEvaluationBuilder Qualifier(string qualifier)
        {
            m_qualifier = qualifier;
            return this;
        }

        public SpecEvaluationBuilder IsDebugged(bool isDebugged = true)
        {
            m_isDebugged = isDebugged;
            return this;
        }

        public SpecEvaluationBuilder UseSerializedAst(bool useSerializedAst = true)
        {
            m_useSerializedAst = useSerializedAst;
            return this;
        }

        public SpecEvaluationBuilder ParseOnly(bool parseOnly = true)
        {
            m_parseOnly = parseOnly;
            return this;
        }
    }
}
