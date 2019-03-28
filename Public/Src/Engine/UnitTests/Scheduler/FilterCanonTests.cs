// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Scheduler.Filter;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class FilterCanonTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public FilterCanonTests(ITestOutputHelper output) : base(output)
        {
            m_context = BuildXLContext.CreateInstanceForTesting();
        }

        private readonly BuildXLContext m_context;

        private PathTable PathTable => m_context.PathTable;

        private StringTable StringTable => m_context.PathTable.StringTable;

        private SymbolTable SymbolTable => m_context.SymbolTable;

        private StringId Tag(string tag)
        {
            return StringId.Create(StringTable, tag);
        }

        private FullSymbol Symbol(string symbol)
        {
            return FullSymbol.Create(SymbolTable, symbol);
        }

        private StringId ModuleName(string name)
        {
            return StringId.Create(StringTable, name);
        }

        private AbsolutePath Path(string path)
        {
            return AbsolutePath.Create(PathTable, path);
        }

        [Fact]
        public void CanonEmptyFilter()
        {
            var canon = new FilterCanonicalizer();
            var emptyFilter = new EmptyFilter();
            var canonEmptyFilter = emptyFilter.Canonicalize(canon);

            Assert.Same(EmptyFilter.Instance, canonEmptyFilter);
        }

        [Fact]
        public void CanonTagFilter()
        {
            var canon = new FilterCanonicalizer();

            var tagFilter1 = new TagFilter(Tag("A"));
            var tagFilter2 = new TagFilter(Tag("A"));
            var tagFilter3 = new TagFilter(Tag("B"));

            var canonTagFilter1 = tagFilter1.Canonicalize(canon);
            var canonTagFilter2 = tagFilter2.Canonicalize(canon);
            var canonTagFilger3 = tagFilter3.Canonicalize(canon);

            Assert.Same(canonTagFilter1, canonTagFilter2);
            Assert.NotSame(canonTagFilter1, canonTagFilger3);
        }

        [Fact]
        public void CanonValueFilter()
        {
            var canon = new FilterCanonicalizer();

            var valueFilter1 = new ValueFilter(Symbol("A.B"), true);
            var valueFilter2 = new ValueFilter(Symbol("A.B"), true);
            var valueFilter3 = new ValueFilter(Symbol("A.B"));
            var valueFilter4 = new ValueFilter(Symbol("B.C"));

            var canonValueFilter1 = valueFilter1.Canonicalize(canon);
            var canonValueFilter2 = valueFilter2.Canonicalize(canon);
            var canonValueFilter3 = valueFilter3.Canonicalize(canon);
            var canonValueFilter4 = valueFilter4.Canonicalize(canon);

            Assert.Same(canonValueFilter1, canonValueFilter2);
            Assert.NotSame(canonValueFilter1, canonValueFilter3);
            Assert.NotSame(canonValueFilter1, canonValueFilter4);
        }

        [Fact]
        public void CanonPipIdFilter()
        {
            var canon = new FilterCanonicalizer();

            var pipIdFilter1 = new PipIdFilter(0);
            var pipIdFilter2 = new PipIdFilter(0);
            var pipIdFilter3 = new PipIdFilter(1);

            var canonPipIdFilter1 = pipIdFilter1.Canonicalize(canon);
            var canonPipIdFilter2 = pipIdFilter2.Canonicalize(canon);
            var canonPipIdFilter3 = pipIdFilter3.Canonicalize(canon);

            Assert.Same(canonPipIdFilter1, canonPipIdFilter2);
            Assert.NotSame(canonPipIdFilter1, canonPipIdFilter3);
        }

        [Fact]
        public void CanonModuleFilter()
        {
            var canon = new FilterCanonicalizer();

            var moduleFilter1 = new ModuleFilter(ModuleName("A"));
            var moduleFilter2 = new ModuleFilter(ModuleName("A"));
            var moduleFilter3 = new ModuleFilter(ModuleName("B"));

            var canonModuleFilter1 = moduleFilter1.Canonicalize(canon);
            var canonModuleFilter2 = moduleFilter2.Canonicalize(canon);
            var canonModuleFilter3 = moduleFilter3.Canonicalize(canon);

            Assert.Same(canonModuleFilter1, canonModuleFilter2);
            Assert.NotSame(canonModuleFilter1, canonModuleFilter3);
        }

        [Fact]
        public void CanonInputFileFilter()
        {
            var canon = new FilterCanonicalizer();

            var inputFileFilter1 = new InputFileFilter(Path(A("C", "pathA")), null, MatchMode.FilePath, true);
            var inputFileFilter2 = new InputFileFilter(Path(A("C", "pathA")), null, MatchMode.FilePath, true);
            var inputFileFilter3 = new InputFileFilter(AbsolutePath.Invalid, "*", MatchMode.PathPrefixAndSuffixWildcard, true);
            var inputFileFilter4 = new InputFileFilter(Path(A("C", "pathB")), null, MatchMode.FilePath, true);

            var canonInputFileFilter1 = inputFileFilter1.Canonicalize(canon);
            var canonInputFileFilter2 = inputFileFilter2.Canonicalize(canon);
            var canonInputFileFilter3 = inputFileFilter3.Canonicalize(canon);
            var canonInputFileFilter4 = inputFileFilter4.Canonicalize(canon);

            Assert.Same(canonInputFileFilter1, canonInputFileFilter2);
            Assert.NotSame(canonInputFileFilter1, canonInputFileFilter3);
            Assert.NotSame(canonInputFileFilter1, canonInputFileFilter4);
        }

        [Fact]
        public void CanonOutputFileFilter()
        {
            var canon = new FilterCanonicalizer();

            var outputFileFilter1 = new OutputFileFilter(Path(A("C", "pathA")), null, MatchMode.FilePath, true);
            var outputFileFilter2 = new OutputFileFilter(Path(A("C", "pathA")), null, MatchMode.FilePath, true);
            var outputFileFilter3 = new OutputFileFilter(AbsolutePath.Invalid, "*", MatchMode.PathPrefixAndSuffixWildcard, true);
            var outputFileFilter4 = new OutputFileFilter(Path(A("C", "pathB")), null, MatchMode.FilePath, true);

            var canonOutputFileFilter1 = outputFileFilter1.Canonicalize(canon);
            var canonOutputFileFilter2 = outputFileFilter2.Canonicalize(canon);
            var canonOutputFileFilter3 = outputFileFilter3.Canonicalize(canon);
            var canonOutputFileFilter4 = outputFileFilter4.Canonicalize(canon);

            Assert.Same(canonOutputFileFilter1, canonOutputFileFilter2);
            Assert.NotSame(canonOutputFileFilter1, canonOutputFileFilter3);
            Assert.NotSame(canonOutputFileFilter1, canonOutputFileFilter4);
        }

        [Fact]
        public void CanonNegatingFilter()
        {
            var canon = new FilterCanonicalizer();

            var tagFilter1 = new TagFilter(Tag("A"));
            var tagFilter2 = new TagFilter(Tag("A"));
            var tagFilter3 = new TagFilter(Tag("B"));

            var negatingFilter1 = new NegatingFilter(tagFilter1);
            var negatingFilter2 = new NegatingFilter(tagFilter2);
            var negatingFilter3 = new NegatingFilter(tagFilter3);

            var canonNegatingFilter1 = negatingFilter1.Canonicalize(canon);
            var canonNegatingFilter2 = negatingFilter2.Canonicalize(canon);
            var canonNegatingFilter3 = negatingFilter3.Canonicalize(canon);

            Assert.Same(canonNegatingFilter1, canonNegatingFilter2);
            Assert.NotSame(canonNegatingFilter1, canonNegatingFilter3);

            var canonTagFilter = tagFilter1.Canonicalize(canon);
            var canonFilter1 = canonNegatingFilter1 as NegatingFilter;
            var canonFilter2 = canonNegatingFilter2 as NegatingFilter;

            Assert.NotNull(canonFilter1);
            Assert.NotNull(canonFilter2);
            Assert.Same(canonFilter1.Inner, canonTagFilter);
            Assert.Same(canonFilter2.Inner, canonTagFilter);
        }

        [Fact]
        public void CanonDependentsFilter()
        {
            var canon = new FilterCanonicalizer();

            var tagFilter1 = new TagFilter(Tag("A"));
            var tagFilter2 = new TagFilter(Tag("A"));
            var tagFilter3 = new TagFilter(Tag("B"));

            var dependentsFilter1 = new DependentsFilter(tagFilter1);
            var dependentsFilter2 = new DependentsFilter(tagFilter2);
            var dependentsFilter3 = new DependentsFilter(tagFilter3);

            var canonDependentsFilter1 = dependentsFilter1.Canonicalize(canon);
            var canonDependentsFilter2 = dependentsFilter2.Canonicalize(canon);
            var canonDependentsFilter3 = dependentsFilter3.Canonicalize(canon);

            Assert.Same(canonDependentsFilter1, canonDependentsFilter2);
            Assert.NotSame(canonDependentsFilter1, canonDependentsFilter3);

            var canonTagFilter = tagFilter1.Canonicalize(canon);
            var canonFilter1 = canonDependentsFilter1 as DependentsFilter;
            var canonFilter2 = canonDependentsFilter2 as DependentsFilter;

            Assert.NotNull(canonFilter1);
            Assert.NotNull(canonFilter2);
            Assert.Same(canonFilter1.Inner, canonTagFilter);
            Assert.Same(canonFilter2.Inner, canonTagFilter);
        }

        [Fact]
        public void CanonMultiTagsFilter()
        {
            var canon = new FilterCanonicalizer();

            var multiTagsFilter1 = new MultiTagsOrFilter(Tag("A"), new[] { Tag("B"), Tag("C") });
            var multiTagsFilter2 = new MultiTagsOrFilter(new[] { Tag("A"), Tag("B") }, Tag("C"));
            var multiTagsFilter3 = new MultiTagsOrFilter(Tag("A"), Tag("B"), Tag("C"));
            var multiTagsFilter4 = new MultiTagsOrFilter(Tag("A"), Tag("B"), Tag("D"));

            var canonMultiTagsFilter1 = multiTagsFilter1.Canonicalize(canon);
            var canonMultiTagsFilter2 = multiTagsFilter2.Canonicalize(canon);
            var canonMultiTagsFilter3 = multiTagsFilter3.Canonicalize(canon);
            var canonMultiTagsFilter4 = multiTagsFilter4.Canonicalize(canon);

            Assert.Same(canonMultiTagsFilter1, canonMultiTagsFilter2);
            Assert.Same(canonMultiTagsFilter1, canonMultiTagsFilter3);
            Assert.NotSame(canonMultiTagsFilter1, canonMultiTagsFilter4);
        }

        [Fact]
        public void CanonBinaryFilter()
        {
            var canon = new FilterCanonicalizer();

            var tagFilter1 = new TagFilter(Tag("A"));
            var tagFilter2 = new TagFilter(Tag("A"));
            var negatingFilter1 = new NegatingFilter(new TagFilter(Tag("B")));
            var negatingFilter2 = new NegatingFilter(new TagFilter(Tag("B")));

            var binaryFilter1 = new BinaryFilter(tagFilter1, FilterOperator.And, negatingFilter1);
            var binaryFilter2 = new BinaryFilter(tagFilter2, FilterOperator.And, negatingFilter2);
            var binaryFilter3 = new BinaryFilter(negatingFilter1, FilterOperator.And, tagFilter2);
            var binaryFilter4 = new BinaryFilter(tagFilter1, FilterOperator.Or, negatingFilter1);

            var canonBinaryFilter1 = binaryFilter1.Canonicalize(canon);
            var canonBinaryFilter2 = binaryFilter2.Canonicalize(canon);
            var canonBinaryFilter3 = binaryFilter3.Canonicalize(canon);
            var canonBinaryFilter4 = binaryFilter4.Canonicalize(canon);

            Assert.Same(canonBinaryFilter1, canonBinaryFilter2);
            Assert.Same(canonBinaryFilter1, canonBinaryFilter3);
            Assert.NotSame(canonBinaryFilter1, canonBinaryFilter4);

            var canonFilter1 = canonBinaryFilter1 as BinaryFilter;
            var canonFilter2 = canonBinaryFilter2 as BinaryFilter;
            var canonFilter3 = canonBinaryFilter3 as BinaryFilter;

            Assert.NotNull(canonFilter1);
            Assert.NotNull(canonFilter2);
            Assert.NotNull(canonFilter3);
            Assert.Same(canonFilter1.Left, canonFilter2.Left);
            Assert.Same(canonFilter1.Right, canonFilter2.Right);
            Assert.Same(canonFilter1.Left, canonFilter3.Left);
            Assert.Same(canonFilter1.Right, canonFilter3.Right);
        }

        [Fact]
        public void CanonAndSimplifyBinaryFilter()
        {
            var canon = new FilterCanonicalizer();

            var negatingFilter1 = new NegatingFilter(new TagFilter(Tag("B")));
            var negatingFilter2 = new NegatingFilter(new TagFilter(Tag("B")));

            var binaryFilter = new BinaryFilter(negatingFilter1, FilterOperator.And, negatingFilter2);
            var canonFilter = binaryFilter.Canonicalize(canon);

            Assert.Same(canonFilter, negatingFilter1.Canonicalize(canon));
        }

        [Fact]
        public void CanonBinaryOrFilterToMultiTags()
        {
            var canon = new FilterCanonicalizer();

            var tagFilter = new TagFilter(Tag("A"));
            var multiTagsFilter = new MultiTagsOrFilter(Tag("B"), Tag("C"));

            var binaryFilter = new BinaryFilter(tagFilter, FilterOperator.Or, multiTagsFilter);
            var canonFilter = binaryFilter.Canonicalize(canon);

            var expected = new MultiTagsOrFilter(Tag("A"), Tag("B"), Tag("C")).Canonicalize(canon);

            Assert.Same(expected, canonFilter);
        }

        [Fact]
        public void CanonBinaryOrFilterPushTagToLeft()
        {
            var canon = new FilterCanonicalizer();

            var tagFilter = new TagFilter(Tag("D"));
            var binaryFilterLeft = new BinaryFilter(new TagFilter(Tag("B")), FilterOperator.And, new TagFilter(Tag("C")));
            var binaryFilter = new BinaryFilter(binaryFilterLeft, FilterOperator.Or, tagFilter);
            var canonFilter = binaryFilter.Canonicalize(canon);

            var expected = new BinaryFilter(tagFilter, FilterOperator.Or, binaryFilterLeft).Canonicalize(canon);
            Assert.Same(expected, canonFilter);
        }

        [Fact]
        public void CanonBinaryOrFilterPushTagToLeftAndJoinAsMultiTags()
        {
            var canon = new FilterCanonicalizer();

            var tagFilterA = new TagFilter(Tag("A"));
            var tagFilterD = new TagFilter(Tag("D"));
            var binaryFilterLeft = new BinaryFilter(new TagFilter(Tag("B")), FilterOperator.And, new TagFilter(Tag("C")));
            var innerbinaryFilter = new BinaryFilter(binaryFilterLeft, FilterOperator.Or, tagFilterD);
            var outerBinaryFilter = new BinaryFilter(tagFilterA, FilterOperator.Or, innerbinaryFilter);
            var canonFilter = outerBinaryFilter.Canonicalize(canon);

            var expected = new BinaryFilter(new MultiTagsOrFilter(Tag("A"), Tag("D")), FilterOperator.Or, binaryFilterLeft).Canonicalize(canon);
            Assert.Same(expected, canonFilter);
        }

        [Fact]
        public void CanonManyFiltersReduce()
        {
            var canon = new FilterCanonicalizer();

            int filterCount = 1000;
            List<PipFilter> pipFilters = new List<PipFilter>();
            List<StringId> modules = new List<StringId>();
            List<StringId> tags = new List<StringId>();

            var binaryFilterLeft = new BinaryFilter(new TagFilter(Tag("B")), FilterOperator.And, new TagFilter(Tag("C")));
            pipFilters.Add(binaryFilterLeft);

            for (int i = 0; i < filterCount; i++)
            {
                tags.Add(Tag(i.ToString()));
                pipFilters.Add(new TagFilter(Tag(i.ToString())));

                modules.Add(ModuleName(i.ToString()));
                pipFilters.Add(new ModuleFilter(ModuleName(i.ToString())));
            }

            var random = new Random();
            while (pipFilters.Count > 1)
            {
                var lastIndex = pipFilters.Count - 1;
                var randomIndex = random.Next(0, lastIndex);

                var randomFilter = pipFilters[randomIndex];
                var lastFilter = pipFilters[lastIndex];

                pipFilters[randomIndex] = new BinaryFilter(randomFilter, FilterOperator.Or, lastFilter);
                pipFilters.RemoveAt(lastIndex);
            }

            var finalFilter = pipFilters[0];
            var canonFilter = finalFilter.Canonicalize(canon);

            BinaryFilter binaryFilter = (BinaryFilter)canonFilter;

            var childFilters = new HashSet<PipFilter>(binaryFilter.TraverseBinaryChildFilters(bf => bf.FilterOperator == FilterOperator.Or)
                .Where(pf => !(pf is BinaryFilter bf && bf.FilterOperator == FilterOperator.Or)));

            var expectedFilters = new[]
            {
                binaryFilterLeft.Canonicalize(canon),
                new MultiTagsOrFilter(tags.ToArray()).Canonicalize(canon),
                new ModuleFilter(modules.ToArray()).Canonicalize(canon)
            };

            Assert.True(childFilters.SetEquals(expectedFilters));
        }
    }
}
