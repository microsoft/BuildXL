// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

using BuildXL;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL
{
    public class TranslateDirectoryParseTests
    {
        private PathTable m_pathTable = new PathTable();
        private readonly string m_helloTestPath = A("c", "Hello");
        private readonly string m_worldTestPath = A("c", "World");
        private readonly string m_bigTestPath = A("c", "Big");

        [Fact]
        public void LessThan0()
        {
            CommandLineUtilities.Option option = default(CommandLineUtilities.Option);
            option.Name = "translateDirectory";
            option.Value = "<Hello";

            try
            {
                Args.ParseTranslatePathOption(m_pathTable, option);
            }
            catch (Exception e)
            {
                XAssert.AreEqual(
                    "The value '<Hello' provided for the /translateDirectory argument is invalid. It can't start with a '<' separator",
                    e.GetLogEventMessage());
                return;
            }

            XAssert.Fail("Should have gotten an exception.");
        }

        [Fact]
        public void Null()
        {
            CommandLineUtilities.Option option = default(CommandLineUtilities.Option);
            option.Name = "translateDirectory";
            option.Value = null;

            try
            {
                Args.ParseTranslatePathOption(m_pathTable, option);
            }
            catch (Exception e)
            {
                XAssert.AreEqual(
                    "The value provided for the /translateDirectory argument is invalid.",
                    e.GetLogEventMessage());
                return;
            }

            XAssert.Fail("Should have gotten an exception.");
        }

        [Fact]
        public void Empty()
        {
            CommandLineUtilities.Option option = default(CommandLineUtilities.Option);
            option.Name = "translateDirectory";
            option.Value = string.Empty;

            try
            {
                Args.ParseTranslatePathOption(m_pathTable, option);
            }
            catch (Exception e)
            {
                XAssert.AreEqual(
                    "The value provided for the /translateDirectory argument is invalid.",
                    e.GetLogEventMessage());
                return;
            }

            XAssert.Fail("Should have gotten an exception.");
        }

        [Fact]
        public void NoLessThanNoDblCol()
        {
            CommandLineUtilities.Option option = default(CommandLineUtilities.Option);
            option.Name = "translateDirectory";
            option.Value = "Hello";

            try
            {
                Args.ParseTranslatePathOption(m_pathTable, option);
            }
            catch (Exception e)
            {
                XAssert.AreEqual(
                    "The value 'Hello' provided for the /translateDirectory argument is invalid. It should contain a '::' or '<' separator",
                    e.GetLogEventMessage());
                return;
            }

            XAssert.Fail("Should have gotten an exception.");
        }

        [Fact]
        public void DblCol0()
        {
            CommandLineUtilities.Option option = default(CommandLineUtilities.Option);
            option.Name = "translateDirectory";
            option.Value = "::Hello";

            try
            {
                Args.ParseTranslatePathOption(m_pathTable, option);
            }
            catch (Exception e)
            {
                XAssert.AreEqual(
                    "The value '::Hello' provided for the /translateDirectory argument is invalid. It can't start with a '::' separator",
                    e.GetLogEventMessage());
                return;
            }

            XAssert.Fail("Should have gotten an exception.");
        }

        [Fact]
        public void LessThanSeparator()
        {
            CommandLineUtilities.Option option = default(CommandLineUtilities.Option);
            option.Name = "translateDirectory";
            option.Value =  m_helloTestPath + "<" + m_worldTestPath;

            TranslateDirectoryData tdd = Args.ParseTranslatePathOption(m_pathTable, option);

            XAssert.IsTrue(tdd.FromPath.ToString(m_pathTable).EndsWith(m_helloTestPath, StringComparison.OrdinalIgnoreCase));
            XAssert.IsTrue(tdd.ToPath.ToString(m_pathTable).EndsWith(m_worldTestPath, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void DblColSeparator()
        {
            CommandLineUtilities.Option option = default(CommandLineUtilities.Option);
            option.Name = "translateDirectory";
            option.Value = m_helloTestPath + "::" + m_worldTestPath;

            TranslateDirectoryData tdd = Args.ParseTranslatePathOption(m_pathTable, option);

            XAssert.IsTrue(tdd.FromPath.ToString(m_pathTable).EndsWith(m_helloTestPath, StringComparison.OrdinalIgnoreCase));
            XAssert.IsTrue(tdd.ToPath.ToString(m_pathTable).EndsWith(m_worldTestPath, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void InvalidTranslateFrom()
        {
            CommandLineUtilities.Option option = default(CommandLineUtilities.Option);
            option.Name = "translateDirectory";
            option.Value =  m_helloTestPath + "::" + m_worldTestPath + "<" + m_bigTestPath;

            try
            {
                Args.ParseTranslatePathOption(m_pathTable, option);
            }
            catch (Exception e)
            {
                XAssert.AreEqual(
                    "The value '" + m_helloTestPath + "::" + m_worldTestPath + "' provided for the /translateDirectory argument is invalid. It should have a valid translateFrom path",
                    e.GetLogEventMessage());
                return;
            }

            if (!OperatingSystemHelper.IsUnixOS)
            {
                XAssert.Fail("Should have gotten an exception.");
            }
        }

        [Fact]
        public void InvalidTranslateTo()
        {
            CommandLineUtilities.Option option = default(CommandLineUtilities.Option);
            option.Name = "translateDirectory";
            option.Value = m_helloTestPath + "<" + m_worldTestPath + "::" + m_bigTestPath;

            try
            {
                Args.ParseTranslatePathOption(m_pathTable, option);
            }
            catch (Exception e)
            {
                XAssert.AreEqual(
                    "The value '" + m_worldTestPath + "::" + m_bigTestPath + "' provided for the /translateDirectory argument is invalid. It should have a valid translateTo path",
                    e.GetLogEventMessage());
                return;
            }

            if (!OperatingSystemHelper.IsUnixOS)
            {
                XAssert.Fail("Should have gotten an exception.");
            }
        }
    }
}
