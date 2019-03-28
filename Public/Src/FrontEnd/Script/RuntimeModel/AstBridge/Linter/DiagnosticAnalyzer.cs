// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Custom linter (analyzer) that emits diagnostic information if the spec doesn't follow certain rules.
    /// </summary>
    public sealed class DiagnosticAnalyzer
    {
        private readonly bool m_useLegacyOfficeLogic;
        private readonly AnalysisContext m_context;

        // Set of policy rules that were requested across execution. This is used to avoid duplicating warnings
        // at rule initialization time, when the same set of policies is requested more than once.
        // TODO: the linter is instantiated many times with the same set of policy rules, which is the only parameter
        // that could change its behavior. Consider changing the factory method so it returns the same instance when the set of rules is the same
        // This implies making the linter thread-safe.
        private static readonly HashSet<HashSet<string>> s_requestedSetOfPolicies =
            new HashSet<HashSet<string>>(HashSet<string>.CreateSetComparer());

        private static readonly object s_thisLock = new object();

        private DiagnosticAnalyzer(Logger logger, LoggingContext loggingContext, HashSet<string> requestedPolicies, bool disableLanguagePolicies)
        {
            m_useLegacyOfficeLogic = false;
            m_context = new AnalysisContext();

            if (disableLanguagePolicies)
            {
                m_context.DisableLanguagePolicies();
            }

            InitializeDiagnosticRules(m_context);
            InitializePolicyRules(logger, loggingContext, m_context, requestedPolicies);
        }

        /// <nodoc />
        public static DiagnosticAnalyzer Create(Logger logger, LoggingContext loggingContext, HashSet<string> requestedPolicies, 
            bool disableLanguagePolicies)
        {
            return new DiagnosticAnalyzer(logger, loggingContext, requestedPolicies, disableLanguagePolicies);
        }

        /// <summary>
        /// Analyze regular build spec file.
        /// </summary>
        /// <remarks>
        /// A semantic model can be passed, if available. Observe the semantic model is never available
        /// when analyzing config or package config files, so this is the only method that exposes it.
        /// </remarks>
        public bool AnalyzeSpecFile(ISourceFile file, Logger logger, LoggingContext loggingContext, PathTable pathTable, Workspace workspace)
        {
            Contract.Requires(file != null);

            var scope = file.IsBuildListFile() ? RuleAnalysisScope.BuildListFile : RuleAnalysisScope.SpecFile;

            return AnalyzeSourceFile(file, scope, logger, loggingContext, pathTable, workspace);
        }

        /// <summary>
        /// Analyze root configuration file (a.k.a. config.dsc).
        /// </summary>
        public bool AnalyzeRootConfigurationFile(ISourceFile file, Logger logger, LoggingContext loggingContext, PathTable pathTable)
        {
            Contract.Requires(file != null);

            return AnalyzeSourceFile(file, RuleAnalysisScope.RootConfig, logger, loggingContext, pathTable);
        }

        /// <summary>
        /// Analyze package configuration file (a.k.a. package.config.dsc).
        /// </summary>
        public bool AnalyzePackageConfigurationFile(ISourceFile file, Logger logger, LoggingContext loggingContext, PathTable pathTable)
        {
            Contract.Requires(file != null);

            return AnalyzeSourceFile(file, RuleAnalysisScope.PackageConfig, logger, loggingContext, pathTable);
        }

        private bool AnalyzeSourceFile(ISourceFile file, RuleAnalysisScope analysisScope, Logger logger, LoggingContext loggingContext, PathTable pathTable, Workspace workspace = null)
        {
            var context = new DiagnosticContext(file, analysisScope, logger, loggingContext, pathTable, workspace);

            // If the file is a public facade one, we don't need to run any linter rule on it since its original version has been already validated
            if (file.IsPublicFacade)
            {
                return true;
            }

            VisitFile(file, context);

            return logger.HasErrors;
        }

        private void VisitFile(ISourceFile node, DiagnosticContext context)
        {
            // Parallel node visitation may only make sense when specs are huge. The only case we know is Office, where
            // some specs have >600K statements. For specs of regular size parallel node visitation adds a significant sync overhead
            // (e.g. for analog it adds ~20% overhead to ast conversion)
            // Since UseLegacyOfficeLogic is an Office-specific flag, we are using that to special-case Office specs until they
            // are onboarded to DScript V2.
            // TODO: This needs to go!!!!!

            if (m_useLegacyOfficeLogic)
            {
                // Parallel visitation can only work if the handlers registered during the creation of rules
                // in <see cref="InitializeDiagnosticRules"/> are thread-safe. They are currently thread-safe.
                ParallelVisitNode(node, context);
            }
            else
            {
                // Synchronous node visitation
                foreach (var nestedNode in NodeWalker.TraverseBreadthFirstAndSelf(node))
                {
                    // Only non-injected nodes are checked by the linter.
                    if (nestedNode.IsInjectedForDScript())
                    {
                        continue;
                    }

                    Handle(nestedNode, context);
                }
            }
        }

        private void Handle(INode node, DiagnosticContext context)
        {
            var handlers = m_context.GetHandlers(context.AnalysisScope, node.Kind);
            foreach (var handler in handlers)
            {
                handler(node, context);
            }
        }

        private void ParallelVisitNode(INode node, DiagnosticContext context)
        {
            ParallelAlgorithms.WhileNotEmpty(new[] { node }, (item, adder) =>
            {
                // Only non-injected nodes are checked by the linter.
                if (item.IsInjectedForDScript())
                {
                    return;
                }

                Handle(item, context);
                using (var list = NodeWalker.GetChildrenFast(item))
                {
                    foreach (var child in list.Instance)
                    {
                        foreach (var e in child)
                        {
                            adder(e);
                        }
                    }
                }
            });
        }

        private static void InitializeDiagnosticRules(AnalysisContext context)
        {
            EnforceConstOnEnumRule.CreateAndRegister(context);
            EnforceIdentifierExtendsInterfaceRule.CreateAndRegister(context);
            EnforceImportOrExportFromStringLiteralRule.CreateAndRegister(context);
            EnforceSimplifiedForRule.CreateAndRegister(context);
            EnforceSingleDeclarationInForOfRule.CreateAndRegister(context);
            EnforceValidInterpolationRule.CreateAndRegister(context);
            EnforceVariableInitializationRule.CreateAndRegister(context);
            EnforceWellShapedInlineImportRule.CreateAndRegister(context);
            EnforceWellShapedInlineImportConfigPackageRule.CreateAndRegister(context);
            EnforceAmbientAccessInConfig.CreateAndRegister(context);
            ForbidForInRule.CreateAndRegister(context);
            ForbidImportStarRule.CreateAndRegister(context);
            ForbidDefaultArgumentRule.CreateAndRegister(context);
            ForbidMethodDeclarationInObjectLiteralRule.CreateAndRegister(context);
            ForbidReadonlyRule.CreateAndRegister(context);
            ForbidSymbolTypeRule.CreateAndRegister(context);
            ForbidEvalRule.CreateAndRegister(context);
            ForbidVarDeclarationRule.CreateAndRegister(context);
            ForbidNullRule.CreateAndRegister(context);
            ForbidLabelRule.CreateAndRegister(context);
            ForbidSomeTopLevelDeclarationsRule.CreateAndRegister(context);
            ForbidClassRule.CreateAndRegister(context);
            EnforceNumberLiteralRule.CreateAndRegister(context);
            ForbidThrowRule.CreateAndRegister(context);
            ForbidEqualsEqualsRule.CreateAndRegister(context);
            EnforceConstBindingOnTopLevel.CreateAndRegister(context);
            ForbidModifiersOnImportRule.CreateAndRegister(context);
            ForbidExportsInNamespace.CreateAndRegister(context);
            ForbidDivisionOperator.CreateAndRegister(context);
            ForbidDefaultImportsRule.CreateAndRegister(context);
            ForbidLocalFunctionsRule.CreateAndRegister(context);
            EnforceQualifierDeclarationRule.CreateAndRegister(context);
            EnforceTemplateDeclarationRule.CreateAndRegister(context);
            ForbidQualifierTypeDeclarationRule.CreateAndRegister(context);
            ForbidPropertyAccessOnQualifierRule.CreateAndRegister(context);
            ForbidNonVariableQualifierOrTemplateDeclarationRule.CreateAndRegister(context);
            ForbidProjectLikeImportsAndExportsOnV2SpecsRule.CreateAndRegister(context);
            ForbidLogicInProjectsRule.CreateAndRegister(context);
            EnforceWellShapedRootNamespace.CreateAndRegister(context);
            ForbidModuleSelfReferencingRule.CreateAndRegister(context);

            ForbidMutableDataTypesInPublicSurface.CreateAndRegister(context);

            // Configuration related rules.
            EnforceRootConfigStuctureRule.CreateAndRegister(context);
            EnforcePackageConfigurationStructureRule.CreateAndRegister(context);
        }

        /// <summary>
        /// Returns a dictionary of all available (non-initialized) policy rules using the rule name as the key
        /// </summary>
        private static Dictionary<string, PolicyRule> GetAllPolicyRules()
        {
            var enforceNoGlobRule = new EnforceNoGlobRule();
            var enforceTypesOnTopLevelDeclarationsRule = new EnforceTypeAnnotationsOnTopLevelDeclarationsRule();
            var enforceSomeTypeSanityRule = new EnforceSomeTypeSanityRule();
            var enforceExplicitTypedTemplateRule = new EnforceExplicitTypedTemplateRule();
            var enforceNoTransformersRule = new EnforceNoTransformersRule();

            return new Dictionary<string, PolicyRule>
            {
                [enforceNoGlobRule.Name] = enforceNoGlobRule,
                [enforceTypesOnTopLevelDeclarationsRule.Name] = enforceTypesOnTopLevelDeclarationsRule,
                [enforceSomeTypeSanityRule.Name] = enforceSomeTypeSanityRule,
                [enforceExplicitTypedTemplateRule.Name] = enforceExplicitTypedTemplateRule,
                [enforceNoTransformersRule.Name] = enforceNoTransformersRule,
            };
        }

        private static void InitializePolicyRules(Logger logger, LoggingContext loggingContext, AnalysisContext context, HashSet<string> requestedPolicies)
        {
            var allPoliciesDictionary = GetAllPolicyRules();
            var allPolicies = allPoliciesDictionary.Values;

            // From the set of requested policies, we collect the ones that actually exist and the ones that are misconfigured
            var activePolicies = new List<PolicyRule>(allPoliciesDictionary.Count);
            var missingPolicies = new List<string>();

            foreach (var requestedPolicy in requestedPolicies)
            {
                if (allPoliciesDictionary.ContainsKey(requestedPolicy))
                {
                    activePolicies.Add(allPoliciesDictionary[requestedPolicy]);
                }
                else
                {
                    missingPolicies.Add(requestedPolicy);
                }
            }

            WarnOnMissingPoliciesIfNeeded(logger, loggingContext, requestedPolicies, missingPolicies, allPolicies);

            // now we need to initialize the active rules in order to register them for required callbacks
            foreach (var rule in activePolicies)
            {
                rule.Initialize(context);
            }
        }

        /// <summary>
        /// Warns when policy rules are misconfigured. For each set of requested policies, it only prints the warning at most once.
        /// </summary>
        private static void WarnOnMissingPoliciesIfNeeded(Logger logger, LoggingContext loggingContext, HashSet<string> requestedPolicies, ICollection<string> missingPolicies, ICollection<PolicyRule> allPolicies)
        {
            lock (s_thisLock)
            {
                if (!s_requestedSetOfPolicies.Contains(requestedPolicies))
                {
                    s_requestedSetOfPolicies.Add(requestedPolicies);
                    if (missingPolicies.Count > 0)
                    {
                        string missingPoliciesString = string.Join(", ", missingPolicies);
                        string allAvailablePoliciesString = string.Join(Environment.NewLine, allPolicies);
                        logger.ReportMissingPolicies(loggingContext, missingPoliciesString,
                            Environment.NewLine + allAvailablePoliciesString);
                    }
                }
            }
        }

        /// <summary>
        /// Analyze the <paramref name="sourceFile"/>.
        /// </summary>
        /// <remarks>
        /// This method will compute the type of the file and will call appropriate analisys function.
        /// </remarks>
        public void AnalyzeFile(ISourceFile sourceFile, Logger logger, LoggingContext loggingContext, PathTable pathTable, Workspace workspace)
        {
            var scope = GetFileScope(sourceFile);
            switch (scope)
            {
                case RuleAnalysisScope.BuildListFile:
                case RuleAnalysisScope.SpecFile:
                    AnalyzeSpecFile(sourceFile, logger, loggingContext, pathTable, workspace);
                    break;
                case RuleAnalysisScope.RootConfig:
                    AnalyzeRootConfigurationFile(sourceFile, logger, loggingContext, pathTable);
                    break;
                case RuleAnalysisScope.PackageConfig:
                    AnalyzePackageConfigurationFile(sourceFile, logger, loggingContext, pathTable);
                    break;
                default:
                    throw Contract.AssertFailure($"Unknown file scope '{scope}'.");
            }
        }

        private static RuleAnalysisScope GetFileScope(ISourceFile file)
        {
            var fileName = Path.GetFileName(file.Path.AbsolutePath);

            if (ExtensionUtilities.IsModuleConfigurationFile(fileName))
            {
                return RuleAnalysisScope.PackageConfig;
            }

            if (ExtensionUtilities.IsGlobalConfigurationFile(fileName))
            {
                return RuleAnalysisScope.RootConfig;
            }

            if (ExtensionUtilities.IsBuildListFile(fileName))
            {
                return RuleAnalysisScope.BuildListFile;
            }

            return RuleAnalysisScope.SpecFile;
        }
    }
}
