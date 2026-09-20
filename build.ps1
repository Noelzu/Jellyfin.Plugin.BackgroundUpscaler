$ErrorActionPreference = 'Stop'

$project = 'Jellyfin.Plugin.BackgroundUpscaler.csproj'
[xml]$xml = Get-Content $project
$version = $xml.Project.PropertyGroup.Version | Select-Object -First 1
if (-not $version) { throw 'Version not found in csproj' }

Remove-Item -Recurse -Force dist -ErrorAction SilentlyContinue
dotnet restore $project
dotnet build $project -c Release --no-restore

New-Item -ItemType Directory -Path dist/plugin -Force | Out-Null
Copy-Item 'bin/Release/net10.0/Jellyfin.Plugin.BackgroundUpscaler.dll' 'dist/plugin/'

$zip = "dist/BackgroundUpscaler_$version.zip"
Compress-Archive -Path 'dist/plugin/Jellyfin.Plugin.BackgroundUpscaler.dll' -DestinationPath $zip -Force
Write-Host "Created $zip"
