// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Luxon from "luxon";

export function toFriendlyString(dateTime: Luxon.DateTime | string) : string {
    if (!dateTime) {
        return "";
    }

    if (typeof dateTime === "string") {
        dateTime = Luxon.DateTime.fromISO(dateTime);
    }

    let diff = dateTime.diffNow(["minutes", "hours"]);
    if (diff.hours < -24) {
        return dateTime.toLocaleString(Luxon.DateTime.DATETIME_SHORT);
    } 
    
    if (diff.hours < -8) {
        return `${Math.round(-1 * diff.hours)} hours ago`;
    }

    if (diff.minutes < 0) {
        return `${Math.round(-1 * diff.hours)} hours, ${Math.round(-1 * diff.minutes)} minutes ago`;
    }

    return `${Math.round(-1 * diff.minutes)} minutes ago`;
}

export function toLongString(dateTime: Luxon.DateTime | string) : string {
    if (!dateTime) {
        return "";
    }

    if (typeof dateTime === "string") {
        dateTime = Luxon.DateTime.fromISO(dateTime);
    }
    
    return dateTime.toLocaleString(Luxon.DateTime.DATETIME_SHORT);
}

export function toDuration(duration: Luxon.Duration | string) : string {
    if (!duration) {
        return "";
    }

    if (typeof duration === "string") {
        duration = Luxon.Duration.fromISO(duration);
    }

    // ensure hours is most significant;
    if (duration.shiftTo("hours").hours >= 1)
    {
        return `${Math.floor(duration.hours)} h, ${Math.floor(duration.minutes)} m, ${Math.floor(duration.seconds)} s`;
    }
    else if (duration.minutes > 0)
    {
        return `${Math.floor(duration.minutes)} m, ${Math.floor(duration.seconds)} s, ${Math.floor(duration.milliseconds)} ms`;
    }
    else if (duration.seconds > 0)
    {
        return `${Math.floor(duration.seconds)} s, ${Math.floor(duration.milliseconds)} ms`;
    }
    else
    {
        return `${Math.floor(duration.milliseconds)} ms`;
    }
}
