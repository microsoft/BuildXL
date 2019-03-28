// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Text;
using BuildXL.Utilities.Collections;
using Xunit;

namespace Test.BuildXL.Utilities.Collections
{
    public sealed class MultiValueDictionaryTests
    {
        [Fact]
        public void Empty()
        {
            var col = new MultiValueDictionary<string, string>();
            Assert.Empty(col);
            Assert.Empty(col.Values);
            Assert.Empty(col.Keys);
            Assert.Equal(string.Empty, Print(col));
        }

        [Fact]
        public void OneKeyOneValue()
        {
            var col = new MultiValueDictionary<string, string>();
            col.Add("A", "1");
            Assert.Single(col);
            Assert.Single(col.Values);
            Assert.Single(col.Keys);
            Assert.Single(col["A"]);
            Assert.Equal(@"A[1]", Print(col));
        }

        [Fact]
        public void OneKeyThreeValues()
        {
            var col = new MultiValueDictionary<string, string>();
            col.Add("A", "2");
            col.Add("A", "1");
            col.Add("A", "3");

            // Added in different order to ensure not sorted and insert-order is preserved.
            Assert.Single(col);
            Assert.Single(col.Values);
            Assert.Single(col.Keys);
            Assert.Equal(3, col["A"].Count);
            Assert.Equal(@"A[2,1,3]", Print(col));
        }

        [Fact]
        public void ThreeKeysOneValueEach()
        {
            var col = new MultiValueDictionary<string, string>();
            col.Add("A", "2");
            col.Add("B", "1");
            col.Add("C", "3");

            // Added in different order to ensure not sorted and insert-order is preserved.
            Assert.Equal(3, col.Count);
            Assert.Equal(3, col.Values.Count());
            Assert.Equal(3, col.Keys.Count());
            Assert.Single(col["A"]);
            Assert.Single(col["B"]);
            Assert.Single(col["C"]);
            Assert.Equal(@"A[2] | B[1] | C[3]", Print(col));
        }

        [Fact]
        public void ThreeKeysThreeValueEach()
        {
            var col = new MultiValueDictionary<string, string>();
            col.Add("A", "2");
            col.Add("B", "2");
            col.Add("B", "1");
            col.Add("C", "2");
            col.Add("A", "1");
            col.Add("C", "1");
            col.Add("C", "3");
            col.Add("A", "3");
            col.Add("B", "3");

            // Added in different order to ensure not sorted and insert-order is preserved.
            Assert.Equal(3, col.Count);
            Assert.Equal(3, col.Values.Count());
            Assert.Equal(3, col.Keys.Count());
            Assert.Equal(3, col["A"].Count);
            Assert.Equal(3, col["B"].Count);
            Assert.Equal(3, col["C"].Count);
            Assert.Equal(@"A[2,1,3] | B[2,1,3] | C[2,1,3]", Print(col));
        }

        [Fact]
        public void ParamsArrayAdd()
        {
            var col = new MultiValueDictionary<string, string>();
            col.Add("A", "2");
            col.Add("B", "2", "1");
            col.Add("A", "1");
            col.Add("C", "2", "1", "3");
            col.Add("A", "3");
            col.Add("B", "3");

            // Added in different order to ensure not sorted and insert-order is preserved.
            Assert.Equal(3, col.Count);
            Assert.Equal(3, col.Values.Count());
            Assert.Equal(3, col.Keys.Count());
            Assert.Equal(3, col["A"].Count);
            Assert.Equal(3, col["B"].Count);
            Assert.Equal(3, col["C"].Count);
            Assert.Equal(@"A[2,1,3] | B[2,1,3] | C[2,1,3]", Print(col));
        }

        [Fact]
        public void Comparer()
        {
            var col = new MultiValueDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            col.Add("A", "2");
            col.Add("a", "1");

            // Added in different order to ensure not sorted and insert-order is preserved.
            Assert.Equal(2, col["A"].Count);
            Assert.Equal(2, col["a"].Count);
            Assert.Equal(@"A[2,1]", Print(col));
        }

        private string Print(MultiValueDictionary<string, string> dictionary)
        {
            var builder = new StringBuilder();

            bool needLineSeparator = false;
            foreach (var kv in dictionary)
            {
                if (needLineSeparator)
                {
                    builder.Append(" | ");
                }

                builder.Append(kv.Key);
                builder.Append("[");
                bool needValueSeparator = false;
                foreach (var value in kv.Value)
                {
                    if (needValueSeparator)
                    {
                        builder.Append(",");
                    }

                    builder.Append(value);
                    needValueSeparator = true;
                }

                builder.Append("]");
                needLineSeparator = true;
            }

            return builder.ToString();
        }
    }
}
