// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    /// <summary>
    /// Represents a TestClass
    /// </summary>
    public sealed class TestClass
    {
        /// <summary>
        /// The name of the UnitTest
        /// </summary>
        private const string UnitTestName = "unitTest";

        /// <summary>
        /// The name of the test class
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The FilePath of the test
        /// </summary>
        public string TestFilePath { get; }

        /// <summary>
        /// Test functions to invoke
        /// </summary>
        public IReadOnlyList<TestFunction> Functions { get; }

        /// <nodoc />
        public TestClass(string name, string testFilePath, params TestFunction[] functions)
        {
            Name = name;
            TestFilePath = testFilePath;
            Functions = functions;
        }

        /// <summary>
        /// Extracts a TestClass from a string
        /// </summary>
        public static bool TryExtractTestClass(Logger logger, ISourceFile sourceFile, IDictionary<string, string> lkgFiles, out TestClass testClass)
        {
            bool success = true;
            var testFunctions = new Dictionary<string, TestFunction>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in NodeWalker.TraverseBreadthFirstAndSelf(sourceFile))
            {
                bool hasValidUnitTestDecorator;
                success &= TryCheckHasUnitTestDecorator(logger, sourceFile, node, out hasValidUnitTestDecorator);

                if (hasValidUnitTestDecorator)
                {
                    if (node.Kind != SyntaxKind.FunctionDeclaration)
                    {
                        logger.LogError(sourceFile, node, "UnitTest attribute is only allowed on top-level functions");
                        success = false;
                        continue;
                    }

                    TestFunction testFunction;
                    var functionDeclaration = (IFunctionDeclaration)node;
                    if (!TestFunction.TryExtractFunction(logger, sourceFile, functionDeclaration, lkgFiles, out testFunction))
                    {
                        success = false;
                        continue;
                    }

                    TestFunction existingFunction;
                    if (testFunctions.TryGetValue(testFunction.ShortName, out existingFunction))
                    {
                        logger.LogError(sourceFile, node, C($"Duplicate test-definition. There are multiple tests with name '{testFunction.ShortName}': '{existingFunction.FullIdentifier}' and '{testFunction.FullIdentifier}'"));
                        success = false;
                        continue;
                    }

                    testFunctions.Add(testFunction.ShortName, testFunction);
                }
            }

            if (success && testFunctions.Count == 0)
            {
                logger.LogError(sourceFile.FileName, 0, 0, C($"No UnitTests found in file. Please decorate functions with @@{UnitTestName}"));
                testClass = null;
                return false;
            }

            testClass = new TestClass(
                Path.GetFileNameWithoutExtension(sourceFile.FileName),
                sourceFile.FileName,
                testFunctions.Values.ToArray());
            return success;
        }

        private static bool TryCheckHasUnitTestDecorator(Logger logger, ISourceFile sourceFile, INode node, out bool hasValidUnitTestDecorator)
        {
            hasValidUnitTestDecorator = false;
            if (node.Decorators != null)
            {
                foreach (var decorator in node.Decorators)
                {
                    bool isUnitTestDecorator;
                    if (!TryCheckIsUnitTestDecorator(logger, sourceFile, decorator, out isUnitTestDecorator))
                    {
                        return false;
                    }

                    if (isUnitTestDecorator)
                    {
                        if (hasValidUnitTestDecorator)
                        {
                            logger.LogError(sourceFile, node, "Duplicate unitTest decorator. Only one is allowed.");
                            return false;
                        }

                        hasValidUnitTestDecorator = true;
                    }
                }
            }

            return true;
        }

        private static bool TryCheckIsUnitTestDecorator(Logger logger, ISourceFile sourceFile, IDecorator decorator, out bool isUnitTestDecorator)
        {
            isUnitTestDecorator = false;
            var expression = decorator.Expression;

            ICallExpression callExpression = null;
            if (expression.Kind == SyntaxKind.CallExpression)
            {
                callExpression = (ICallExpression)expression;
                expression = callExpression.Expression;
            }

            if (expression.Kind == SyntaxKind.PropertyAccessExpression)
            {
                var propertyAccess = (IPropertyAccessExpression)expression;
                expression = propertyAccess.Name;
            }

            if (expression.Kind == SyntaxKind.Identifier)
            {
                var identifier = (IIdentifier)expression;
                if (string.Equals(UnitTestName, identifier.Text, StringComparison.Ordinal))
                {
                    isUnitTestDecorator = true;
                }
                else if (string.Equals(UnitTestName, identifier.Text, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError(sourceFile, decorator, C($"Decorator '{identifier.Text}' is using the wrong casing. Please use '{UnitTestName}'"));
                    return false;
                }
            }

            // If this is a unit test and it came from a function call, perform some extra checks
            if (isUnitTestDecorator && callExpression != null)
            {
                if (callExpression.Arguments.Count > 0)
                {
                    logger.LogError(sourceFile, decorator, "UnitTest decorators are not allowed to have arguments");
                    return false;
                }

                if (callExpression.TypeArguments != null && callExpression.TypeArguments.Count > 0)
                {
                    logger.LogError(sourceFile, decorator, "UnitTest decorators are not allowed to be generic");
                    return false;
                }
            }

            return true;
        }
    }
}
