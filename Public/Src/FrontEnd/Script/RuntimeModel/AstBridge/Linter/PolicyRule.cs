// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Abstract base class for optional (user-configurable) DS Lint rules.
    /// </summary>
    internal abstract class PolicyRule : DiagnosticRule
    {
        /// <summary>
        /// Name of the rule, which can be referenced from a configuration file
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// User-facing description of what the rule does
        /// </summary>
        public abstract string Description { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return "'" + Name + "': " + Description;
        }

        /// <inheritdoc />
        public override RuleType RuleType => RuleType.UserConfigurablePolicy;
    }
}
