<#
.SYNOPSIS
    Builds Cleaner.exe and CleanerSetup.exe from source.

.DESCRIPTION
    The project is a plain .NET Framework 4.8 WinForms application. Instead of
    requiring Visual Studio or an MSBuild restore step, this script calls the
    Roslyn C# compiler that ships with the .NET SDK directly and compiles the
    sources against the reference assemblies that are already on the machine.
    That keeps the build fully offline and reproducible.

    Outputs (dist/):
      Cleaner.exe        the application
      CleanerSetup.exe   the installer / uninstaller

.PARAMETER AppOnly
    Build the application only, skip the installer.

.PARAMETER SkipTests
    Do not run "Cleaner.exe --selftest" after building.

.PARAMETER Clean
    Remove the obj/ and dist/ folders first.

.EXAMPLE
    pwsh -File build\build.ps1
#>
[CmdletBinding()]
param(
    [switch]$AppOnly,
    [switch]$SkipTests,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = $PSScriptRoot
$root = Split-Path -Parent $scriptRoot
$srcApp = Join-Path $root 'src\Cleaner.App'
$srcSetup = Join-Path $root 'src\Cleaner.Setup'
$assets = Join-Path $root 'src\assets'
$dist = Join-Path $root 'dist'
$obj = Join-Path $root 'obj'
$docs = Join-Path $root 'docs'
$script:rootFull = [IO.Path]::GetFullPath($root)

$frameworkReferences = @(
    'mscorlib.dll'
    'System.dll'
    'System.Core.dll'
    'System.Drawing.dll'
    'System.Windows.Forms.dll'
    'System.Xml.dll'
    'System.Xml.Linq.dll'
    'System.Management.dll'
    'System.Runtime.Serialization.dll'
    'System.IO.Compression.dll'
    'System.IO.Compression.FileSystem.dll'
    'System.Web.Extensions.dll'
    'Microsoft.CSharp.dll'
)

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-Compiler {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $sdkRoots = @()
        $sdkRoots += (Join-Path $env:ProgramFiles 'dotnet\sdk')
        if (${env:ProgramFiles(x86)}) {
            $sdkRoots += (Join-Path ${env:ProgramFiles(x86)} 'dotnet\sdk')
        }

        foreach ($sdkRoot in $sdkRoots) {
            if (-not (Test-Path -LiteralPath $sdkRoot)) { continue }
            $candidates = Get-ChildItem -LiteralPath $sdkRoot -Directory -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending
            foreach ($sdk in $candidates) {
                $csc = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
                if (Test-Path -LiteralPath $csc) {
                    return [pscustomobject]@{
                        Kind    = 'dotnet'
                        Exe     = $dotnet.Source
                        Compiler = $csc
                        Version = $sdk.Name
                    }
                }
            }
        }
    }

    $legacy = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (Test-Path -LiteralPath $legacy) {
        return [pscustomobject]@{
            Kind     = 'framework'
            Exe      = $legacy
            Compiler = $null
            Version  = 'legacy'
        }
    }

    throw 'No C# compiler found. Install the .NET SDK (dotnet) or run on Windows with .NET Framework 4.x.'
}

function Resolve-ReferenceDirectory {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'),
        (Join-Path $env:ProgramFiles 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319')
    )

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrEmpty($candidate)) { continue }
        if ((Test-Path -LiteralPath (Join-Path $candidate 'mscorlib.dll')) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'System.Windows.Forms.dll'))) {
            return $candidate
        }
    }

    throw 'The .NET Framework 4.8 reference assemblies were not found. Install the .NET Framework 4.8 Developer Pack.'
}

function Convert-ToRelative {
    param([string]$Path)
    if ([string]::IsNullOrEmpty($Path)) { return $Path }
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = $script:rootFull + [IO.Path]::DirectorySeparatorChar
    if ($full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($prefix.Length)
    }
    return $full
}

function Invoke-Compile {    param(
        [string]$Output,
        [string[]]$Sources,
        [string[]]$References,
        [string]$Target = 'winexe',
        [string]$Icon,
        [string]$Manifest,
        [string[]]$Resources,
        [string]$Pdb
    )

    $arguments = New-Object System.Collections.Generic.List[string]
    $exe = $script:compiler.Exe

    if ($script:compiler.Kind -eq 'dotnet') {
        $arguments.Add($script:compiler.Compiler)
    }

    foreach ($argument in @(
        '/nologo'
        '/noconfig'
        '/nostdlib+'
        ('/target:' + $Target)
        '/platform:anycpu'
        '/langversion:11'
        '/optimize+'
        '/codepage:65001'
        '/warn:4'
        '/nowarn:1701'
        '/utf8output'
        ('/pathmap:' + $script:rootFull + '=/_/')
    )) {
        $arguments.Add($argument)
    }

    if ($Pdb) {
        $arguments.Add('/debug:portable')
        $arguments.Add('/pdb:' + (Convert-ToRelative -Path $Pdb))
    }

    if ($Icon) { $arguments.Add('/win32icon:' + (Convert-ToRelative -Path $Icon)) }
    if ($Manifest) { $arguments.Add('/win32manifest:' + (Convert-ToRelative -Path $Manifest)) }

    foreach ($reference in $References) {
        $arguments.Add('/reference:' + $reference)
    }

    foreach ($resource in $Resources) {
        $arguments.Add('/resource:' + (Convert-ToRelative -Path ($resource -split ',')[0]) + ',' + ($resource -split ',')[1])
    }

    $arguments.Add('/out:' + (Convert-ToRelative -Path $Output))

    foreach ($source in $Sources) {
        $arguments.Add((Convert-ToRelative -Path $source))
    }

    Push-Location $script:rootFull
    try {
        $compilerArguments = $arguments.ToArray()
        & $exe @compilerArguments
    }
    finally {
        Pop-Location
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed with exit code $LASTEXITCODE ($Output)."
    }
}

# --------------------------------------------------------------------------
$script:compiler = Resolve-Compiler
$referenceDirectory = Resolve-ReferenceDirectory
$references = @()
foreach ($name in $frameworkReferences) {
    $candidate = Join-Path $referenceDirectory $name
    if (Test-Path -LiteralPath $candidate) {
        $references += $candidate
    }
}

Write-Host "Cleaner build" -ForegroundColor White
Write-Host "  compiler   : $($script:compiler.Exe) $($script:compiler.Compiler) ($($script:compiler.Version))"
Write-Host "  references : $referenceDirectory"
Write-Host "  output     : $dist"

if ($Clean) {
    Write-Step 'Cleaning previous output'
    foreach ($folder in @($obj, $dist)) {
        if (Test-Path -LiteralPath $folder) {
            Remove-Item -LiteralPath $folder -Recurse -Force
        }
    }
}

foreach ($folder in @($obj, $dist)) {
    if (-not (Test-Path -LiteralPath $folder)) {
        New-Item -ItemType Directory -Path $folder | Out-Null
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $assets 'app.ico'))) {
    throw 'src/assets/app.ico is missing. Run build/make-icon.py to regenerate it.'
}

# --------------------------------------------------------------------------
Write-Step 'Compiling Cleaner.exe'
$appSources = @(Get-ChildItem -LiteralPath $srcApp -Filter '*.cs' -File | ForEach-Object { $_.FullName })
if ($appSources.Count -eq 0) {
    throw "No sources found in $srcApp."
}

$appExe = Join-Path $dist 'Cleaner.exe'
Invoke-Compile `
    -Output $appExe `
    -Sources $appSources `
    -References $references `
    -Icon (Join-Path $assets 'app.ico') `
    -Manifest (Join-Path $scriptRoot 'app.manifest') `
    -Pdb (Join-Path $obj 'Cleaner.pdb')

$appSize = (Get-Item -LiteralPath $appExe).Length
Write-Host ("  Cleaner.exe  {0:N0} bytes" -f $appSize) -ForegroundColor Green

# --------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Step 'Running self tests'
    # Cleaner.exe is a GUI subsystem binary: cmd.exe waits for it and propagates
    # the exit code, PowerShell alone would not. A throw-away data root keeps the
    # developer's real settings and logs untouched.
    $selfTestData = Join-Path $obj 'selftest-data'
    if (Test-Path -LiteralPath $selfTestData) {
        Remove-Item -LiteralPath $selfTestData -Recurse -Force
    }
    New-Item -ItemType Directory -Path $selfTestData | Out-Null

    $selfTestOutput = & cmd.exe /c ('"' + $appExe + '" --selftest --data-root "' + $selfTestData + '"')
    $selfTestExit = $LASTEXITCODE
    $selfTestOutput | ForEach-Object { Write-Host $_ }

    if ($selfTestExit -ne 0) {
        throw "Self tests failed with exit code $selfTestExit."
    }
}

# --------------------------------------------------------------------------
if (-not $AppOnly) {
    $setupSources = @()
    if (Test-Path -LiteralPath $srcSetup) {
        $setupSources = @(Get-ChildItem -LiteralPath $srcSetup -Filter '*.cs' -File | ForEach-Object { $_.FullName })
    }

    if ($setupSources.Count -eq 0) {
        Write-Host '  (installer sources not present, skipping CleanerSetup.exe)' -ForegroundColor Yellow
    }
    else {
        Write-Step 'Packing the installer payload'
        $payloadRoot = Join-Path $obj 'payload'
        if (Test-Path -LiteralPath $payloadRoot) {
            Remove-Item -LiteralPath $payloadRoot -Recurse -Force
        }

        New-Item -ItemType Directory -Path $payloadRoot | Out-Null
        Copy-Item -LiteralPath $appExe -Destination (Join-Path $payloadRoot 'Cleaner.exe')

        $usageSource = Join-Path $docs 'USAGE.txt'
        if (Test-Path -LiteralPath $usageSource) {
            Copy-Item -LiteralPath $usageSource -Destination (Join-Path $payloadRoot 'README.txt')
        }

        $payloadZip = Join-Path $obj 'Cleaner.Payload.zip'
        if (Test-Path -LiteralPath $payloadZip) {
            Remove-Item -LiteralPath $payloadZip -Force
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $payloadRoot,
            $payloadZip,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)
        Write-Host ("  payload      {0:N0} bytes" -f (Get-Item -LiteralPath $payloadZip).Length) -ForegroundColor Green

        Write-Step 'Compiling CleanerSetup.exe'
        $setupExe = Join-Path $dist 'CleanerSetup.exe'
        $resources = @("$payloadZip,Cleaner.Setup.Payload.zip")

        Invoke-Compile `
            -Output $setupExe `
            -Sources $setupSources `
            -References $references `
            -Icon (Join-Path $assets 'app.ico') `
            -Manifest (Join-Path $scriptRoot 'setup.manifest') `
            -Resources $resources `
            -Pdb (Join-Path $obj 'CleanerSetup.pdb')

        Write-Host ("  CleanerSetup.exe {0:N0} bytes" -f (Get-Item -LiteralPath $setupExe).Length) -ForegroundColor Green
    }
}

# --------------------------------------------------------------------------
Write-Step 'Hashes'
foreach ($file in (Get-ChildItem -LiteralPath $dist -Filter '*.exe' | Sort-Object Name)) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    Write-Host ("  {0,-20} {1}" -f $file.Name, $hash)
}

Write-Host ""
Write-Host 'Build finished.' -ForegroundColor Green
