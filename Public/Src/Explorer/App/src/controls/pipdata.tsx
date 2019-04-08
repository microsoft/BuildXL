// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import * as PipDataModel from '../models/pipData';
import * as Links from "../controls/links"
import * as Status from "../models/status"

export interface Props {
    data: PipDataModel.PipData,
    sessionId: string,
    style?: "linePerArg",
}

export class PipData extends React.Component<Props, {}> {
    render() {
        switch (this.props.style) {
            case "linePerArg":
                return this.props.data.i.map((entry, index) => <div key={index.toString()}>{this.renderEntry(entry, this.props.data.e)}</div>);
            default:
                return this.renderPipData(this.props.data);
        }
    }

    renderPipData(data: PipDataModel.PipData) : React.ReactNode {
        let nodes = [];
        let i = 0;
        for (var item of data.i)
        {
            if (i !== 0 && data.s) {
                nodes.push(<span key={i.toString()}>{data.s}</span>);
                i++;
            }
            
            nodes.push(<span key={i.toString()}>{this.renderEntry(item, data.e)}</span>);
            i++
        }

        return <span>{nodes}</span>;
    }

    renderEntry(entry: PipDataModel.PipDataEntry, encoding: PipDataModel.PipDataEncoding) : React.ReactNode {
        if (PipDataModel.isStringEntry(entry)) {
            // $TODO: Handle encoding scenario's
            return entry.s;
        }
        else if (PipDataModel.isPathEntry(entry)) {
            return <Links.PathLink renderIcon={false} reference={entry.p} sessionId={this.props.sessionId} />;
        }
        else if (PipDataModel.isNestedEntry(entry)) {
            return this.renderPipData(entry.n);
        }

        throw Status.logLocalError("Error unexpected PipDataEntry type");
    }
}
