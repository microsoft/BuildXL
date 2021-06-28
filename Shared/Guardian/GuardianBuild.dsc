import * as Guardian from "Sdk.Guardian";
import {Transformer} from "Sdk.Transformers";


// Partially seal Guardian directory because Guardian may write temp files under the root directory
const guardianDirectory : Directory = d`${Environment.getPathValue("TOOLPATH_GUARDIAN")}/gdn`;
const guardianTool : StaticDirectory = Transformer.sealPartialDirectory(guardianDirectory, globR(guardianDirectory, "*"));

// Guardian arguments will be automatically set by the SDK
export const guardianCredScanResult = Guardian.runCredScanOnEntireRepository(guardianTool, d`${Context.getMount("EnlistmentRoot").path}`);