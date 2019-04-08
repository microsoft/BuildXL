// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Defines a scope of the rule: whether it's applicable on spec, root config, package config or combination of them.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1714")]
    [Flags]
    public enum RuleAnalysisScope
    {
        /// <nodoc />
        None = 0,

        /// <summary>
        /// Rule applies on the spec.
        /// </summary>
        SpecFile = 1 << 1,

        /// <summary>
        /// Rule applies on the config.dsc.
        /// </summary>
        RootConfig = 1 << 2,

        /// <summary>
        /// Rule applies on package.config.dsc
        /// </summary>
        PackageConfig = 1 << 3,

        /// <summary>
        /// Rule applies on *.bl
        /// </summary>
        BuildListFile = 1 << 4,

        /// <summary>
        /// Rule applies on all kind of specs.
        /// </summary>
        All = SpecFile | RootConfig | PackageConfig | BuildListFile,
    }

    /// <summary>
    /// Defines analysis kind, like lanugage restriction, language policy, domain policy etc.
    /// </summary>
    public enum RuleType
    {
        /// <nodoc />
        None = 0,

        /// <summary>
        /// Non-configurable rule that defines the langauge.
        /// </summary>
        LanguageRule,

        /// <summary>
        /// Configurable language rule that can be enabled or disabled.
        /// </summary>
        LanguagePolicy,

        /// <summary>
        /// Domain policy, like whether to use glob or not. WIP: change this comment.
        /// </summary>
        UserConfigurablePolicy,
    }

    /// <summary>
    /// Abstract base class for DS Lint rules.
    /// </summary>
    internal abstract class DiagnosticRule
    {
        public abstract void Initialize(AnalysisContext context);

        /// <summary>
        /// Returns an analysis scope for the current rule.
        /// </summary>
        /// <remarks>
        /// SpecFile is a reasonable default!
        /// </remarks>
        public virtual RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;
        
        /// <summary>
        /// Returns type of a current rule.
        /// </summary>
        public abstract RuleType RuleType { get; }
    }

    /// <summary>
    /// Configurable policy rule that can be turned off and on.
    /// </summary>
    internal abstract class LanguagePolicyRule : DiagnosticRule
    {
        /// <inheritdoc />
        public override RuleType RuleType => RuleType.LanguagePolicy;
    }

    /// <summary>
    /// Non-configurable DScript language rule.
    /// </summary>
    /// <remarks>
    /// This rule is "baked" into the language and can not be disabled.
    /// </remarks>
    internal abstract class LanguageRule : DiagnosticRule
    {
        /// <inheritdoc />
        public override RuleType RuleType => RuleType.LanguageRule;
    }
}
