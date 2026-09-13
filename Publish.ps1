# Builds the two release executables into .\publish
#   ShowTextOnly.exe                                    portable, Native AOT, runs without .NET installed
#   ShowTextOnly-FrameworkDependent-RequiresNET10.exe   single file, needs the .NET 10 Desktop Runtime
# Native AOT links with the Visual C++ linker from the "Desktop development with C++" workload. When vswhere does not
# list that Visual Studio installation, pass the path of its vcvarsall.bat with -VcVarsAllPath.
param([string]$VcVarsAllPath)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'ShowTextOnly.csproj'
$publishFolder = Join-Path $PSScriptRoot 'publish'
$workFolder = Join-Path $PSScriptRoot 'obj\PublishWork'

if (-not $VcVarsAllPath)
{
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere)
    {
        $VcVarsAllPath = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'VC\Auxiliary\Build\vcvarsall.bat' | Select-Object -First 1
    }
}
if (-not $VcVarsAllPath -or -not (Test-Path $VcVarsAllPath))
{
    throw 'vcvarsall.bat was not found. Install the "Desktop development with C++" workload of Visual Studio or pass -VcVarsAllPath.'
}

if (Test-Path $workFolder) { Remove-Item $workFolder -Recurse -Force }
New-Item -ItemType Directory -Force $publishFolder | Out-Null

Write-Host '== Framework-dependent (single file, ReadyToRun)'
dotnet publish $project -c Release -r win-x64 --self-contained false -o "$workFolder\FrameworkDependent" --nologo
if ($LASTEXITCODE -ne 0) { throw 'The framework-dependent publish failed.' }

Write-Host '== Portable (Native AOT)'
cmd /c "call `"$VcVarsAllPath`" amd64 >nul && dotnet publish `"$project`" -c Release -r win-x64 -p:PublishAot=true -p:IlcUseEnvironmentalTools=true -o `"$workFolder\Portable`" --nologo"
if ($LASTEXITCODE -ne 0) { throw 'The Native AOT publish failed.' }

Copy-Item "$workFolder\FrameworkDependent\ShowTextOnly.exe" "$publishFolder\ShowTextOnly-FrameworkDependent-RequiresNET10.exe" -Force
Copy-Item "$workFolder\Portable\ShowTextOnly.exe" "$publishFolder\ShowTextOnly.exe" -Force
Get-ChildItem $publishFolder -Filter *.exe | ForEach-Object { '{0,-50} {1,6:N1} MB' -f $_.Name, ($_.Length / 1MB) }
