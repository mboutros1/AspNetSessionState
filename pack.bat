set v=0.0.0.45920

dotnet build src\MB.HybridSessionProviderAsync\MB.HybridSessionProviderAsync.csproj  -c Release

set packageFileName="MB.HybridSessionProviderAsync.%v%.nupkg"
echo %packageFileName%
set feed=%NugetFeed_Nugetter%
echo %feed% 
del %packageFileName%
nuget pack src\MB.HybridSessionProviderAsync\MB.HybridSessionProviderAsync.csproj -OutputDirectory src  -Properties Configuration=Release -IncludeReferencedProjects -version %v%
nuget push  -source "%feed%" -ApiKey VSTS "src\\%packageFileName%"  
pause
