// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.CloudTest.Gvfs
{
    public class BasicFileOperationTests
    {
        [Fact]
        public void EditFile()
        {
            using (var helper = new JournalHelper())
            {
                var filePath = Path.Combine(helper.WorkingFolder, "a.txt");
                
                // Track
                helper.TrackPath(filePath);
                // $REview: @Iman: Why does tracking a non-existent file require adding the parent folder?
                // If this is true, I'll implement it in the helper
                helper.TrackPath(helper.WorkingFolder);

                // Do
                File.WriteAllText(filePath, "hi");

                // Check
                helper.SnapCheckPoint();
                helper.AssertCreateFile(filePath);
            }
            
        }
    }
}