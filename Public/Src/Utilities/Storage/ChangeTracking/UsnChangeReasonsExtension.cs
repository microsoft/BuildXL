// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Native.IO.Windows;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Link impact kind.
    /// </summary>
    internal enum LinkImpact
    {
        /// <summary>
        /// None.
        /// </summary>
        None,

        /// <summary>
        /// All links affected.
        /// </summary>
        AllLinks,

        /// <summary>
        /// Only a single link affected.
        /// </summary>
        SingleLink,
    }

    /// <summary>
    /// Membership impact kind.
    /// </summary>
    [Flags]
    internal enum MembershipImpact
    {
        /// <summary>
        /// None.
        /// </summary>
        None = 0,

        /// <summary>
        /// Creation of a new file.
        /// </summary>
        Creation = 1,

        /// <summary>
        /// Deletion of a file.
        /// </summary>
        Deletion = 2,

        /// <summary>
        /// Both creation and deletion.
        /// </summary>
        CreationAndDeletion = Creation | Deletion,
    }

    /// <summary>
    /// Extension for <see cref="UsnChangeReasons"/>.
    /// </summary>
    internal static class UsnChangeReasonsExtension
    {
        /// <summary>
        /// Gets link impact.
        /// </summary>
        internal static LinkImpact LinkImpact(this UsnChangeReasons reasons)
        {
            const UsnChangeReasons AllLinkChangeReasons =
                UsnChangeReasons.FileDelete |
                // Only caused by deletion of the last link, so let's be thrifty and claim 'all links' affected.
                UsnChangeReasons.DataExtend |
                UsnChangeReasons.DataOverwrite |
                UsnChangeReasons.DataTruncation |
                UsnChangeReasons.StreamChange |
                UsnChangeReasons.ReparsePointChange;

            const UsnChangeReasons SingleLinkChangeReasons =
                UsnChangeReasons.HardLinkChange | // This is a deletion or creation of a link (other than the last).
                UsnChangeReasons.RenameOldName;

            if ((reasons & AllLinkChangeReasons) != 0)
            {
                return ChangeTracking.LinkImpact.AllLinks;
            }

            if ((reasons & SingleLinkChangeReasons) != 0)
            {
                return ChangeTracking.LinkImpact.SingleLink;
            }

            return ChangeTracking.LinkImpact.None;
        }

        /// <summary>
        /// Gets membership impacts.
        /// </summary>
        internal static MembershipImpact MembershipImpact(this UsnChangeReasons reasons)
        {
            const UsnChangeReasons CreationReasons =
                UsnChangeReasons.FileCreate |
                UsnChangeReasons.RenameNewName |
                UsnChangeReasons.HardLinkChange; // Can be either a creation or deletion

            const UsnChangeReasons DeletionReasons =
                UsnChangeReasons.FileDelete |
                UsnChangeReasons.RenameOldName |
                UsnChangeReasons.HardLinkChange; // Can be either a creation or deletion

            return ((reasons & CreationReasons) != 0 ? ChangeTracking.MembershipImpact.Creation : ChangeTracking.MembershipImpact.None) |
                   ((reasons & DeletionReasons) != 0 ? ChangeTracking.MembershipImpact.Deletion : ChangeTracking.MembershipImpact.None);
        }
    }
}
