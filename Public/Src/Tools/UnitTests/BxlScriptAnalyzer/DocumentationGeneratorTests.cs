// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Analyzer.Analyzers;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities;
using Xunit;

namespace Test.Tool.DScript.Analyzer
{
    public class DocumentationGeneratorTests : AnalyzerTest<DocumentationGenerator>
    {
        [Fact]
        public void TestFunction()
        {
            DocumentationGeneratorTestSuccess(
                @"namespace Foo
                {
                    /** This is ignored */
                    export function isTrue() { return true; }
                    namespace Bar
                    {
                        /** Here we define what bar does.
                         * @param x This does things
                         * @param y
                         * This is a second line.
                         */
                        @@public
                        export function bar() { return false; }
                    }
                }",
                "TestModule.md",
                @"# TestModule Module
* Workspace
  * [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index)

| Type | Name | Description |
|------|------|-------------|
| ![Function](https://docs.microsoft.com/en-us/media/toolbars/member.svg =14x14) | [Foo.Bar.bar](#foo.bar.bar-function) | Here we define what bar does. |
# Foo.Bar.bar Function

| Parent | Module | Workspace |
| - | - | - |
| [Foo.Bar](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestModule](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index) |
### Definition
Here we define what bar does.
This is a second line.
### Parameters
* **x**: This does things
* **y**
");
        }

        [Fact]
        public void TestInterface()
        {
            DocumentationGeneratorTestSuccess(
                @"namespace Foo
                {
                    namespace Bar
                    {
                        /** Here we define what ITest does.
                         * This is a second line.
                         */
                        @@public
                        export interface ITest { /** X doc */ x: number; /** Y doc */ @@Tool.option(""/w"") y: string; }
                    }
                }",
                "TestModule.md",
                @"# TestModule Module
* Workspace
  * [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index)

| Type | Name | Description |
|------|------|-------------|
| ![Interface](https://docs.microsoft.com/en-us/media/toolbars/type.svg =14x14) | [Foo.Bar.ITest](#foo.bar.itest-interface) | Here we define what ITest does. |
# Foo.Bar.ITest Interface

| Parent | Module | Workspace |
| - | - | - |
| [Foo.Bar](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestModule](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index) |
### Definition
Here we define what ITest does.
This is a second line.

| Properties | Description |
| - | - |
| [Foo.Bar.ITest.x](#foo.bar.itest.x-property) | X doc |
| [Foo.Bar.ITest.y](#foo.bar.itest.y-property) | Y doc |

## Foo.Bar.ITest.x Property
X doc
## Foo.Bar.ITest.y Property
Y doc
Tool option /w
");
        }

        [Fact]
        public void TestEnum()
        {
            DocumentationGeneratorTestSuccess(
                @"namespace Foo
                {
                    namespace Bar
                    {
                        /** Here we define what Enum1 does.
                         * This is a second line.
                         */
                        @@public
                        export const enum Enum1 {
                            /** value1 definition */
                            @@Tool.option(""--value1"")
                            value1 = 1,
                            @@Tool.option(""--value2"")
                            /** value2 definition */
                            value2 = 2,
                        }
                    }
                }",
                "TestModule.md",
                @"# TestModule Module
* Workspace
  * [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index)

| Type | Name | Description |
|------|------|-------------|
| ![Enum](https://docs.microsoft.com/en-us/media/toolbars/type.svg =14x14) | [Foo.Bar.Enum1](#foo.bar.enum1-enum) | Here we define what Enum1 does. |
# Foo.Bar.Enum1 Enum

| Parent | Module | Workspace |
| - | - | - |
| [Foo.Bar](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestModule](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index) |
### Definition
Here we define what Enum1 does.
This is a second line.
### Values
* value1
  * value1 definition
  * Tool option --value1
* value2
  * value2 definition
  * Tool option --value2
");
        }

        [Fact]
        public void TestType()
        {
            DocumentationGeneratorTestSuccess(
                @"namespace Foo
                {
                    namespace Bar
                    {
                        /** Here we define what TestType does.
                         * This is a second line.
                         */
                        @@public
                        export type TestType = number;
                    }
                }",
                "TestModule.md",
                @"# TestModule Module
* Workspace
  * [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index)

| Type | Name | Description |
|------|------|-------------|
| ![Type](https://docs.microsoft.com/en-us/media/toolbars/type.svg =14x14) | [Foo.Bar.TestType](#foo.bar.testtype-type) | Here we define what TestType does. |
# Foo.Bar.TestType Type

| Parent | Module | Workspace |
| - | - | - |
| [Foo.Bar](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestModule](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index) |
### Definition
Here we define what TestType does.
This is a second line.
");
        }

        [Fact]
        public void TestValue()
        {
            DocumentationGeneratorTestSuccess(
                @"namespace Foo
                {
                    namespace Bar
                    {
                        /** Here we define what TestValue does.
                         * This is a second line.
                         */
                        @@public
                        export const TestValue = 42;
                    }
                }",
                "TestModule.md",
                @"# TestModule Module
* Workspace
  * [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index)

| Type | Name | Description |
|------|------|-------------|
| ![Value](https://docs.microsoft.com/en-us/media/toolbars/member.svg =14x14) | [Foo.Bar.TestValue](#foo.bar.testvalue-value) | Here we define what TestValue does. |
# Foo.Bar.TestValue Value

| Parent | Module | Workspace |
| - | - | - |
| [Foo.Bar](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestModule](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index) |
### Definition
Here we define what TestValue does.
This is a second line.
");
        }

        [Fact]
        public void TestCombined()
        {
            DocumentationGeneratorTestSuccess(
                @"namespace Foo
                {
                    namespace Bar
                    {
                        /** Here we define what bar does.
                         * This is a second line.
                         */
                        @@public
                        export function bar() { return false; }

                        /** Here we define what ITest does.
                         * This is a second line.
                         */
                        @@public
                        export interface ITest { @@Tool.option(""--barfoo"") /** X doc */ x: number; /** Y doc */ @@Tool.option(""--foobar"") y: string; }

                        /** Here we define what Enum1 does.
                         * This is a second line.
                         */
                        @@public
                        export const enum Enum1 {
                            /** value1 definition */
                            @@Tool.option(""--value1"")
                            value1 = 1,
                            @@Tool.option(""--value2"")
                            /** value2 definition */
                            value2 = 2,
                        }

                        /** Here we define what TestType does.
                         * This is a second line.
                         */
                        @@public
                        export type TestType = number;

                        /** Here we define what TestValue does.
                         * This is a second line.
                         */
                        @@public
                        export const TestValue = 42;
                    }
                }",
                "TestModule.md",
                @"# TestModule Module
* Workspace
  * [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index)

| Type | Name | Description |
|------|------|-------------|
| ![Interface](https://docs.microsoft.com/en-us/media/toolbars/type.svg =14x14) | [Foo.Bar.ITest](#foo.bar.itest-interface) | Here we define what ITest does. |
| ![Type](https://docs.microsoft.com/en-us/media/toolbars/type.svg =14x14) | [Foo.Bar.TestType](#foo.bar.testtype-type) | Here we define what TestType does. |
| ![Enum](https://docs.microsoft.com/en-us/media/toolbars/type.svg =14x14) | [Foo.Bar.Enum1](#foo.bar.enum1-enum) | Here we define what Enum1 does. |
| ![Function](https://docs.microsoft.com/en-us/media/toolbars/member.svg =14x14) | [Foo.Bar.bar](#foo.bar.bar-function) | Here we define what bar does. |
| ![Value](https://docs.microsoft.com/en-us/media/toolbars/member.svg =14x14) | [Foo.Bar.TestValue](#foo.bar.testvalue-value) | Here we define what TestValue does. |
# Foo.Bar.ITest Interface

| Parent | Module | Workspace |
| - | - | - |
| [Foo.Bar](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestModule](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index) |
### Definition
Here we define what ITest does.
This is a second line.

| Properties | Description |
| - | - |
| [Foo.Bar.ITest.x](#foo.bar.itest.x-property) | X doc |
| [Foo.Bar.ITest.y](#foo.bar.itest.y-property) | Y doc |

## Foo.Bar.ITest.x Property
X doc
Tool option --barfoo
## Foo.Bar.ITest.y Property
Y doc
Tool option --foobar
# Foo.Bar.TestType Type

| Parent | Module | Workspace |
| - | - | - |
| [Foo.Bar](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestModule](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index) |
### Definition
Here we define what TestType does.
This is a second line.
# Foo.Bar.Enum1 Enum

| Parent | Module | Workspace |
| - | - | - |
| [Foo.Bar](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestModule](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index) |
### Definition
Here we define what Enum1 does.
This is a second line.
### Values
* value1
  * value1 definition
  * Tool option --value1
* value2
  * value2 definition
  * Tool option --value2
# Foo.Bar.bar Function

| Parent | Module | Workspace |
| - | - | - |
| [Foo.Bar](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestModule](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index) |
### Definition
Here we define what bar does.
This is a second line.
# Foo.Bar.TestValue Value

| Parent | Module | Workspace |
| - | - | - |
| [Foo.Bar](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestModule](/BuildXL/Reference-Guide/Sdk-Documentation/TestModule) | [TestRepo](/BuildXL/Reference-Guide/Sdk-Documentation/index) |
### Definition
Here we define what TestValue does.
This is a second line.
");
        }

        /// <summary>
        /// Runs the analyzer on the testSouce and expects it to succeed with the given formatted text.
        /// </summary>
        public void DocumentationGeneratorTestSuccess(
            string testSource,
            string expectedFileName = null,
            string expectedFileContents = null,
            string[] extraSources = null,
            Dictionary<string, string> modules = null)
    {
        using (TempFileStorage temp = new TempFileStorage(true, TemporaryDirectory))
        {
            TestHelper(
                testSource,
                extraSources,
                modules,
                new[] { "/outputFolder:" + temp.RootDirectory, "/rootLink:BuildXL/Reference-Guide/Sdk-Documentation" },
                false,
                (success, logger, sourceFile) =>
                {
                    var errors = string.Join(Environment.NewLine, logger.CapturedDiagnostics.Select(d => d.Message));
                    Assert.True(success, "Expect to have successful run. Encountered:\r\n" + errors);
                    Assert.False(logger.HasErrors, "Expect to have no errors. Encountered:\r\n" + errors);

                    string contents = File.ReadAllText(Path.Combine(temp.RootDirectory, expectedFileName));
                    Console.Write(expectedFileName + ":");
                    Console.Write(contents);

                    if (OperatingSystemHelper.IsUnixOS)
                    {
                        // This file is saved as Windows CRLF, hence convert on Unix systems for the tests to pass
                        contents = contents.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                    }

                    Assert.Equal(expectedFileContents, contents);
                },
                true);
            }
        }
    }
}
