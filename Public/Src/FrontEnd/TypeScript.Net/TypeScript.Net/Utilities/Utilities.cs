// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using TypeScript.Net.Core;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Extensions;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;
using static TypeScript.Net.Types.NodeUtilities;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net
{
    // TODO: in typescript implementation this type is used only by the language service. This is a good candidate for being removed.
    internal class ReferencePathMatchResult
    {
        public IFileReference FileReference { get; set; }

        public IDiagnosticMessage DiagnosticMessage { get; set; }

        public Optional<bool> IsNoDefaultLib { get; set; }
    }

    internal static class Utils
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        public static string GetSourceTextOfNodeFromSourceFile(ISourceFile sourceFile, INode node, bool includeTrivia = false)
        {
            if (NodeIsMissing(node))
            {
                return string.Empty;
            }

            // DScript-specific. If the node is injected, then the text does not exist in the original file. So we grab the canonical form.
            if (node.IsInjectedForDScript())
            {
                return node.ToDisplayString();
            }

            // Original implementation rescanned the source file to get the text of the node.
            // But that required holding the text in memory for the entire front-end pipeline.
            // This implementation is not perfect but is good enough for all practical purposes.
            return node.ToDisplayString();
        }

        public static string GetTextOfNodeFromSourceText(TextSource sourceText, INode node)
        {
            if (NodeIsMissing(node))
            {
                return string.Empty;
            }

            var startIndex = Scanner.SkipTrivia(sourceText, node.Pos);
            return sourceText.Substring(startIndex, node.End - startIndex);
        }

        /// <summary>
        /// Add an extra underscore to identifiers that start with two underscores to avoid issues with magic names like '__proto__'
        /// </summary>
        public static string EscapeIdentifier(string identifier)
        {
            return identifier.Length >= 2 && identifier.CharCodeAt(0) == CharacterCodes._ && identifier.CharCodeAt(1) == CharacterCodes._ 
                ? "_" + identifier 
                : identifier;
        }

        internal static bool TokenIsIdentifierOrKeyword(SyntaxKind token)
        {
            return token >= SyntaxKind.Identifier;
        }

        /// <summary>
        /// Remove extra underscore from escaped identifier
        /// </summary>
        public static string UnescapeIdentifier(string identifier)
        {
            return identifier.Length >= 3 && identifier.CharCodeAt(0) == CharacterCodes._ && identifier.CharCodeAt(1) == CharacterCodes._ && identifier.CharCodeAt(2) == CharacterCodes._ 
                ? identifier.Substring(1) 
                : identifier;
        }

        /// <summary>
        /// Make an identifier from an external module name by extracting the string after the last "/" and replacing
        /// all non-alphanumeric characters with underscores
        /// </summary>
        public static string MakeIdentifierFromModuleName(string moduleName)
        {
            return StringExtensions.Replace(StringExtensions.Replace(Path.GetBaseFileName(moduleName), "^(\\d)", "_$1"), "\\W", "_");
        }

        private const string FullTripleSlashReferencePathRegEx = "^(\\/\\/\\/\\s*<reference\\s+path\\s*=\\s*)('|\")(.+?)\\2.*?\\/>";

        public static bool IsExternalModuleNameRelative(string moduleName)
        {
            // TypeScript 1.0 spec (April 2014): 11.2.1
            // An external module name is "relative" if the first term is "." or "..".
            if (moduleName.Length < 2)
            {
                return false;
            }

            var first = moduleName[0];
            var second = moduleName[1];

            // './' or '.\'
            if (first == '.' && (second == '/' || second == '\\'))
            {
                return true;
            }

            char? third = moduleName.Length >= 3 ? (char?)moduleName[2] : null;
            char? fourth = moduleName.Length >= 4 ? (char?)moduleName[3] : null;

            // '../' or '..\'
            if (first == '.' && second == '.' && (third == '/' || fourth == '\\'))
            {
                return true;
            }

            return false;

            // Old implementation:
            // return moduleName.Substr(0, 2) == "./" || moduleName.Substr(0, 3) == "../" || moduleName.Substr(0, 2) == ".\\" || moduleName.Substr(0, 3) == "..\\";
            // Allocated too much.
        }

        public static bool IsInstantiatedModule(INode node, bool preserveConstEnums)
        {
            var moduleState = Binding.Binder.GetModuleInstanceState(node);
            return moduleState == Binding.ModuleInstanceState.Instantiated ||
                (preserveConstEnums && moduleState == Binding.ModuleInstanceState.ConstEnumOnly);
        }

        public static ISourceFile TryResolveScriptReference(ScriptReferenceHost host, ISourceFile sourceFile, IFileReference reference)
        {
            if (!host.GetCompilerOptions().NoResolve.HasValue)
            {
                var referenceFileName = Path.IsRootedDiskPath(reference.FileName) ? reference.FileName : Path.CombinePaths(Path.GetDirectoryPath(sourceFile.FileName), reference.FileName);
                return host.GetSourceFile(referenceFileName);
            }

            return null;
        }

        internal static ReferencePathMatchResult GetFileReferenceFromReferencePath(string comment, ICommentRange commentRange)
        {
            var simpleReferenceRegEx = "^///\\s*<reference\\s+";
            var isNoDefaultLibRegEx = "^(///\\s*<reference\\s+no-default-lib\\s*=\\s*)('|\")(.+?)\\2\\s */> ";

            if (simpleReferenceRegEx.Test(comment))
            {
                if (isNoDefaultLibRegEx.Test(comment))
                {
                    return new ReferencePathMatchResult() { IsNoDefaultLib = true };
                }

                var matchResult = FullTripleSlashReferencePathRegEx.Exec(comment);
                if (matchResult != null)
                {
                    var start = commentRange.Pos;
                    var end = commentRange.End;
                    return new ReferencePathMatchResult()
                    {
                        FileReference = new FileReference()
                        {
                            Pos = start,
                            End = end,
                            FileName = matchResult.Groups[3].ToString(),
                        },
                        IsNoDefaultLib = false,
                    };
                }

                return new ReferencePathMatchResult()
                {
                    DiagnosticMessage = Errors.Invalid_reference_directive_syntax,
                };
            }

            return null;
        }

        public static string[] IndentStrings { get; } = { string.Empty, "    " };

        public static string GetIndentString(int level)
        {
            if (IndentStrings[level] == null)
            {
                IndentStrings[level] = GetIndentString(level - 1) + IndentStrings[1];
            }

            return IndentStrings[level];
        }

        public static int GetIndentSize()
        {
            return IndentStrings[1].Length;
        }

        public static ScriptTarget GetEmitScriptTarget(ICompilerOptions compilerOptions)
        {
            return compilerOptions.Target.HasValue ? compilerOptions.Target.Value : ScriptTarget.Es3;
        }

        public static ModuleKind GetEmitModuleKind(ICompilerOptions compilerOptions)
        {
            return compilerOptions.Module.HasValue ?
                compilerOptions.Module.Value :
                GetEmitScriptTarget(compilerOptions) == ScriptTarget.Es6 ? ModuleKind.Es6 : ModuleKind.None;
        }

        public static IConstructorDeclaration GetFirstConstructorWithBody(IClassLikeDeclaration node)
        {
            return NodeArrayExtensions.ForEachUntil(node.Members, member =>
            {
                if (member.Kind == SyntaxKind.Constructor && NodeIsPresent(member.Cast<ConstructorDeclaration>().Body))
                {
                    return member.Cast<ConstructorDeclaration>();
                }

                return (IConstructorDeclaration)null;
            });
        }

        public static ISymbol GetLocalSymbolForExportDefault(ISymbol symbol)
        {
            if (symbol?.ValueDeclaration != null && (symbol.ValueDeclaration.Flags & NodeFlags.Default) != NodeFlags.None)
            {
                return symbol.ValueDeclaration.LocalSymbol;
            }

            return null;
        }

        public static string GetDefaultLibFileName(CompilerOptions options)
        {
            return options.Target == ScriptTarget.Es6 ? "lib.Es6.d.Ts" : "lib.D.ts";
        }

        public static IDeclaration GetTypeParameterOwner(IDeclaration d)
        {
            if (d?.Kind == SyntaxKind.TypeParameter)
            {
                for (INode current = d; current != null; current = current.Parent)
                {
                    if (IsFunctionLike(current) != null || IsClassLike(current) != null || current.Kind == SyntaxKind.InterfaceDeclaration)
                    {
                        return current.Cast<IDeclaration>();
                    }
                }
            }

            return null;
        }

        /// <nodoc/>
        public readonly struct AccessorDeclarations
        {
            /// <nodoc/>
            public IAccessorDeclaration FirstAccessor { get; }

            /// <nodoc/>
            public IAccessorDeclaration SecondAccessor { get; }

            /// <nodoc/>
            public IAccessorDeclaration GetAccessor { get; }

            /// <nodoc/>
            public IAccessorDeclaration SetAccessor { get; }

            /// <nodoc/>
            public AccessorDeclarations(IAccessorDeclaration firstAccessor, IAccessorDeclaration secondAccessor, IAccessorDeclaration getAccessor, IAccessorDeclaration setAccessor)
            {
                FirstAccessor = firstAccessor;
                SecondAccessor = secondAccessor;
                GetAccessor = getAccessor;
                SetAccessor = setAccessor;
            }
        }

        public static AccessorDeclarations GetAllAccessorDeclarations(INodeArray<IDeclaration> declarations, IAccessorDeclaration accessor)
        {
            IAccessorDeclaration firstAccessor = null;
            IAccessorDeclaration secondAccessor = null;
            IAccessorDeclaration getAccessor = null;
            IAccessorDeclaration setAccessor = null;
            if (HasDynamicName(accessor))
            {
                firstAccessor = accessor;
                if (accessor.Kind == SyntaxKind.GetAccessor)
                {
                    getAccessor = accessor;
                }
                else if (accessor.Kind == SyntaxKind.SetAccessor)
                {
                    setAccessor = accessor;
                }
                else
                {
                    Contract.Assert(false, "Accessor has wrong kind");
                }
            }
            else
            {
                NodeArrayExtensions.ForEach(declarations, member =>
                {
                    if ((member.Kind == SyntaxKind.GetAccessor || member.Kind == SyntaxKind.SetAccessor)
                        && (member.Flags & NodeFlags.Static) == (accessor.Flags & NodeFlags.Static))
                    {
                        var memberName = GetPropertyNameForPropertyNameNode(member.Name);
                        var accessorName = GetPropertyNameForPropertyNameNode(accessor.Name);
                        if (memberName == accessorName)
                        {
                            if (firstAccessor == null)
                            {
                                firstAccessor = member.Cast<IAccessorDeclaration>();
                            }
                            else if (secondAccessor == null)
                            {
                                secondAccessor = member.Cast<IAccessorDeclaration>();
                            }

                            if (member.Kind == SyntaxKind.GetAccessor && getAccessor == null)
                            {
                                getAccessor = member.Cast<IAccessorDeclaration>();
                            }

                            if (member.Kind == SyntaxKind.SetAccessor && setAccessor == null)
                            {
                                setAccessor = member.Cast<IAccessorDeclaration>();
                            }
                        }
                    }
                });
            }

            return new AccessorDeclarations(firstAccessor, secondAccessor, getAccessor, setAccessor);
        }
    }
}
