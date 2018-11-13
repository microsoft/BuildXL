// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Test.BuildXL.TestUtilities.XUnit
{
    /// <summary>
    /// This is a util class for getting any information about the local drives
    /// </summary>
    public class DriveUtils
    {
        /// <summary>
        /// Get first unused drive letter on this machine that
        /// does not occur in the filter list
        /// </summary>
        public static string GetFirstUnusedDriveLetter(string[] filter = null)
        {
            List<string> unusedLetters = GetAllUnusedDriveLetters();
            return unusedLetters.FirstOrDefault(letter => filter == null || !filter.Contains(letter));
        }

        /// <summary>
        /// Get a list of all unused drive letters
        /// </summary>
        public static List<string> GetAllUnusedDriveLetters()
        {
            // puts the first letter, as an int, of the drive into the hash set (d[0] gets first letter)
            HashSet<int> driveLettersUsed = new HashSet<int>(Directory.GetLogicalDrives().Select(d => (int)d[0]));
            List<string> unUsedLetters = new List<string>();

            // iterating over ASCII table A-Z letters
            for (int asciiIndex = (int)'A'; asciiIndex <= (int)'Z'; asciiIndex++)
            {
                if (!driveLettersUsed.Contains(asciiIndex))
                {
                    string letter = Convert.ToChar(asciiIndex) + string.Empty;
                    unUsedLetters.Add(letter);
                }
            }

            return unUsedLetters;
        }
    }
}
