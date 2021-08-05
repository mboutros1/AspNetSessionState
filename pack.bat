set v=0.0.0.45913
set packageFileName="MB.HybridSessionProviderAsync.%v%.nupkg"
echo %packageFileName%
set feed="http://tfs.abssuite.analytics.moodys.net:8080/tfs/DefaultCollection/_packaging/Nugetter/nuget/v3/index.json"
del %packageFileName%
nuget pack src\MB.HybridSessionProviderAsync\MB.HybridSessionProviderAsync.csproj -OutputDirectory src  -Properties Configuration=Release -IncludeReferencedProjects -version %v%
nuget push  -source "%feed%" -ApiKey VSTS "src\\%packageFileName%"  
