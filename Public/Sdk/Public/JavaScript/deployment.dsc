
import * as Deployment from "Sdk.Deployment";

/**
 * Used to deploy this SDK as part of the in-box SDKs
 */
@@public
export const deployment: Deployment.Definition = {
    // All files but this one
    contents: globR(d`.`, "*").filter(file => file !== Context.getSpecFile())
};