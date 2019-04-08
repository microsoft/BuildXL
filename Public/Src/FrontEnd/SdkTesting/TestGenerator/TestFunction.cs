// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    /// <summary>
    /// Extracted UnitTest function
    /// </summary>
    public sealed class TestFunction
    {
        /// <summary>
        /// The short name of the test, this is the name of the function
        /// </summary>
        public string ShortName { get; }

        /// <summary>
        /// The full identifier for the test function including the namespaces
        /// </summary>
        public string FullIdentifier { get; }

        /// <summary>
        /// Points to the LkgFilePath used to validate
        /// </summary>
        public string LkgFilePath { get; }

        /// <summary>
        /// Line number of the test
        /// </summary>
        public LineAndColumn OriginalLineAndColumn { get; }

        /// <nodoc />
        public TestFunction(string shortName, string fullIdentifier, LineAndColumn originalLineAndColumn, string lkgFilePath)
        {
            ShortName = shortName;
            FullIdentifier = fullIdentifier;
            OriginalLineAndColumn = originalLineAndColumn;
            LkgFilePath = lkgFilePath;
        }

        /// <summary>
        /// Validates and Tries to extract the information function from the function
        /// </summary>
        public static bool TryExtractFunction(Logger logger, ISourceFile sourceFile, IFunctionDeclaration possibleFunction, IDictionary<string, string> lkgFiles, out TestFunction testFunction)
        {
            testFunction = null;

            var location = possibleFunction.GetLineInfo(sourceFile).ToLineAndColumn();

            var shortName = possibleFunction.Name.Text;

            var fullName = shortName;
            INode current = possibleFunction.Parent;
            while (current != null)
            {
                switch (current.Kind)
                {
                    case SyntaxKind.SourceFile:
                    case SyntaxKind.ModuleBlock:
                        current = current.Parent;
                        break;
                    case SyntaxKind.ModuleDeclaration:
                        var moduleDecl = (IModuleDeclaration)current;
                        fullName = moduleDecl.Name.Text + "." + fullName;
                        current = current.Parent;
                        break;
                    default:
                        logger.LogError(sourceFile, possibleFunction, "Only top-level functions are allowed to be declared as UnitTests.");
                        return false;
                }
            }

            if (!possibleFunction.Flags.HasFlag(NodeFlags.Export))
            {
                logger.LogError(sourceFile, possibleFunction, C($"UnitTest function '{fullName}' must be exported"));
                return false;
            }

            if (possibleFunction.Type != null && possibleFunction.Type.Kind != SyntaxKind.VoidKeyword)
            {
                logger.LogError(sourceFile, possibleFunction, C($"UnitTest function '{fullName}' cannot return a value. It must be a 'void' function"));
                return false;
            }

            if (possibleFunction.Parameters.Count > 0)
            {
                logger.LogError(sourceFile, possibleFunction, C($"UnitTest function '{fullName}' cannot have any parameters"));
                return false;
            }

            if (possibleFunction.TypeParameters != null && possibleFunction.TypeParameters.Count > 0)
            {
                logger.LogError(sourceFile, possibleFunction, C($"UnitTest function '{fullName}' cannot be generic"));
                return false;
            }

            if (possibleFunction.Body == null)
            {
                logger.LogError(sourceFile, possibleFunction, C($"UnitTest function '{fullName}' must declare a function body"));
                return false;
            }

            string lkgFilePath = null;
            var lkgKey = Args.ComputeLkgKey(sourceFile.FileName, shortName);
            lkgFiles.TryGetValue(lkgKey, out lkgFilePath);

            // Remove it to mark it is already processed
            lkgFiles.Remove(lkgKey);

            testFunction = new TestFunction(shortName, fullName, location, lkgFilePath);
            return true;
        }
    }
}
