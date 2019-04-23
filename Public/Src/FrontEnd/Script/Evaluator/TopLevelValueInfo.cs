// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using JetBrains.Annotations;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// All state capture when thunk is started
    /// </summary>
    public sealed class TopLevelValueInfo
    {
        /// <summary>
        /// Gets the active thunk within the current context, if any.
        /// </summary>
        /// <remarks>
        /// To discover the complete chain of all active thunks, one has to walk the <see cref="ImmutableContextBase.ParentContext" /> chain as well.
        /// </remarks>
        internal Thunk ActiveThunk;

        /// <summary>
        /// Gets the full symbol of the value
        /// </summary>
        public FullSymbol ValueName { get; }

        /// <summary>
        /// Gets the path to the spec file where the value is declared
        /// </summary>
        public AbsolutePath SpecFile { get; }

        /// <summary>
        /// Gets the line information in the <see cref="SpecFile"/> where the top level value is declared
        /// </summary>
        public LineInfo ValueDeclarationLineInfo { get; }

        /// <summary>
        /// The template value implicitly captured by the active thunk. Can be null if we are in V1 mode.
        /// </summary>
        [CanBeNull]
        public object CapturedTemplateValue { get; }

        /// <nodoc />
        public TopLevelValueInfo(
            Thunk activeThunk,
            FullSymbol valueName,
            AbsolutePath specFile,
            LineInfo valueDeclarationLineInfo,
            object templateValue)
        {
            Contract.Requires(activeThunk != null);
            Contract.Requires(valueName.IsValid);
            Contract.Requires(specFile.IsValid);

            ActiveThunk = activeThunk;
            ValueName = valueName;
            SpecFile = specFile;
            ValueDeclarationLineInfo = valueDeclarationLineInfo;
            CapturedTemplateValue = templateValue;
        }
    }
}
