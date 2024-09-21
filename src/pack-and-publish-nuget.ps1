cd kissproxylib
del .\bin\Release\m0lte.kissproxylib.*.nupkg
dotnet pack
dotnet nuget push .\bin\Release\m0lte.kissproxylib.*.nupkg -k $env:m0lte_nuget_key -s https://api.nuget.org/v3/index.json
cd ..