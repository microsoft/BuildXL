<h1>Lower concurrency experiment</h1>

March 2022  

Office builds on gen5 compute (processor count:80) 

normalConcurrency: /maxMaterialize:100 /maxCacheLookup:160 
lowerConcurrency: /maxMaterialize:80 /maxMaterialize:80 

|AbTestingGroup|NumBuilds|InternalError|NumCacheHitRate_avg|TotalExeDuration_avg|Phase_Build_avg|Phase_BuildXL_p50|Phase_BuildXL_p95|CriticalPath_CacheLookup_avg|CriticalPath_MaterializeInputs_avg|
|:----|:----|:----|:----|:----|:----|:----|:----|:----|:----|
|lowerConcurrency|56,354|1,345|89.5|19,769.2|58.2|32.7|168.2|2.7|2.2|
|normalConcurrency|55,792|1,267|89.5|20,568.3|59.1|33.1|172.4|3.3|2.3|

<h1>MergeIOCache experiment</h1>

April 2022 

Office builds on gen5 compute (processor count:80)
normal=\"/maxMaterialize:80 /maxCacheLookup:80\" 
mergeIOCache=\"/maxMaterialize:80 /maxCacheLookup:80 /orchestratorCacheLookupMultiplier:1 /parameter:BuildXLMergeIOCacheLookupDispatcher=1\""

|AbTestingGroup|NumBuilds|InternalError|NumCacheHitRate_avg|TotalExeDuration_avg|Phase_Build_avg|Phase_BuildXL_p50|Phase_BuildXL_p95|CriticalPath_Start_avg|CriticalPath_CacheLookup_avg|CriticalPath_MaterializeInputs_avg|CriticalPath_TotalOrchestratorQueue_avg|
|:----|:----|:----|:----|:----|:----|:----|:----|:----|:----|:----|:----|
|mergeIOCache|23,218|268|90.2|17,071.1|58.7|33.2|182.5|0.1|3|2.3|11.8|
|normal|22,541|237|89.9|17,282.7|56.3|32.1|161.8|0.1|3|2.2|9.5|

<h1>MaxIO experiment</h1>

April 2022 

Office builds on gen5 compute (processor count:80)
/abTesting:maxIO10=\"/maxIO:10\"
/abTesting:maxIO20=\"/maxIO:20\" 

|AbTestingGroup|NumBuilds|InternalError|NumCacheHitRate_avg|TotalExeDuration_avg|Phase_Build_avg|Phase_BuildXL_p50|Phase_BuildXL_p95|CriticalPath_Start_avg|CriticalPath_CacheLookup_avg|CriticalPath_MaterializeInputs_avg|CriticalPath_TotalOrchestratorQueue_avg|
|:----|:----|:----|:----|:----|:----|:----|:----|:----|:----|:----|:----|
|maxIO20|18,614|307|88.3|21,871.7|54.9|32.4|161|0.1|3.4|2.7|10.3|
|maxIO10|18,407|337|88.5|20,421.7|54.5|31.9|162.2|0.1|3.4|2.6|9.8|

<h1>maxProcMultiplier experiment</h1>

May 23, 2022

All Office builds 
maxProcNormal=\"/maxProcMultiplier:1\"
maxProcLow=\"/maxProcMultiplier:0.9\"  
maxProcLowest=\"/maxProcMultiplier:0.75\"  

|AbTestingGroup|NumBuilds|InternalError|NumCacheHitRate_avg|TotalExeDuration_avg|Phase_Build_avg|Phase_BuildXL_p50|Phase_BuildXL_p95|CriticalPath_Start_avg|CriticalPath_CacheLookup_avg|CriticalPath_MaterializeInputs_avg|CriticalPath_TotalOrchestratorQueue_avg|
|:----|:----|:----|:----|:----|:----|:----|:----|:----|:----|:----|:----|
|maxIO20|18,614|307|88.3|21,871.7|54.9|32.4|161|0.1|3.4|2.7|10.3|
|maxIO10|18,407|337|88.5|20,421.7|54.5|31.9|162.2|0.1|3.4|2.6|9.8|

