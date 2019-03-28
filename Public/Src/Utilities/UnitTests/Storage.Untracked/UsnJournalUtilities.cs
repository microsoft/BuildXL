// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;

namespace Test.BuildXL.Storage.Admin
{
    public static class UsnJournalUtilities
    {
        public static UsnChangeReasons GetAggregateChangeReasons(
            FileId fileFilter,
            IEnumerable<UsnRecord> records)
        {
            UsnChangeReasons reasons = default(UsnChangeReasons);
            foreach (UsnRecord record in records)
            {
                if (fileFilter == record.FileId)
                {
                    reasons |= record.Reason;
                }
            }

            return reasons;
        }
    }
}
