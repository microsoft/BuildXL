// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL;
using BuildXL.Utilities.Core;

namespace Test.BuildXL.FingerprintStore
{
    internal class GitHelperMock : IGitHelper
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="count"></param>
        /// <param name="refName"></param>
        /// <returns></returns>
        public Task<Possible<IReadOnlyList<string>>> GetLatestCommitHashesAsync(int count, string refName = "HEAD")
        {
            // Represent latest commits are "{refName}-0", "{refName}-1", etc.
            var latestCommits = new List<string>(count);

            int delta = 0;
            
            if (refName.Contains('-') && int.TryParse(refName.Split("-").Last(), out var index))
            {
                // The refName is a commit hash given by this mock, like "main-2". Use that index 
                // to count backwards (and, say, return main-3, main-4...)
                delta = index;
                refName = refName.Split("-", 2)[0];
            }

            for (int i = 0; i < count; i++)
            {
                latestCommits.Add($"{refName}-{i+delta}");
            }

            return Task.FromResult(new Possible<IReadOnlyList<string>>(latestCommits));
        }

        public Task<Possible<string>> GetMergeBaseAsync(string a, string b)
        {
            // Always return "{a}-4", simulating something like this topology
            //           o---o---o--- B
            //          /
            //      ---o---o---o---o--- A
            return Task.FromResult(new Possible<string>($"{a}-4"));
        }
    }
}
