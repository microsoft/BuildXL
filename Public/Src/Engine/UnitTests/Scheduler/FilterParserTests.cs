// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using BuildXL.Scheduler.Filter;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class FilterParserTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        private readonly string DummyMountPath = (OperatingSystemHelper.IsUnixOS ? "" : $"{Path.DirectorySeparatorChar}") + $"{Path.DirectorySeparatorChar}dummy{Path.DirectorySeparatorChar}path";

        private readonly BuildXLContext m_context;

        private StringTable StringTable => m_context.PathTable.StringTable;
        private PathTable PathTable => m_context.PathTable;
        private SymbolTable SymbolTable => m_context.SymbolTable;

        public FilterParserTests(ITestOutputHelper output) : base(output)
        {
            m_context = BuildXLContext.CreateInstanceForTesting();
        }

        [Fact]
        public void NullFilter()
        {
            RunNegativeFilterParserTest(null, 0, "Cannot parse a null or empty filter");
        }

        [Fact]
        public void EmptyFilter()
        {
            RunNegativeFilterParserTest(null, 0, "Cannot parse a null or empty filter");
        }

        [Fact]
        public void UnknownFilter()
        {
            RunNegativeFilterParserTest("notafilter=\'doesnt matter\'", 0, "Unknown");
        }

        [Fact]
        public void DependentsSelected()
        {
            RootFilter expected = new RootFilter(
                new DependentsFilter(
                    new TagFilter(StringId.Create(StringTable, "Test"))));

            RunPositiveFilterParserTest("+tag=\'Test\'", expected);
        }

        [Fact]
        public void ExtraTextAfterFilter()
        {
            RunNegativeFilterParserTest("tag='foo'asdf", 9, null);
        }

        [Fact]
        public void UnepxectedEndOfFilter()
        {
            RunNegativeFilterParserTest("(tag='foo'asdfand", 10, null);
        }

        [Fact]
        public void SingleFilterNegated()
        {
            RootFilter expected = new RootFilter(
                new TagFilter(StringId.Create(StringTable, "Test")).Negate());

            RunPositiveFilterParserTest("~(tag=\'Test\')", expected);
        }

        [Fact]
        public void SingleFilterDoubleNegated()
        {
            RootFilter expected = new RootFilter(
                new TagFilter(StringId.Create(StringTable, "Test")));

            RunPositiveFilterParserTest("~(~(tag=\'Test\'))", expected);
        }

        [Fact]
        public void SingleFilterDependents()
        {
            RootFilter expected = new RootFilter(
                new DependentsFilter(new TagFilter(StringId.Create(StringTable, "Test"))));

            RunPositiveFilterParserTest("dpt(tag=\'Test\')", expected);
        }

        [Fact]
        public void SingleFilterDependencies()
        {
            RootFilter expected = new RootFilter(
                new DependenciesFilter(new TagFilter(StringId.Create(StringTable, "Test"))));
            
            RunPositiveFilterParserTest("dpc(tag=\'Test\')", expected);
        }

        [Fact]
        public void SingleFilterRequiredInputs()
        {
            RootFilter expected = new RootFilter(
                new DependenciesFilter(new TagFilter(StringId.Create(StringTable, "Test")), ClosureMode.DirectExcludingSelf));

            RunPositiveFilterParserTest("requiredfor(tag=\'Test\')", expected);
        }

        [Fact]
        public void SingleFilterCopyDependents()
        {
            RootFilter expected = new RootFilter(
                new CopyDependentsFilter(new TagFilter(StringId.Create(StringTable, "Test"))));

            RunPositiveFilterParserTest("copydpt(tag=\'Test\')", expected);
        }

        [Fact]
        public void NegationOnlySupportedOutsideParen()
        {
            RunNegativeFilterParserTest("~tag=\'Test\'", 1, "Negation may only be used on an expression surrounded by ( ).");
        }

        [Fact]
        public void PlusOnlySupportedOnGroupExpression()
        {
            RunNegativeFilterParserTest("~(tag='Test') and +input='OffsetHelpers.h'", 18, "The + operator may only be used as the outer operator and not nested inside expression.");
        }

        [Fact]
        public void PlusOnlySupportedOnTheOuterExpressionWithSpaceAndParens()
        {
            RunNegativeFilterParserTest("~(tag='Test') and + ( input='OffsetHelpers.h' )", 18, "The + operator may only be used as the outer operator and not nested inside expression.");
        }

        [Fact]
        public void HelpTextExampleIsCorrect()
        {
            // Checking that an example from HelpText.cs is valid
            RootFilter expected = new RootFilter(
                new BinaryFilter(
                    new TagFilter(StringId.Create(StringTable, "csc.exe")),
                    FilterOperator.And,
                    new TagFilter(StringId.Create(StringTable, "test")).Negate()));

            RunPositiveFilterParserTest("(tag='csc.exe'and~(tag='test'))", expected);
        }

        [Fact]
        public void FilterWithWhitespace()
        {
            RootFilter expected = new RootFilter(
                new DependentsFilter(
                    new BinaryFilter(
                        new TagFilter(StringId.Create(StringTable, "Test")), FilterOperator.And,
                        new TagFilter(StringId.Create(StringTable, "csc.exe")))));

            RunPositiveFilterParserTest(" + ( tag = \'Test\' and tag = \'csc.exe\' )", expected);
        }

        /// <summary>
        /// Make sure we don't get a stack overflow or anything nasty
        /// </summary>
        [Fact]
        public void VeryLongFilter()
        {
            StringBuilder longFilter = new StringBuilder();
            int length = 5000;
            for (int i = 0; i < length; i++)
            {
                if (i == length - 1)
                {
                    longFilter.AppendFormat(@" input='asdf{0}'", i);
                }
                else
                {
                    longFilter.AppendFormat(@" input='asdf{0}' or", i);
                }
            }

            FilterParser filterParser = new FilterParser(m_context, TryGetPathByMountName, longFilter.ToString());
            RootFilter rootFilter;
            FilterParserError error;
            XAssert.IsTrue(filterParser.TryParse(out rootFilter, out error));

            var statistics = rootFilter.GetStatistics();
            XAssert.AreEqual(length, statistics.InputFileFilterCount);
            XAssert.AreEqual(length - 1, statistics.BinaryFilterCount);
        }

        #region Filter specific tests
        [Fact]
        public void TagFilter()
        {
            RootFilter expected = new RootFilter(
                new TagFilter(StringId.Create(StringTable, "Test")));

            RunPositiveFilterParserTest("tag=\'Test\'", expected);
        }

        [Fact]
        public void CompoundFilter()
        {
            RootFilter expected = new RootFilter(
                new BinaryFilter(
                    new TagFilter(StringId.Create(StringTable, "Test")),
                    FilterOperator.And,
                    new TagFilter(StringId.Create(StringTable, "csc.exe"))));

            RunPositiveFilterParserTest("(tag=\'Test\'andtag=\'csc.exe\')", expected);
        }

        [Fact]
        public void CompoundFilterPrecedence()
        {
            // And has greater precedence than or.
            RootFilter expected = new RootFilter(
                new BinaryFilter(
                    new BinaryFilter(
                        new TagFilter(StringId.Create(StringTable, "Test")),
                        FilterOperator.And,
                        new TagFilter(StringId.Create(StringTable, "csc.exe"))),
                    FilterOperator.Or,
                    new TagFilter(StringId.Create(StringTable, "Test2"))));

            RunPositiveFilterParserTest("tag=\'Test\'andtag=\'csc.exe\'ortag=\'Test2\'", expected);

            RootFilter expected2 = new RootFilter(
                new BinaryFilter(
                    new TagFilter(StringId.Create(StringTable, "Test2")),
                    FilterOperator.Or,
                    new BinaryFilter(
                        new TagFilter(StringId.Create(StringTable, "Test")),
                        FilterOperator.And,
                        new TagFilter(StringId.Create(StringTable, "csc.exe")))));

            RunPositiveFilterParserTest("tag=\'Test2\'ortag=\'csc.exe\'andtag=\'Test\'", expected2);
        }

        [Fact]
        public void NestedBinaryFilter()
        {
            RootFilter expected = new RootFilter(
                new DependentsFilter(
                    new BinaryFilter(
                        new BinaryFilter(
                            new TagFilter(StringId.Create(StringTable, "Test1")),
                            FilterOperator.And,
                            new TagFilter(StringId.Create(StringTable, "Test2"))),
                        FilterOperator.And,
                        new TagFilter(StringId.Create(StringTable, "Test3")))));

            RunPositiveFilterParserTest(" + (  ( tag = \'Test1\' and tag = \'Test2\' ) and tag = \'Test3\' )", expected);

            expected = new RootFilter(
                new DependentsFilter(
                    new BinaryFilter(
                        new TagFilter(StringId.Create(StringTable, "Test1")),
                        FilterOperator.And,
                        new BinaryFilter(
                            new TagFilter(StringId.Create(StringTable, "Test2")),
                            FilterOperator.And,
                            new TagFilter(StringId.Create(StringTable, "Test3"))))));

            RunPositiveFilterParserTest(" + (  tag = \'Test1\' and ( tag = \'Test2\' and tag = \'Test3\' ) )", expected);
        }

        [Fact]
        public void NestedDependentBinaryFilter()
        {
            RootFilter expected = new RootFilter(
                new BinaryFilter(
                    new DependentsFilter(
                        new BinaryFilter(
                            new TagFilter(StringId.Create(StringTable, "Test1")),
                            FilterOperator.And,
                            new TagFilter(StringId.Create(StringTable, "Test2")))),
                    FilterOperator.And,
                    new TagFilter(StringId.Create(StringTable, "Test3"))));

            RunPositiveFilterParserTest(" (  dpt( tag = \'Test1\' and tag = \'Test2\' ) and tag = \'Test3\' )", expected);

            expected = new RootFilter(
                new BinaryFilter(
                    new TagFilter(StringId.Create(StringTable, "Test1")),
                    FilterOperator.And,
                    new DependentsFilter(
                        new BinaryFilter(
                            new TagFilter(StringId.Create(StringTable, "Test2")),
                            FilterOperator.And,
                            new TagFilter(StringId.Create(StringTable, "Test3"))))));

            RunPositiveFilterParserTest(" (  tag = \'Test1\' and dpt( tag = \'Test2\' and tag = \'Test3\' ) )", expected);
        }

        [Fact]
        public void NestedCopyDependentBinaryFilter()
        {
            RootFilter expected = new RootFilter(
                new BinaryFilter(
                    new CopyDependentsFilter(
                        new BinaryFilter(
                            new TagFilter(StringId.Create(StringTable, "Test1")),
                            FilterOperator.And,
                            new TagFilter(StringId.Create(StringTable, "Test2")))),
                    FilterOperator.And,
                    new TagFilter(StringId.Create(StringTable, "Test3"))));

            RunPositiveFilterParserTest(" (  copydpt( tag = \'Test1\' and tag = \'Test2\' ) and tag = \'Test3\' )", expected);

            expected = new RootFilter(
                new BinaryFilter(
                    new TagFilter(StringId.Create(StringTable, "Test1")),
                    FilterOperator.And,
                    new CopyDependentsFilter(
                        new BinaryFilter(
                            new TagFilter(StringId.Create(StringTable, "Test2")),
                            FilterOperator.And,
                            new TagFilter(StringId.Create(StringTable, "Test3"))))));

            RunPositiveFilterParserTest(" (  tag = \'Test1\' and copydpt( tag = \'Test2\' and tag = \'Test3\' ) )", expected);
        }

        [Fact]
        public void NestedDependencyBinaryFilter()
        {
            RootFilter expected = new RootFilter(
                new BinaryFilter(
                    new DependenciesFilter(
                        new BinaryFilter(
                            new TagFilter(StringId.Create(StringTable, "Test1")),
                            FilterOperator.And,
                            new TagFilter(StringId.Create(StringTable, "Test2")))),
                    FilterOperator.And,
                    new TagFilter(StringId.Create(StringTable, "Test3"))));

            RunPositiveFilterParserTest(" (  dpc( tag = \'Test1\' and tag = \'Test2\' ) and tag = \'Test3\' )", expected);

            expected = new RootFilter(
                new BinaryFilter(
                    new TagFilter(StringId.Create(StringTable, "Test1")),
                    FilterOperator.And,
                    new DependenciesFilter(
                        new BinaryFilter(
                            new TagFilter(StringId.Create(StringTable, "Test2")),
                            FilterOperator.And,
                            new TagFilter(StringId.Create(StringTable, "Test3"))))));

            RunPositiveFilterParserTest(" (  tag = \'Test1\' and dpc( tag = \'Test2\' and tag = \'Test3\' ) )", expected);
        }

        [Fact]
        public void PipIdFilter()
        {
            RootFilter expected = new RootFilter(
                new PipIdFilter(-8826251658372796143));

            RunPositiveFilterParserTest("id=\'8582DAE1547CB111'", expected);
        }

        [Fact]
        public void PipIdWithPrefixFilter()
        {
            RootFilter expected = new RootFilter(
                new PipIdFilter(-8826251658372796143));

            RunPositiveFilterParserTest("id=\'Pip8582DAE1547CB111'", expected);
        }

        [Fact]
        public void PipIdFilterBadId()
        {
            // Bad hex value - contains 'J'
            RunNegativeFilterParserTest("id=\'8582DAE1547CB11J'", 4, "Failed to parse pip id");
        }

        [Fact]
        public void SpecFilterAbsolute()
        {
            var path = X("/c/users/John Doe/src/foo.ds");
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Create(m_context.PathTable, path)));

            RunPositiveFilterParserTest($"spec=\'{path}'", expected);
        }

        [Fact]
        public void SpecFilterDependenciesAbsolute()
        {
            var path = X("/c/users/John Doe/src/foo.ds");
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Create(m_context.PathTable, path), specDependencies: true));

            RunPositiveFilterParserTest($"specref=\'{path}\'", expected);
        }

        [Fact]
        public void SpecFilterModule()
        {
            RootFilter expected = new RootFilter(
                CreateModuleFilter(StringId.Create(m_context.StringTable, @"testModule")));

            RunPositiveFilterParserTest("module=\'testModule\'", expected);
        }

        [Fact]
        public void SpecFilterModuleDottedName()
        {
            RootFilter expected = new RootFilter(
                CreateModuleFilter(StringId.Create(m_context.StringTable, @"My.Test.Module")));

            RunPositiveFilterParserTest("module=\'My.Test.Module\'", expected);
        }

        private static SpecFileFilter CreateSpecFilter(AbsolutePath path,
            string pathWildcard = null,
            MatchMode matchMode = MatchMode.FilePath,
            bool pathFromMount = false,
            bool valueTransitive = false,
            bool specDependencies = false)
        {
            return new SpecFileFilter(path, pathWildcard, matchMode, pathFromMount, valueTransitive, specDependencies);
        }

        private static ModuleFilter CreateModuleFilter(StringId moduleIdentity)
        {
            return new ModuleFilter(moduleIdentity);
        }

        [Fact]
        public void SpecFilterRelative()
        {
            var path = $"src{Path.DirectorySeparatorChar}foo.ds";
            string fullPath = Path.GetFullPath(path);

            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Create(m_context.PathTable, fullPath)));

            RunPositiveFilterParserTest($"spec=\'{path}\'", expected);
        }

        // Constructing an invalid path on Unix systems within a string is quite hard
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void SpecFilterInvalidPath()
        {
            // Invalid character in path
            var errMsg = "Failed"; // it doesn't matter what the error message is, what matters is the error position.
            RunNegativeFilterParserTest("spec=\'c:\\users?\\John Doe\\src\\foo.ds\'", 6, errMsg);
            RunNegativeFilterParserTest("spec=\'c:\\users\\c:\\John Doe\\src\\foo.ds\'", 6, errMsg);
        }

        [Fact]
        public void SpecFilterCurrentDirectory()
        {
            var path = X("/c/users/John Doe/src");
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Create(m_context.PathTable, path), null, MatchMode.WithinDirectory));

            RunPositiveFilterParserTest($"spec=\'{path}{Path.DirectorySeparatorChar}.'", expected);
        }

        [Fact]
        public void SpecFilterRecursiveSubdirectories()
        {
            var path = X("/c/users/John Doe/src");
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Create(m_context.PathTable, path), null, MatchMode.WithinDirectoryAndSubdirectories));

            RootFilter actual = RunPositiveFilterParserTest($"spec=\'{path}{Path.DirectorySeparatorChar}*'", expected);
            XAssert.AreEqual(1, actual.GetEvaluationFilter(SymbolTable, PathTable).ValueDefinitionRootsToResolve.Count);
            XAssert.AreEqual(path, actual.GetEvaluationFilter(SymbolTable, PathTable).ValueDefinitionRootsToResolve[0].ToString(m_context.PathTable));
        }

        [Fact]
        public void SpecFilterFilename()
        {
            var path = $"{Path.DirectorySeparatorChar}filename.ds";
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Invalid, matchMode: MatchMode.PathPrefixWildcard, pathWildcard:path));

            RootFilter actual = RunPositiveFilterParserTest($"spec=\'*{path}'", expected);
            XAssert.AreEqual(0, actual.GetEvaluationFilter(SymbolTable, PathTable).ValueDefinitionRootsToResolve.Count);
        }

        [Fact]
        public void SpecFilterFilenamePrefixWildcard()
        {
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Invalid, ".ds", MatchMode.PathPrefixWildcard));

            RootFilter actual = RunPositiveFilterParserTest("spec=\'*.ds'", expected);
            XAssert.AreEqual(0, actual.GetEvaluationFilter(SymbolTable, PathTable).ValueDefinitionRootsToResolve.Count);
        }

        [Fact]
        public void SpecFilterFilenameSuffixWildcard()
        {
            var path = X("/c/src/BuildXL.");
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Invalid, path, MatchMode.PathSuffixWildcard));
            RootFilter actual = RunPositiveFilterParserTest($"spec='{path}*'", expected);
            XAssert.AreEqual(0, actual.GetEvaluationFilter(SymbolTable, PathTable).ValueDefinitionRootsToResolve.Count);
        }

        [Fact]
        public void SpecFilterFilenamePrefixAndSuffixWildcard()
        {
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Invalid, $"src{Path.DirectorySeparatorChar}BuildXL.", MatchMode.PathPrefixAndSuffixWildcard));
            RootFilter actual = RunPositiveFilterParserTest($"spec='*src{Path.DirectorySeparatorChar}BuildXL.*'", expected);
            XAssert.AreEqual(0, actual.GetEvaluationFilter(SymbolTable, PathTable).ValueDefinitionRootsToResolve.Count);
        }

        [Fact]
        public void SpecFilterEmptyFilenamePrefixAndSuffixWildcard()
        {
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Invalid, Path.DirectorySeparatorChar.ToString(), MatchMode.PathPrefixAndSuffixWildcard));
            RootFilter actual = RunPositiveFilterParserTest("spec='*'", expected);
        }

        [Fact]
        public void SpecFilterFilenamePrefixAndSuffixWildcardWithoutAnyNonWildcardCharacters()
        {
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Invalid, Path.DirectorySeparatorChar.ToString(), MatchMode.PathPrefixAndSuffixWildcard));
            RootFilter actual = RunPositiveFilterParserTest("spec='**'", expected);
            XAssert.AreEqual(0, actual.GetEvaluationFilter(SymbolTable, PathTable).ValueDefinitionRootsToResolve.Count);
        }

        [Fact]
        public void SpecFilterInvalidWildcard()
        {
            RunNegativeFilterParserTest("spec=\'BuildXL.*.dll'", 6, "may only contain wildcards at the beginning or end of the string");
        }

        [Fact]
        public void FilterPathMatches()
        {
            var filter = CreateSpecFilter(AbsolutePath.Invalid, $"{Path.DirectorySeparatorChar}foo.dll", MatchMode.PathPrefixWildcard);
            XAssert.IsTrue(filter.PathMatches(AbsolutePath.Create(m_context.PathTable, X("/c/src/blah/foo.dll")), m_context.PathTable));
            XAssert.IsFalse(filter.PathMatches(AbsolutePath.Create(m_context.PathTable, X("/c/src/blah/bar.dll")), m_context.PathTable));

            var filter2 = CreateSpecFilter(AbsolutePath.Invalid, X("/c/src"), MatchMode.PathSuffixWildcard);
            XAssert.IsTrue(filter2.PathMatches(AbsolutePath.Create(m_context.PathTable, X("/c/src/blah/foo.dll")), m_context.PathTable));
            XAssert.IsFalse(filter2.PathMatches(AbsolutePath.Create(m_context.PathTable, X("/c/notSource/blah/bar.dll")), m_context.PathTable));

            var filter3 = CreateSpecFilter(AbsolutePath.Invalid, $"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", MatchMode.PathPrefixAndSuffixWildcard);
            XAssert.IsTrue(filter3.PathMatches(AbsolutePath.Create(m_context.PathTable, X("/c/src/blah/foo.dll")), m_context.PathTable));
            XAssert.IsFalse(filter3.PathMatches(AbsolutePath.Create(m_context.PathTable, X("/c/notSource/blah/bar.dll")), m_context.PathTable));
        }

        [Fact]
        public void FilterWildcardExpansion()
        {
            var currentDirectory = Environment.CurrentDirectory;
            var filter = Deserialize($"spec='src{Path.DirectorySeparatorChar}blah*'");
            XAssert.IsTrue(((SpecFileFilter)filter.PipFilter).PathMatches(AbsolutePath.Create(m_context.PathTable, currentDirectory + $"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}blah{Path.DirectorySeparatorChar}foo.dll"), m_context.PathTable));
        }

        [Fact]
        public void SpecFilterFromMount()
        {
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Create(m_context.PathTable, DummyMountPath), null, MatchMode.WithinDirectoryAndSubdirectories, pathFromMount: true));

            RunPositiveFilterParserTest("spec='Mount[DummyMount]'", expected);
        }

        [Fact]
        public void SpecFilterFromMountTrailingWildcard()
        {
            RootFilter expected = new RootFilter(
                CreateSpecFilter(AbsolutePath.Create(m_context.PathTable, DummyMountPath + $"{Path.DirectorySeparatorChar}hello"), null, MatchMode.WithinDirectoryAndSubdirectories, pathFromMount: true));

            RunPositiveFilterParserTest($"spec='Mount[DummyMount]{Path.DirectorySeparatorChar}hello{Path.DirectorySeparatorChar}*'", expected);
        }

        [Fact]
        public void OutputFilterRecursivePath()
        {
            var path = X("/c/users/John Doe/src");
            RootFilter expected = new RootFilter(
                new OutputFileFilter(AbsolutePath.Create(m_context.PathTable, path), null, MatchMode.WithinDirectoryAndSubdirectories, pathFromMount: false));

            RunPositiveFilterParserTest($"output=\'{path}{Path.DirectorySeparatorChar}*'", expected);
        }

        [Fact]
        public void OutputFilterFileName()
        {
            RootFilter expected = new RootFilter(
                new OutputFileFilter(AbsolutePath.Invalid, $"{Path.DirectorySeparatorChar}myoutput.dll", MatchMode.PathPrefixWildcard, pathFromMount: false));

            RunPositiveFilterParserTest($"output='*{Path.DirectorySeparatorChar}myoutput.dll'", expected);
        }

        [Fact]
        public void OutputFilterFromMount()
        {
            RootFilter expected = new RootFilter(
                new OutputFileFilter(AbsolutePath.Create(m_context.PathTable, DummyMountPath), null, MatchMode.WithinDirectoryAndSubdirectories, pathFromMount: true));

            RunPositiveFilterParserTest("output='Mount[DummyMount]'", expected);
            RunNegativeFilterParserTest("output='MOUNT[DUMMYMOUNT'", 24, "end of a named mount");
            RunNegativeFilterParserTest("output='mount[badMountName]'", 14, "Could not find mount");
            RunNegativeFilterParserTest("output='mount[)(@!&%]'", 14, "Could not find mount");
        }

        [Fact]
        public void ValueFilter()
        {
            FullSymbol identifier = FullSymbol.Create(m_context.SymbolTable, "BuildXL.Transformers.dll");
            RootFilter expected = new RootFilter(new ValueFilter(identifier));
            RunPositiveFilterParserTest("value=\'BuildXL.Transformers.dll'", expected);
        }

        [Fact]
        public void ValueFilterCannotBeParsed()
        {
            RunNegativeFilterParserTest("value=\'@#$%'", 7, "Failed to parse value identifier");
        }

        [Fact]
        public void ValueShortCircuit()
        {
            RootFilter filter = Deserialize("tag='foo'");
            XAssert.AreEqual(0, filter.GetEvaluationFilter(SymbolTable, PathTable).ValueNamesToResolve.Count);

            filter = Deserialize("value='foo'");
            XAssert.AreEqual(1, filter.GetEvaluationFilter(SymbolTable, PathTable).ValueNamesToResolve.Count);

            // We may short circuit on value when there is an and
            filter = Deserialize("(value='foo' and tag='bar')");
            XAssert.AreEqual(1, filter.GetEvaluationFilter(SymbolTable, PathTable).ValueNamesToResolve.Count);

            // We may not short circuit on value when there is an "or" without both sides returning a set of values to filter by
            filter = Deserialize("(value='foo' or tag='bar')");
            XAssert.AreEqual(0, filter.GetEvaluationFilter(SymbolTable, PathTable).ValueNamesToResolve.Count);

            // We can short circuit an "or" when both return a value
            filter = Deserialize("(value='foo' or (value='bar' or value='bar2'))");
            XAssert.AreEqual(3, filter.GetEvaluationFilter(SymbolTable, PathTable).ValueNamesToResolve.Count);

            // When both sides of an "and" return values to evaluate we only need to evaluate one side
            filter = Deserialize("(value='foo' and (value='bar' or value='bar2'))");
            XAssert.AreEqual(1, filter.GetEvaluationFilter(SymbolTable, PathTable).ValueNamesToResolve.Count);

            // A negated value filter must cause all values to evaluate
            filter = Deserialize("~(value='bar')");
            XAssert.AreEqual(0, filter.GetEvaluationFilter(SymbolTable, PathTable).ValueNamesToResolve.Count);
            filter = Deserialize("(value='bar' or ~(value='bar2'))");
            XAssert.AreEqual(0, filter.GetEvaluationFilter(SymbolTable, PathTable).ValueNamesToResolve.Count);
            filter = Deserialize("~(value='bar' and value='bar2')");
            XAssert.AreEqual(0, filter.GetEvaluationFilter(SymbolTable, PathTable).ValueNamesToResolve.Count);

            // The left value filter may still cause short circuiting with a negated value filter on the right because the operator is "and"
            filter = Deserialize("(value='bar' and ~(value='bar2'))");
            XAssert.AreEqual(1, filter.GetEvaluationFilter(SymbolTable, PathTable).ValueNamesToResolve.Count);
        }

        [Fact]
        public void SourceFilter()
        {
            RootFilter expected = new RootFilter(
                new OutputFileFilter(AbsolutePath.Invalid, $"{Path.DirectorySeparatorChar}Program.cs", MatchMode.PathPrefixWildcard, pathFromMount: false));

            RunPositiveFilterParserTest($"input='*{Path.DirectorySeparatorChar}Program.cs'", expected);
        }

        #endregion

        private RootFilter Deserialize(string inputString)
        {
            RootFilter result;
            FilterParserError errorString;
            FilterParser filterParser = new FilterParser(m_context, TryGetPathByMountName, inputString);
            if (!filterParser.TryParse(out result, out errorString))
            {
                XAssert.Fail(errorString.Message + " At:" + errorString.Position);
            }

            XAssert.AreEqual(errorString, null);
            return result;
        }

        private RootFilter RunPositiveFilterParserTest(string inputString, RootFilter expected)
        {
            RootFilter result = Deserialize(inputString);
            XAssert.AreEqual(expected.GetHashCode(), result.GetHashCode());
            return result;
        }

        private void RunNegativeFilterParserTest(string inputString, int expectedErrorPosition, string expectedError)
        {
            RootFilter result;
            FilterParserError error;
            FilterParser filterParser = new FilterParser(m_context, TryGetPathByMountName, inputString);
            XAssert.IsFalse(filterParser.TryParse(out result, out error));
            XAssert.AreEqual(expectedErrorPosition, error.Position);
            if (expectedError != null && !error.Message.Contains(expectedError))
            {
                XAssert.Fail("Error messages do not match: expected: '{0}' actual: '{1}'", expectedError, error.Message);
            }
        }

        private bool TryGetPathByMountName(string mountName, out AbsolutePath path)
        {
            if (mountName.Equals("DummyMount", System.StringComparison.OrdinalIgnoreCase))
            {
                path = AbsolutePath.Create(m_context.PathTable, DummyMountPath);
                return true;
            }

            path = AbsolutePath.Invalid;
            return false;
        }
    }
}
