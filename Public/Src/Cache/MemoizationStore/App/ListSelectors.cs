// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.MemoizationStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     List selectors verb.
        /// </summary>
        [Verb(Aliases = "ls", Description = "List selectors for a weak fingerprint")]
        public void ListSelectors(
            [Required, Description("Cache root directory")] string root,
            [Required, Description("Weak fingerprint hex string")] string weakFingerprint
            )
        {
            // ReSharper disable once ArgumentsStyleLiteral
            // ReSharper disable once ArgumentsStyleAnonymousFunction
            RunSQLiteStoreSession(new AbsolutePath(root), lruEnabled: true, funcAsync: async (context, store, session) =>
            {
                IEnumerable<GetSelectorResult> results =
                    await session.GetSelectors(context, new Fingerprint(weakFingerprint), CancellationToken.None, UrgencyHint.Nominal).ToList(CancellationToken.None);

                foreach (GetSelectorResult result in results)
                {
                    _logger.Always($"{result}");
                }
            });
        }
    }
}
