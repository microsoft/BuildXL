// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import * as Settings from "../models/settings";
import { IPalette, ISemanticColors, loadTheme } from 'office-ui-fabric-react';

export class Theme extends React.Component<{}, { theme : Settings.ThemeStyle }> {

    public constructor(props: {}) {
        super(props);

        window.addEventListener("bxp-settingsUpdated", (e : CustomEvent<Settings.SettingsData>) => {
            this.onSettingsChanged(e.detail)
        });

        this.state = {
            theme: Settings.current.theme,
        };
        this.updateTheme(this.state.theme);
    }

    private contentElement: HTMLStyleElement;

    onSettingsChanged(newSettings: Settings.SettingsData) {
        this.setState({theme: newSettings.theme});
        this.updateTheme(newSettings.theme);

    }

    getThemeDefinition(theme: Settings.ThemeStyle): ThemeDefinition {
        switch (theme) {
            case "dark":
                return darkTheme;
            case "light":
                return lightTheme;
            default:
                return darkTheme;
        }
    }

    getCSSVariablesContent(themeDefinition: ThemeDefinition): string {
        let cssVariables = [];
        for (const varName in themeDefinition) {
            cssVariables.push(`--${varName}: ${themeDefinition[varName]}`);
        }

        return `:root { ${cssVariables.join("; ")} }`;
    }

    updateTheme(theme: Settings.ThemeStyle) {
        if (!this.contentElement) {
            this.contentElement = document.createElement("style");
            this.contentElement.type = "text/css";
            if (document.head) {
                document.head.appendChild(this.contentElement);
            }
        }

        const currentTheme = this.getThemeDefinition(theme);

        // Update our the variables block
        this.contentElement.innerText = this.getCSSVariablesContent(currentTheme);

        // Update office fabric palette

        // Convert the TFS theme into the appropriate OfficeFabric Theme.
        const palette: Partial<IPalette> = { };

        // Build the primary color part of the palette.
        palette.themePrimary = "rgba(" + currentTheme['palette-primary'] + ", 1)";
        palette.themeLighterAlt = "rgba(" + currentTheme['palette-primary-tint-40'] + ", 1)";
        palette.themeLighter = "rgba(" + currentTheme['palette-primary-tint-30'] + ", 1)";
        palette.themeLight = "rgba(" + currentTheme['palette-primary-tint-20'] + ", 1)";
        palette.themeTertiary = "rgba(" + currentTheme['palette-primary-tint-10'] + ", 1)";
        palette.themeDarkAlt = "rgba(" + currentTheme['palette-primary-shade-10'] + ", 1)";
        palette.themeDark = "rgba(" + currentTheme['palette-primary-shade-20'] + ", 1)";
        palette.themeDarker = "rgba(" + currentTheme['palette-primary-shade-30'] + ", 1)";

        // Build the neutral color part of the palette.
        palette.neutralLighterAlt = "rgba(" + currentTheme['palette-neutral-2'] + ", 1)";
        palette.neutralLighter = "rgba(" + currentTheme['palette-neutral-4'] + ", 1)";
        palette.neutralLight = "rgba(" + currentTheme['palette-neutral-8'] + ", 1)";
        palette.neutralQuaternaryAlt = "rgba(" + currentTheme['palette-neutral-10'] + ", 1)";
        palette.neutralQuaternary = "rgba(" + currentTheme['palette-neutral-10'] + ", 1)"; /* Slightly lighter in Fabric */
        palette.neutralTertiaryAlt = "rgba(" + currentTheme['palette-neutral-20'] + ", 1)";
        palette.neutralTertiary = "rgba(" + currentTheme['palette-neutral-30'] + ", 1)";
        palette.neutralSecondary = "rgba(" + currentTheme['palette-neutral-60'] + ", 1)";
        palette.neutralPrimaryAlt = "rgba(" + currentTheme['palette-neutral-80'] + ", 1)";/* Slightly darker in Fabric */
        palette.neutralPrimary = "rgba(" + currentTheme['palette-neutral-80'] + ", 1)";
        palette.neutralDark = "rgba(" + currentTheme['palette-neutral-100'] + ", 1)";

        // Other general colors.
        palette.black = "rgba(" + currentTheme['palette-neutral-100'] + ", 1)";
        palette.white = "rgba(" + currentTheme['palette-neutral-0'] + ", 1)";

        // @TODO: Do we want to map our colors to the Fabric Semantic Colors.
        const semanticColors: Partial<ISemanticColors> = { };
        semanticColors.listText = currentTheme['text-primary-color'];
        semanticColors.link = "rgba(" + currentTheme['palette-primary'] + ", 1)";
        
        // Load the theme into the office fabric theme system.
        loadTheme({ palette: palette , semanticColors: semanticColors });
    }

    render() {
        return (
            <></>
        );
    }
}


interface ThemeDefinition {
    [key: string] : string
}

const lightTheme : ThemeDefinition = {
    "palette-primary-shade-30": "0, 69, 120",
    "palette-primary-shade-20": "0, 90, 158",
    "palette-primary-shade-10": "16, 110, 190",
    "palette-primary": "0, 120, 212",
    "palette-primary-tint-10": "43, 136, 216",
    "palette-primary-tint-20": "199, 224, 244",
    "palette-primary-tint-30": "222, 236, 249",
    "palette-primary-tint-40": "239, 246, 252",
    "palette-neutral-100": "0, 0, 0",
    "palette-neutral-80": "51, 51, 51",
    "palette-neutral-70": "76, 76, 76",
    "palette-neutral-60": "102, 102, 102",
    "palette-neutral-30": "166, 166, 166",
    "palette-neutral-20": "200, 200, 200",
    "palette-neutral-10": "218, 218, 218",
    "palette-neutral-8": "234, 234, 234",
    "palette-neutral-6": "239, 239, 239",
    "palette-neutral-4": "244, 244, 244",
    "palette-neutral-2": "248, 248, 248",
    "palette-neutral-0": "255, 255, 255",
    "palette-accent1-light": "249, 235, 235",
    "palette-accent1": "218, 10, 0",
    "palette-accent1-dark": "168, 0, 0",
    "palette-accent2-light": "223, 246, 221",
    "palette-accent2": "186, 216, 10",
    "palette-accent2-dark": "16, 124, 16",
    "palette-accent3-light": "255, 244, 206",
    "palette-accent3": "248, 168, 0",
    "palette-accent3-dark": "220, 182, 122",

    "background-color": "rgba(var(--palette-neutral-0), 1)",

    "text-primary-color": "rgba(var(--palette-neutral-100), .9)",
    "text-secondary-color": "rgba(var(--palette-neutral-100), .55)",
    "text-disabled-color": "rgba(var(--palette-neutral-100), .3)",

    "component-grid-row-hover-color": "rgba(var(--palette-neutral-4), 1)",
    "component-grid-selected-row-color": "rgba(var(--palette-primary-tint-40), 1)",
    "component-grid-focus-border-color": "rgba(var(--palette-primary), 1)",
    "component-grid-link-selected-row-color": "rgba(var(--palette-primary-shade-10), 1)",
    "component-grid-link-hover-color": "rgba(var(--palette-primary-shade-20), 1)",
    "component-grid-action-hover-color": "rgba(var(--palette-neutral-8), 1)",
    "component-grid-action-selected-cell-hover-color": "rgba(var(--palette-primary-tint-30), 1)",
    "component-grid-cell-bottom-border-color": "rgba(var(--palette-neutral-8), 1)",

    "icon-folder-color": "#dcb67a",

    "component-errorBoundary-border-color": "rgba(var(--palette-accent1), 1)",
    "component-errorBoundary-background-color": "rgba(var(--palette-accent1-light), 1)",

    "nav-header-background": "rgba(var(--palette-neutral-0), 1)",
    "nav-header-item-hover-background": "rgba(var(--palette-neutral-100), 0.02)",
    "nav-header-active-item-background": "rgba(var(--palette-neutral-100), 0.08)",
    "nav-header-text-primary-color": "var(--text-primary-color)",
    "nav-header-product-color": "rgba(var(--palette-primary), 1)",

    "nav-vertical-background-color": "rgba(var(--palette-neutral-100), 0.08)",
    "nav-vertical-item-hover-background": "rgba(var(--palette-neutral-100), 0.08)",
    "nav-vertical-active-group-background": "rgba(var(--palette-neutral-100), 0.08)",
    "nav-vertical-active-item-background": "rgba(var(--palette-neutral-100), 0.2)",
    "nav-vertical-text-primary-color": "var(--text-primary-color)",
    "nav-vertical-text-secondary-color": "var(--text-secondary-color)",

    "mainMenuBackgroundColor": "rgba(var(--palette-neutral-80), 1)",
    "mainMenuBackgroundHoverColor": "rgba(var(--palette-neutral-100), 1)",

    "tfsSelectorBackgroundColor": "rgba(var(--palette-primary-shade-20), 1)",

    "headerBottomBackgroundColor": "rgba(var(--palette-neutral-4), 1)",
    "headerBottomDisplayedTextColor": "rgba(var(--palette-primary-shade-10), 1)",
    "headerBottomLinkHoverColor": "rgba(var(--palette-neutral-20), 1)",
    "headerBottomTextColor": "rgba(var(--palette-neutral-100), 1)",
    "headerTopBackgroundColor": "rgba(var(--palette-primary), 1)",
    "headerTopLinkHoverColor": "rgba(var(--palette-primary-shade-10), 1)",
    "headerTopTextColor": "rgba(var(--palette-neutral-0), 1)",

    "searchBoxBackgroundColor": "rgba(var(--palette-primary-shade-20), 1)",
    "searchBoxPlaceholderTextColor": "rgba(var(--palette-primary-tint-20), 1)",
    "searchBoxTextColor": "rgba(var(--palette-neutral-0), 1)",
};

const darkTheme : ThemeDefinition = {
    "palette-primary-shade-30": "76, 161, 219",
    "palette-primary-shade-20": "51, 148, 214",
    "palette-primary-shade-10": "25, 135, 209",
    "palette-primary": "0, 97, 163",
    "palette-primary-tint-10": "0, 109, 183",
    "palette-primary-tint-20": "0, 97, 163",
    "palette-primary-tint-30": "0, 85, 142",
    "palette-primary-tint-40": "0, 73, 122",
    "palette-neutral-100": "212, 212, 212",
    "palette-neutral-80": "169, 169, 169",
    "palette-neutral-70": "148, 148, 148",
    "palette-neutral-60": "127, 127, 127",
    "palette-neutral-30": "85, 85, 85",
    "palette-neutral-20": "74, 74, 74",
    "palette-neutral-10": "64, 64, 64",
    "palette-neutral-8": "58, 58, 58",
    "palette-neutral-6": "53, 53, 53",
    "palette-neutral-4": "48, 48, 48",
    "palette-neutral-2": "42, 42, 42",
    "palette-neutral-0": "37, 37, 37",
    "palette-accent1-light": "249, 235, 235",
    "palette-accent1": "218, 10, 0",
    "palette-accent1-dark": "168, 0, 0",
    "palette-accent2-light": "223, 246, 221",
    "palette-accent2": "186, 216, 10",
    "palette-accent2-dark": "16, 124, 16",
    "palette-accent3-light": "255, 244, 206",
    "palette-accent3": "248, 168, 0",
    "palette-accent3-dark": "220, 182, 122",
    "background-color": "rgba(var(--palette-neutral-0), 1)",
    "text-primary-color": "rgba(var(--palette-neutral-100), .9)",
    "text-secondary-color": "rgba(var(--palette-neutral-100), .55)",
    "text-disabled-color": "rgba(var(--palette-neutral-100), .3)",
    "component-grid-row-hover-color": "rgba(var(--palette-neutral-4), 1)",
    "component-grid-selected-row-color": "rgba(var(--palette-primary-tint-40), 0.5)",
    "component-grid-focus-border-color": "rgba(var(--palette-primary), 1)",
    "component-grid-link-selected-row-color": "rgba(var(--palette-primary-shade-20), 1)",
    "component-grid-link-hover-color": "rgba(var(--palette-primary-shade-30), 1)",
    "component-grid-action-hover-color": "rgba(var(--palette-neutral-8), 1)",
    "component-grid-action-selected-cell-hover-color": "rgba(var(--palette-primary-tint-30), 1)",
    "component-grid-cell-bottom-border-color": "rgba(var(--palette-neutral-8), 1)",
    "icon-folder-color": "#dcb67a",
    "component-errorBoundary-border-color": "rgba(var(--palette-accent1), 1)",
    "component-errorBoundary-background-color": "rgba(var(--palette-accent1-light), 1)",
    "nav-header-background": "rgba(var(--palette-neutral-0), 1)",
    "nav-header-item-hover-background": "rgba(var(--palette-neutral-100), 0.02)",
    "nav-header-active-item-background": "rgba(var(--palette-neutral-100), 0.08)",
    "nav-header-text-primary-color": "var(--text-primary-color)",
    "nav-header-product-color": "rgba(var(--palette-primary), 1)",
    "nav-vertical-background-color": "rgba(var(--palette-neutral-100), 0.08)",
    "nav-vertical-item-hover-background": "rgba(var(--palette-neutral-100), 0.08)",
    "nav-vertical-active-group-background": "rgba(var(--palette-neutral-100), 0.08)",
    "nav-vertical-active-item-background": "rgba(var(--palette-neutral-100), 0.2)",
    "nav-vertical-text-primary-color": "var(--text-primary-color)",
    "nav-vertical-text-secondary-color": "var(--text-secondary-color)",
    "mainMenuBackgroundColor": "rgba(var(--palette-neutral-80), 1)",
    "mainMenuBackgroundHoverColor": "rgba(var(--palette-neutral-100), 1)",
    "tfsSelectorBackgroundColor": "rgba(var(--palette-primary-shade-20), 1)",
    "headerBottomBackgroundColor": "rgba(var(--palette-neutral-4), 1)",
    "headerBottomDisplayedTextColor": "rgba(var(--palette-primary-shade-10), 1)",
    "headerBottomLinkHoverColor": "rgba(var(--palette-neutral-20), 1)",
    "headerBottomTextColor": "rgba(var(--palette-neutral-100), 1)",
    "headerTopBackgroundColor": "rgba(var(--palette-primary), 1)",
    "headerTopLinkHoverColor": "rgba(var(--palette-primary-shade-10), 1)",
    "headerTopTextColor": "rgba(var(--palette-neutral-0), 1)",
    "searchBoxBackgroundColor": "rgba(var(--palette-primary-shade-20), 1)",
    "searchBoxPlaceholderTextColor": "rgba(var(--palette-primary-tint-20), 1)",
    "searchBoxTextColor": "rgba(var(--palette-neutral-0), 1)",
};
