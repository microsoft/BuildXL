/**
 * Read three graph fragments and add their pips to this build.
 * 
 * fragment1:
 *    pipA1 -> fileA
 * 
 * fragment2:
 *    pipB1 <- fileA
 *          -> dirB
 * 
 * fragment3:
 *    pipC1 <- dirB
 *          -> fileC1
 *          -> fileC2 
 * 
  */

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

// Both the second and the third fragments must specify their fragment dependency.
export const fragment1 = Transformer.readPipGraphFragment(f`${Context.getMount("GraphFragments").path}\fragment1.out`, []);
export const fragment2 = Transformer.readPipGraphFragment(f`${Context.getMount("GraphFragments").path}\fragment2.out`, [fragment1]);
export const fragment3 = Transformer.readPipGraphFragment(f`${Context.getMount("GraphFragments").path}\fragment3.out`, [fragment2]);
