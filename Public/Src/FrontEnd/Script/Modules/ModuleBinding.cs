// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.Utilities;
using JetBrains.Annotations;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    ///     Bindings for module literals.
    /// </summary>
    /// <remarks>
    ///     This binding is the right-hand side of the symbol table entry of a module literal.
    /// </remarks>
    /// TODO: Declaration.ModifierFlags is used to flag bindings as exports and it is shared with declarations. Consider refactor this into a binding-specific class and incorporate to ExportKind
    public sealed class ModuleBinding
    {
        /// <summary>
        ///     Creates a module binding. A modifier can be passed to specify if the binding is exported.
        /// </summary>
        /// <remarks>
        ///     In case the binding is exported but more detailed information of the kind of export is available, pass
        ///     ExportKind instead
        /// </remarks>
        public ModuleBinding(object body, Declaration.DeclarationFlags modifier, LineInfo location)
        {
            Body = body;
            Modifier = modifier;
            Location = location;
        }

        /// <nodoc />
        [JetBrains.Annotations.NotNull]
        public object Body { get; }

        /// <nodoc />
        public LineInfo Location { get; }

        /// <nodoc />
        public Declaration.DeclarationFlags Modifier { get; }

        /// <nodoc />
        public bool IsExported => (Modifier & Declaration.DeclarationFlags.Export) != 0;

        /// <summary>
        ///     Creates fun binding.
        /// </summary>
        /// <remarks>
        ///     Used by ambients only!
        /// </remarks>
        public static ModuleBinding CreateFun(SymbolAtom name, InvokeAmbient fun, CallSignature callSignature, StringTable stringTable)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(fun != null);
            Contract.Requires(callSignature != null);

            return CreateFun(name, fun, callSignature, new FunctionStatistic(SymbolAtom.Invalid, name, callSignature, stringTable));
        }

        /// <summary>
        ///     Creates fun binding.
        /// </summary>
        /// <remarks>
        ///     Used by ambients only!
        /// </remarks>
        public static ModuleBinding CreateFun(SymbolAtom name, InvokeAmbient fun, CallSignature callSignature, FunctionStatistic statistic)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(fun != null);
            Contract.Requires(callSignature != null);

            return new ModuleBinding(
                FunctionLikeExpression.CreateAmbient(name, callSignature, fun, statistic),
                Declaration.DeclarationFlags.Export, default(LineInfo));
        }

        /// <summary>
        ///     Creates enum binding.
        /// </summary>
        /// <remarks>
        ///     Used by ambients only!
        /// </remarks>
        public static ModuleBinding CreateEnum(SymbolAtom name, int value)
        {
            Contract.Requires(name.IsValid);

            return new ModuleBinding(new EnumValue(name, value), Declaration.DeclarationFlags.Export, default(LineInfo));
        }

    }
}
