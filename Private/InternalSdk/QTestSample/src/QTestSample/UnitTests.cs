// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace QTestExample
{
    [TestClass]
    public class QTestSampleTest
    {
        /// <summary>
        /// Ensure that necessary test data exists in the QTest sandbox
        /// </summary>
        [TestMethod]
        public void TestFoo2()
        {
            Program.Main(null);            
            Assert.AreEqual(4, 4);
            
            string testDataDir = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));

            string fooFile = Path.Combine(testDataDir, @"TestData\foo\foo.txt");
            Assert.IsTrue(File.Exists(fooFile));

            string barFile = Path.Combine(testDataDir, @"TestData\bar\bar.txt");
            Assert.IsTrue(File.Exists(barFile));

            string parentDataFile = Path.Combine(testDataDir, @"TestData\ParentData.txt");
            Assert.IsTrue(File.Exists(parentDataFile));

            Console.WriteLine("ttt");
        }
    }
}
