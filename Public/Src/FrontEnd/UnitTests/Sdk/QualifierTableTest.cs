// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Sdk
{
    public class QualifierTableTest : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        private string m_path = A("Z", "temp");

        public QualifierTableTest(ITestOutputHelper output)
            : base(output)
        {
            // Register
            RegisterEventSource(ETWLogger.Log);
        }

        [Fact]
        public void EmptyTable()
        {
            var context = FrontEndContext.CreateInstanceForTesting();
            var table = new QualifierTable(context.StringTable);
            XAssert.AreEqual("{}", ToString(table.EmptyQualifierId, table, context));
            XAssert.AreEqual("{}", ToString(table.CreateQualifierSpace(new Tuple<string, string[]>[0]), table, context));
            XAssert.AreEqual(1, table.QualifiersCount);
            XAssert.AreEqual(1, table.QualifierSpacesCount);
        }

        [Fact]
        public void MutateQualifier()
        {
            var context = FrontEndContext.CreateInstanceForTesting();
            var table = new QualifierTable(context.StringTable);
            XAssert.AreEqual(1, table.QualifiersCount, "Empty qualifier should be added by default");

            // Add simple key
            var qualifier1 = table.CreateQualifierWithValue(table.EmptyQualifierId, "BKey", "BValue");
            XAssert.AreEqual("{BKey:\"BValue\"}", ToString(qualifier1, table, context));
            XAssert.AreEqual(2, table.QualifiersCount, "Item should be added");

            // Add again key
            var qualifier2 = table.CreateQualifierWithValue(qualifier1, "BKey", "BValue");
            XAssert.AreEqual("{BKey:\"BValue\"}", ToString(qualifier2, table, context));
            XAssert.AreEqual(2, table.QualifiersCount, "Item should have been cached");

            // Add value before
            var qualifier3 = table.CreateQualifierWithValue(qualifier2, "AKey", "AValue");
            XAssert.AreEqual("{AKey:\"AValue\", BKey:\"BValue\"}", ToString(qualifier3, table, context));
            XAssert.AreEqual(3, table.QualifiersCount, "Item should be added");

            // Add value after
            var qualifier4 = table.CreateQualifierWithValue(qualifier2, "DKey", "DValue");
            XAssert.AreEqual("{BKey:\"BValue\", DKey:\"DValue\"}", ToString(qualifier4, table, context));
            XAssert.AreEqual(4, table.QualifiersCount, "Item should be added");

            // Add middle after
            var qualifier5 = table.CreateQualifierWithValue(qualifier4, "CKey", "CValue");
            XAssert.AreEqual("{BKey:\"BValue\", CKey:\"CValue\", DKey:\"DValue\"}", ToString(qualifier5, table, context));
            XAssert.AreEqual(5, table.QualifiersCount, "Item should be added");

            // Override middle value
            var qualifier6 = table.CreateQualifierWithValue(qualifier5, "CKey", "COther");
            XAssert.AreEqual("{BKey:\"BValue\", CKey:\"COther\", DKey:\"DValue\"}", ToString(qualifier6, table, context));
            XAssert.AreEqual(6, table.QualifiersCount, "Item should be added");

            // Restore middle value
            var qualifier7 = table.CreateQualifierWithValue(qualifier5, "CKey", "CValue");
            XAssert.AreEqual("{BKey:\"BValue\", CKey:\"CValue\", DKey:\"DValue\"}", ToString(qualifier7, table, context));
            XAssert.AreEqual(6, table.QualifiersCount, "Item should have been cached");
        }

        [Fact]
        public void MutateQualifierSpace()
        {
            var context = FrontEndContext.CreateInstanceForTesting();
            var table = new QualifierTable(context.StringTable);
            XAssert.AreEqual(1, table.QualifierSpacesCount, "Empty qualifier space should be added by default");

            // Add simple key
            var space1 = table.CreateQualifierSpace(new Tuple<string, string[]>("Akey", new[] { "AVal1" }));
            XAssert.AreEqual("{Akey:[\"AVal1\"]}", ToString(space1, table, context));
            XAssert.AreEqual(2, table.QualifierSpacesCount, "Item should be added");

            // Add again key
            var space2 = table.CreateQualifierSpace(new Tuple<string, string[]>("Akey", new[] { "AVal1" }));
            XAssert.AreEqual("{Akey:[\"AVal1\"]}", ToString(space2, table, context));
            XAssert.AreEqual(2, table.QualifierSpacesCount, "Item should have been cached");

            // Add same key different value
            var space3 = table.CreateQualifierSpace(new Tuple<string, string[]>("Akey", new[] { "AVal2" }));
            XAssert.AreEqual("{Akey:[\"AVal2\"]}", ToString(space3, table, context));
            XAssert.AreEqual(3, table.QualifierSpacesCount, "Item should be added");

            // Add same key all values value
            var space4 = table.CreateQualifierSpace(new Tuple<string, string[]>("Akey", new[] { "AVal1", "AVal2" }));
            XAssert.AreEqual("{Akey:[\"AVal1\", \"AVal2\"]}", ToString(space4, table, context));
            XAssert.AreEqual(4, table.QualifierSpacesCount, "Item should be added");

            // Add multiple keys and values
            var space5 = table.CreateQualifierSpace(
                new Tuple<string, string[]>("Akey", new[] { "AVal1", "AVal2", "AVal3" }),
                new Tuple<string, string[]>("BKey", new[] { "BVal1", "BVal2", "BVal3" }));
            XAssert.AreEqual("{Akey:[\"AVal1\", \"AVal2\", \"AVal3\"], BKey:[\"BVal1\", \"BVal2\", \"BVal3\"]}", ToString(space5, table, context));
            XAssert.AreEqual(5, table.QualifierSpacesCount, "Item should be added");

            // Add same keys and values but different order
            var space6 = table.CreateQualifierSpace(
                new Tuple<string, string[]>("BKey", new[] { "BVal1", "BVal3", "BVal2" }),
                new Tuple<string, string[]>("Akey", new[] { "AVal1", "AVal3", "AVal2" }));
            XAssert.AreEqual("{Akey:[\"AVal1\", \"AVal2\", \"AVal3\"], BKey:[\"BVal1\", \"BVal2\", \"BVal3\"]}", ToString(space6, table, context));
            XAssert.AreEqual(5, table.QualifierSpacesCount, "Item should have been cached");
        }

        [Fact]
        public void ScopeEmptyQualifier()
        {
            TestTryCoerceScopeToSpace(
                table => table.EmptyQualifierId,
                new Dictionary<string, string[]>(),
                "{}");

            TestTryCoerceScopeToSpace(
                table => table.EmptyQualifierId,
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} } },
                "{BKey:\"BVal2\"}");

            TestTryCoerceScopeToSpace(
                table => table.EmptyQualifierId,
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} }, { "DKey", new[] { "DVal1"} } },
                "{BKey:\"BVal2\", DKey:\"DVal1\"}");
        }

        [Fact]
        public void ScopeEmptyQualifierWithDisabledDefaults()
        {
            TestTryCoerceScopeToSpace(
                table => table.EmptyQualifierId,
                new Dictionary<string, string[]>(),
                "{}",
                expectedSuccess: true,
                useDefaultsOnCoercion: false);

            TestTryCoerceScopeToSpace(
                table => table.EmptyQualifierId,
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} } },
                "{}",
                expectedSuccess: false,
                useDefaultsOnCoercion: false);

            TestTryCoerceScopeToSpace(
                table => table.EmptyQualifierId,
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} }, { "DKey", new[] { "DVal1"} } },
                "{}",
                expectedSuccess: false,
                useDefaultsOnCoercion: false);

            SetExpectedFailures(2, 0, "The reference passes a qualifier key 'BKey' with value ''.");
        }

        [Fact]
        public void ScopeSingleValueNotInSpace()
        {
            TestTryCoerceScopeToSpace(
                table => table.CreateQualifierWithValue(table.EmptyQualifierId, "AKey", "AVal1"),
                new Dictionary<string, string[]>(),
                "{}");

            TestTryCoerceScopeToSpace(
                table => table.CreateQualifierWithValue(table.EmptyQualifierId, "AKey", "AVal1"),
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} } },
                "{BKey:\"BVal2\"}");

            TestTryCoerceScopeToSpace(
                table => table.CreateQualifierWithValue(table.EmptyQualifierId, "AKey", "AVal1"),
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} }, { "DKey", new[] { "DVal1"} } },
                "{BKey:\"BVal2\", DKey:\"DVal1\"}");
        }

        [Fact]
        public void ScopeSingleValuetInSpace()
        {
            TestTryCoerceScopeToSpace(
                table => table.CreateQualifierWithValue(table.EmptyQualifierId, "BKey", "BVal1"),
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} } },
                "{BKey:\"BVal1\"}");

            TestTryCoerceScopeToSpace(
                table => table.CreateQualifierWithValue(table.EmptyQualifierId, "BKey", "BVal1"),
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} }, { "DKey", new[] { "DVal1"} } },
                "{BKey:\"BVal1\", DKey:\"DVal1\"}");

            TestTryCoerceScopeToSpace(
                table => table.CreateQualifierWithValue(table.EmptyQualifierId, "CKey", "CVal2"),
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} }, { "DKey", new[] { "DVal1", "DVal2"} } },
                "{BKey:\"BVal2\", DKey:\"DVal1\"}");

            TestTryCoerceScopeToSpace(
                table => table.CreateQualifierWithValue(table.EmptyQualifierId, "DKey", "DVal2"),
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} }, { "DKey", new[] { "DVal1", "DVal2"} } },
                "{BKey:\"BVal2\", DKey:\"DVal2\"}");
        }

        [Fact]
        public void ScopeMultipleValues()
        {
            TestTryCoerceScopeToSpace(
                table =>
                {
                    var q = table.EmptyQualifierId;
                    q = table.CreateQualifierWithValue(q, "AKey", "AVal1");
                    q = table.CreateQualifierWithValue(q, "BKey", "BVal1");
                    q = table.CreateQualifierWithValue(q, "CKey", "CVal1");
                    q = table.CreateQualifierWithValue(q, "DKey", "DVal1");
                    q = table.CreateQualifierWithValue(q, "EKey", "EVal1");
                    return q;
                },
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal1"} }, { "DKey", new[] { "DVal1", "DVal2"} } },
                "{BKey:\"BVal1\", DKey:\"DVal1\"}");
        }

        [Fact]
        public void ScopeInvalidValue()
        {
            TestTryCoerceScopeToSpace(
                table => table.CreateQualifierWithValue(table.EmptyQualifierId, "BKey", "BVal1"),
                new Dictionary<string, string[]> { { "BKey", new[] { "BVal2", "BVal3"} } },
                null,
                false);

            SetExpectedFailures(1, 0, m_path+"(10,10)", "qualifier key 'BKey' with value 'BVal1'", "Legal values are: 'BVal2, BVal3'.");
        }

        private void TestTryCoerceScopeToSpace(
            Func<QualifierTable, QualifierId> getQualifier,
            Dictionary<string, string[]> space,
            string expectedQualifier,
            bool expectedSuccess = true,
            bool useDefaultsOnCoercion = true)
        {
            var context = FrontEndContext.CreateInstanceForTesting();
            var table = new QualifierTable(context.StringTable);

            var testQualifier = getQualifier(table);
            var testSpace = table.CreateQualifierSpace(space.Select(kvp => new Tuple<string, string[]>(kvp.Key, kvp.Value)).ToArray());

            QualifierId q;
            UnsupportedQualifierValue error;
            var result = table.TryCreateQualifierForQualifierSpace(
                context.PathTable,
                context.LoggingContext,
                testQualifier,
                testSpace,
                useDefaultsForCoercion: useDefaultsOnCoercion,
                resultingQualifierId: out q,
                error: out error);

            XAssert.AreEqual(expectedSuccess, result);

            if (result)
            {
                XAssert.AreEqual(expectedQualifier, ToString(q, table, context));
            }
            else
            {
                var location = LocationData.Create(AbsolutePath.Create(context.PathTable, m_path), 10, 10);
                Logger.Log.ErrorUnsupportedQualifierValue(
                    context.LoggingContext,
                    location.ToLogLocation(context.PathTable),
                    error.QualifierKey,
                    error.InvalidValue,
                    error.LegalValues
                );
            }
        }

        private string ToString(QualifierId qualifierId, QualifierTable qualifierTable, FrontEndContext context)
        {
            Contract.Requires(context != null);
            Contract.Requires(qualifierTable != null);

            Qualifier qualifier = qualifierTable.GetQualifier(qualifierId);
            return qualifier.ToDisplayString(context.StringTable);
        }

        private string ToString(QualifierSpaceId qualifierSpaceId, QualifierTable qualifierTable, FrontEndContext context)
        {
            Contract.Requires(context != null);
            Contract.Requires(qualifierTable != null);

            QualifierSpace qualifierSpace = qualifierTable.GetQualifierSpace(qualifierSpaceId);
            return qualifierSpace.ToDisplayString(context.StringTable);
        }
    }
}
