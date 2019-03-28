// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using TypeScript.Net.Types;

namespace BuildXL.Ide.LanguageServer.Completion
{
    /// <summary>
    /// Handles completions for a qualified name.
    /// </summary>
    /// <remarks>
    /// A qualified name is typically an namespace, type, value imported from another module.
    /// 
    /// For example:
    /// import {Shared, Preprocessor, PlatformDependentQualifier} from "Build.Wdg.Native.Shared";
    /// 
    /// Shared, Preprocessor, etc. are all qualified names.
    /// 
    /// So when completion happens in scenarios like:
    /// interface MyNewType {
    ///    myField: Shared.{completion happens here}
    /// }
    /// 
    /// Completion is requested on "Shared." where the stuff to the right of the dot is 
    /// an identifier, and the parent is a qualified name, and the qualified name's parent
    /// (in this example) is a type-like field since you are typing an interface member.
    /// </remarks>
    internal static class QualifiedName
    {        
        /// <summary>
        /// Creates an array of symbols for a qualified name.
        /// </summary>
        /// <remarks>
        /// This was ported from the TypeScript version of the language server.
        /// </remarks>
        public static IEnumerable<ISymbol> CreateCompletionItemsFromQualifiedName(CompletionState completionState, INode completionNode)
        {
            var qualifiedName = completionNode.Cast<IQualifiedName>();

            return PropertyAccessExpression.GetTypeScriptMemberSymbols(qualifiedName.Left, completionState.TypeChecker);
        }
    }
}
