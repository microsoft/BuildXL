// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions.Internal;

namespace BuildXL.Cache.ContentStore.Sessions.Internal
{
    /// <summary>
    ///     Extension methods for <see cref="IContentSession"/>.
    /// </summary>
    public static class ContentSessionExtensions
    {
        public static Task<PutResult> PutOrPutTrustedFileAsync(
            this IContentSession session,
            Context context,
            ContentHashWithSize contentHashWithSize,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            if (session is ITrustedContentSession trustedSession)
            {
                return trustedSession.PutTrustedFileAsync(
                    context,
                    contentHashWithSize,
                    path,
                    realizationMode,
                    cts,
                    urgencyHint);
            }
            else
            {
                return session.PutFileAsync(
                    context,
                    contentHashWithSize,
                    path,
                    realizationMode,
                    cts,
                    urgencyHint);
            }
        }
    }
}
