// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import * as DateTimeHelpers from '../models/dateTimeHelpers';

import * as Model from "../models/buildSummary";
import { AsyncBuildPage, BuildRouteParams } from "../pages/asyncBuildPage";

export class BuildSummary extends AsyncBuildPage<BuildRouteParams, Model.Summary> {

    getRequestUrl(props: BuildRouteParams): string {
        return `/b/${props.sessionId}/summary`;
    }

    renderData(data: Model.Summary) {
        return (
            <div className="hub-page">
                <div className="hub-header">
                    <h1>Build Summary</h1>
                </div>
                <div className="hub-body">
                    <section className="hub-box">
                        <table className="hub-details">
                            <tbody>
                            <tr>
                                <th>SessionId:</th>
                                    <td>{data.sessionId}</td>
                                </tr>
                                <tr>
                                    <th>SartTime:</th>
                                    <td>{DateTimeHelpers.toLongString(data.startTime)}</td>
                                </tr>
                                <tr>
                                    <th>Duration:</th>
                                    <td>{DateTimeHelpers.toDuration(data.duration)}</td>
                                </tr>
                            </tbody>
                        </table>
                    </section>
                </div>
            </div>
        )
    };
}


