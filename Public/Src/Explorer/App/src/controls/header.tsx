// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import { NavLink } from 'react-router-dom'

export class Header extends React.Component<{}, {}> {
    render() {
        return (
            <header>
                <div className="brand"><NavLink to="/" exact><span className="brand-icon">🁴</span><span className="brand-name">Build Explorer</span></NavLink></div>
                <div className="breadcrumbs commandbar-item">
                    <ol className="breadcrumb-list">
                    </ol>
                </div>

                <div className="spacer commandbar-item"><span></span></div>

                <div className="command commandbar-item"><i className="ms-Icon ms-Icon--Search" aria-hidden="true"></i></div>
            </header>
        );
    }
}
