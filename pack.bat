set packageFileName="\\file.abssuite.analytics.moodys.net\Software\Oracle\OracleNugetPackages\OracleProvider19-Unmanaged-64\Oracle.Data.Provider.Unmanaged.x64.4.122.19.2.nupkg"
set dir="\\file.abssuite.analytics.moodys.net\Software\Oracle\OracleNugetPackages\OracleProvider19-Unmanaged-64"
set feed="http://tfs.abssuite.analytics.moodys.net:8080/tfs/DefaultCollection/_packaging/thirdParty/nuget/v3/index.json"
del %packageFileName%
nuget pack src\MB.HybridSessionProviderAsync\MB.HybridSessionProviderAsync.csproj -OutputDirectory src  -Properties Configuration=Release -IncludeReferencedProjects
nuget push  -source "%feed%" -ApiKey VSTS "%packageFileName%"  
