// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.FrontEnd.Sdk.Workspaces
{
    /// <summary>
    /// Represents the case where a module resolver can't be initialized.
    /// </summary>
    /// <remarks>
    /// The failure is not very actionable from an end use perspective, this failure is triggered after more detailed logs
    /// have already been logged, and essentially to indicate failure to downstream consumers.
    /// </remarks>
    public sealed class WorkspaceModuleResolverGenericInitializationFailure : Failure
    {
        private readonly string m_resolverKind;

        /// <nodoc/>
        public WorkspaceModuleResolverGenericInitializationFailure(string resolverKind)
        {
            m_resolverKind = resolverKind;
        }

        /// <inheritdoc/>
        public override BuildXLException CreateException()
        {
            return new BuildXLException(Describe());
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Workspace module resolver '{m_resolverKind}' could not be initialized. Errors should have been logged already.");
        }

        /// <inheritdoc/>
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
