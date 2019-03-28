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
        public void TestFoo()
        {
            Program.Main(null);            
            Assert.AreEqual(4, 4);
            
            string testDataDir = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));                       

            string parentDataFile = Path.Combine(testDataDir, @"ParentData.txt");
            Assert.IsTrue(File.Exists(parentDataFile));            
        }
    }
}
