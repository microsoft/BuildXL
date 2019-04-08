import * as Deployment from "Sdk.Deployment";

@@public
export const deployment : Deployment.Definition = {
    contents: [
        {
            file: f`Templates/BuildXL.dsc.template`,
            targetFileName: "BuildXL.dsc",
        },
        {
            file: f`Templates/module.config.dsc.template`,
            targetFileName: "module.config.dsc",
        },
    ]
};