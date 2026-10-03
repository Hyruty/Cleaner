[CmdletBinding()]
param(
    [switch]$Scheduled,
    [switch]$DryRun,
    [switch]$SelfTest,
    [switch]$UiSelfTest,
    [switch]$UiSmokeTest,
    [switch]$ListCategories,
    [switch]$UninstallAutoClean,
    [string]$Categories = "",
    [string]$ScreenshotPath = "",
    [string]$DataRoot = "",
    [string]$TargetUserSid = "",
    [string]$TargetLocalAppData = "",
    [string]$TargetTemp = ""
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$script:TaskName = "CDriveCleaner_AutoClean"
if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $initialLocalAppData = if ([string]::IsNullOrWhiteSpace($TargetLocalAppData)) {
        $env:LOCALAPPDATA
    }
    else {
        $TargetLocalAppData
    }
    $script:AppRoot = Join-Path $initialLocalAppData "CDriveCleaner"
}
else {
    $script:AppRoot = [IO.Path]::GetFullPath($DataRoot)
}
$script:ConfigPath = Join-Path $script:AppRoot "settings.json"
$script:LogFolder = Join-Path $script:AppRoot "logs"
$script:EngineFolder = Join-Path $script:AppRoot "engine"
$script:ScriptFolder = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:ScriptFile = $MyInvocation.MyCommand.Path
$script:LogPath = Join-Path $script:LogFolder ("clean-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
$script:SkipInitialScan = $false
$script:CleanupUserContext = $null

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $script:IsAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
catch {
    $script:IsAdmin = $false
}

$script:UiStrings = $null
$stringsPath = Join-Path $script:ScriptFolder "strings.zh-CN.json"
if (Test-Path -LiteralPath $stringsPath -PathType Leaf) {
    try {
        $script:UiStrings = Get-Content -LiteralPath $stringsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        $script:UiStrings = $null
    }
}

function Get-Text {
    param([Parameter(Mandatory = $true)][string]$Key)

    if ($null -eq $script:UiStrings) {
        return $Key
    }

    $node = $script:UiStrings
    foreach ($part in $Key.Split(".")) {
        if ($null -eq $node) {
            return $Key
        }

        $property = $node.PSObject.Properties[$part]
        if ($null -eq $property) {
            return $Key
        }
        $node = $property.Value
    }

    return [string]$node
}

function Get-InteractiveUserSid {
    try {
        $currentSessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
        $explorers = Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -ErrorAction Stop

        foreach ($explorer in $explorers) {
            if ($explorer.SessionId -ne $currentSessionId) {
                continue
            }

            $owner = Invoke-CimMethod -InputObject $explorer -MethodName GetOwner -ErrorAction Stop
            if ($owner.ReturnValue -ne 0 -or [string]::IsNullOrWhiteSpace($owner.User)) {
                continue
            }

            $accountName = if ([string]::IsNullOrWhiteSpace($owner.Domain)) {
                $owner.User
            }
            else {
                "{0}\{1}" -f $owner.Domain, $owner.User
            }

            $account = New-Object Security.Principal.NTAccount($accountName)
            return $account.Translate([Security.Principal.SecurityIdentifier]).Value
        }
    }
    catch {
    }

    return $null
}

function Get-LocalAppDataForSid {
    param([Parameter(Mandatory = $true)][string]$Sid)

    $profilePath = $null
    try {
        $profileKey = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$Sid"
        $profilePath = (Get-ItemProperty -LiteralPath $profileKey -Name "ProfileImagePath" -ErrorAction Stop).ProfileImagePath
    }
    catch {
    }

    foreach ($registryPath in @(
        "Registry::HKEY_USERS\$Sid\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders",
        "Registry::HKEY_USERS\$Sid\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"
    )) {
        try {
            $localAppData = (Get-ItemProperty -LiteralPath $registryPath -Name "Local AppData" -ErrorAction Stop)."Local AppData"
            if (-not [string]::IsNullOrWhiteSpace($localAppData)) {
                if (-not [string]::IsNullOrWhiteSpace($profilePath)) {
                    $localAppData = $localAppData.Replace("%USERPROFILE%", $profilePath)
                }
                return [IO.Path]::GetFullPath($localAppData)
            }
        }
        catch {
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($profilePath)) {
        return [IO.Path]::GetFullPath((Join-Path $profilePath "AppData\Local"))
    }

    return $null
}

function Get-CleanupUserContext {
    if ($null -ne $script:CleanupUserContext) {
        return $script:CleanupUserContext
    }

    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $sid = $currentSid
    $localAppData = $env:LOCALAPPDATA
    $temp = $env:TEMP
    $source = "current-process"

    if (-not [string]::IsNullOrWhiteSpace($TargetUserSid)) {
        $sid = $TargetUserSid
        $localAppData = $TargetLocalAppData
        $temp = $TargetTemp
        $source = "passed-from-gui"
    }
    elseif ($script:IsAdmin) {
        $interactiveSid = Get-InteractiveUserSid
        if (-not [string]::IsNullOrWhiteSpace($interactiveSid)) {
            $sid = $interactiveSid
            $interactiveLocalAppData = Get-LocalAppDataForSid -Sid $interactiveSid
            if (-not [string]::IsNullOrWhiteSpace($interactiveLocalAppData)) {
                $localAppData = $interactiveLocalAppData
                $temp = Join-Path $interactiveLocalAppData "Temp"
                $source = "interactive-shell-user"
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        $localAppData = $env:LOCALAPPDATA
    }
    if ([string]::IsNullOrWhiteSpace($temp)) {
        $temp = Join-Path $localAppData "Temp"
    }

    if ([string]::IsNullOrWhiteSpace($DataRoot) -and $source -ne "current-process") {
        $script:AppRoot = Join-Path $localAppData "CDriveCleaner"
        $script:ConfigPath = Join-Path $script:AppRoot "settings.json"
        $script:LogFolder = Join-Path $script:AppRoot "logs"
        $script:EngineFolder = Join-Path $script:AppRoot "engine"
        $script:LogPath = Join-Path $script:LogFolder ("clean-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
    }

    $script:CleanupUserContext = [pscustomobject]@{
        Sid = $sid
        LocalAppData = [IO.Path]::GetFullPath($localAppData)
        Temp = [IO.Path]::GetFullPath($temp)
        Source = $source
    }

    return $script:CleanupUserContext
}

function Initialize-AppStorage {
    foreach ($folder in @($script:AppRoot, $script:LogFolder, $script:EngineFolder)) {
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
            $null = [IO.Directory]::CreateDirectory($folder)
        }
    }

    try {
        $cutoff = (Get-Date).AddDays(-30)
        Get-ChildItem -LiteralPath $script:LogFolder -Filter "clean-*.log" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -lt $cutoff } |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
    catch {
    }
}

function Write-LogMessage {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR")]
        [string]$Level = "INFO"
    )

    try {
        if (-not (Test-Path -LiteralPath $script:LogFolder -PathType Container)) {
            $null = [IO.Directory]::CreateDirectory($script:LogFolder)
        }

        $line = "{0} [{1}] {2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Level, $Message
        Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8
    }
    catch {
    }
}

function Get-CleanupCategories {
    $userContext = Get-CleanupUserContext
    $localAppData = $userContext.LocalAppData
    $programData = [Environment]::GetFolderPath("CommonApplicationData")

    $userTempPaths = @(
        $userContext.Temp,
        (Join-Path $localAppData "Temp")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    $crashPaths = @(
        (Join-Path $localAppData "CrashDumps"),
        (Join-Path $localAppData "Microsoft\Windows\WER\ReportArchive"),
        (Join-Path $localAppData "Microsoft\Windows\WER\ReportQueue")
    )

    $shaderPaths = @(
        (Join-Path $localAppData "D3DSCache"),
        (Join-Path $localAppData "NVIDIA\DXCache"),
        (Join-Path $localAppData "NVIDIA\GLCache"),
        (Join-Path $localAppData "AMD\DxCache"),
        (Join-Path $localAppData "AMD\GLCache"),
        (Join-Path $localAppData "Intel\ShaderCache")
    )

    $systemTempPaths = @(
        (Join-Path $env:SystemRoot "Temp")
    )

    $errorReportPaths = @(
        (Join-Path $programData "Microsoft\Windows\WER\ReportArchive"),
        (Join-Path $programData "Microsoft\Windows\WER\ReportQueue")
    )

    $updateCachePaths = @(
        (Join-Path $env:SystemRoot "SoftwareDistribution\Download")
    )

    return @(
        [pscustomobject]@{
            Id = "userTemp"
            NameKey = "category.userTemp.name"
            DescriptionKey = "category.userTemp.description"
            RequiresAdmin = $false
            DefaultEnabled = $true
            Type = "paths"
            Paths = [string[]]$userTempPaths
        }
        [pscustomobject]@{
            Id = "browserCache"
            NameKey = "category.browserCache.name"
            DescriptionKey = "category.browserCache.description"
            RequiresAdmin = $false
            DefaultEnabled = $true
            Type = "browser"
            Paths = [string[]]@()
        }
        [pscustomobject]@{
            Id = "crashDumps"
            NameKey = "category.crashDumps.name"
            DescriptionKey = "category.crashDumps.description"
            RequiresAdmin = $false
            DefaultEnabled = $true
            Type = "paths"
            Paths = [string[]]$crashPaths
        }
        [pscustomobject]@{
            Id = "shaderCache"
            NameKey = "category.shaderCache.name"
            DescriptionKey = "category.shaderCache.description"
            RequiresAdmin = $false
            DefaultEnabled = $true
            Type = "paths"
            Paths = [string[]]$shaderPaths
        }
        [pscustomobject]@{
            Id = "systemTemp"
            NameKey = "category.systemTemp.name"
            DescriptionKey = "category.systemTemp.description"
            RequiresAdmin = $true
            DefaultEnabled = $false
            Type = "paths"
            Paths = [string[]]$systemTempPaths
        }
        [pscustomobject]@{
            Id = "errorReports"
            NameKey = "category.errorReports.name"
            DescriptionKey = "category.errorReports.description"
            RequiresAdmin = $true
            DefaultEnabled = $false
            Type = "paths"
            Paths = [string[]]$errorReportPaths
        }
        [pscustomobject]@{
            Id = "windowsUpdateCache"
            NameKey = "category.windowsUpdateCache.name"
            DescriptionKey = "category.windowsUpdateCache.description"
            RequiresAdmin = $true
            DefaultEnabled = $false
            Type = "paths"
            Paths = [string[]]$updateCachePaths
        }
        [pscustomobject]@{
            Id = "recycleBin"
            NameKey = "category.recycleBin.name"
            DescriptionKey = "category.recycleBin.description"
            RequiresAdmin = $false
            DefaultEnabled = $false
            Type = "recycle"
            Paths = [string[]]@()
        }
    )
}

function Get-CategoryById {
    param([Parameter(Mandatory = $true)][string]$Id)

    return Get-CleanupCategories | Where-Object { $_.Id -eq $Id } | Select-Object -First 1
}

function Test-BrowserRunning {
    $processes = Get-Process -Name @("msedge", "chrome", "brave") -ErrorAction SilentlyContinue
    if ($null -ne $processes) {
        return $true
    }
    return $false
}

function Resolve-CleanupTargets {
    param([Parameter(Mandatory = $true)]$Category)

    $targets = New-Object System.Collections.Generic.List[string]

    switch ($Category.Type) {
        "paths" {
            foreach ($path in $Category.Paths) {
                if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path)) {
                    $targets.Add([IO.Path]::GetFullPath($path))
                }
            }
        }
        "browser" {
            $localAppData = (Get-CleanupUserContext).LocalAppData
            $roots = @(
                (Join-Path $localAppData "Microsoft\Edge\User Data"),
                (Join-Path $localAppData "Google\Chrome\User Data")
            )

            foreach ($root in $roots) {
                if (-not (Test-Path -LiteralPath $root -PathType Container)) {
                    continue
                }

                $profiles = Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue
                foreach ($profile in $profiles) {
                    foreach ($relativePath in @("Cache", "Code Cache", "GPUCache")) {
                        $candidate = Join-Path $profile.FullName $relativePath
                        if (Test-Path -LiteralPath $candidate -PathType Container) {
                            $targets.Add([IO.Path]::GetFullPath($candidate))
                        }
                    }
                }
            }
        }
    }

    return @($targets | Sort-Object -Unique)
}

function Assert-SafeCleanupTarget {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $trimmedPath = $fullPath.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $root = [IO.Path]::GetPathRoot($fullPath)

    if ([string]::IsNullOrWhiteSpace($root) -or $trimmedPath -ieq $root.TrimEnd([IO.Path]::DirectorySeparatorChar)) {
        throw "Refusing to clean a drive root: $fullPath"
    }

    $protected = @(
        [Environment]::GetFolderPath("UserProfile"),
        [Environment]::GetFolderPath("Desktop"),
        [Environment]::GetFolderPath("MyDocuments"),
        [Environment]::GetFolderPath("MyPictures"),
        [Environment]::GetFolderPath("MyMusic"),
        [Environment]::GetFolderPath("MyVideos")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($safePath in $protected) {
        if ($trimmedPath -ieq ([IO.Path]::GetFullPath($safePath).TrimEnd([IO.Path]::DirectorySeparatorChar))) {
            throw "Refusing to clean a protected folder: $fullPath"
        }
    }

    return $fullPath
}

function Get-PathStatistics {
    param([Parameter(Mandatory = $true)][string]$Path)

    $state = [pscustomobject]@{
        Bytes = [int64]0
        Files = [int64]0
        Folders = [int64]0
        Errors = 0
    }

    if ([IO.File]::Exists($Path)) {
        try {
            $state.Bytes = [int64](New-Object IO.FileInfo -ArgumentList $Path).Length
            $state.Files = 1
        }
        catch {
            $state.Errors = 1
        }
        return $state
    }

    if (-not [IO.Directory]::Exists($Path)) {
        return $state
    }

    $stack = New-Object "System.Collections.Generic.Stack[string]"
    $stack.Push($Path)

    while ($stack.Count -gt 0) {
        $directory = $stack.Pop()
        try {
            foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
                try {
                    $attributes = [IO.File]::GetAttributes($entry)
                    if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
                        $state.Folders++
                        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
                            $stack.Push($entry)
                        }
                    }
                    else {
                        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
                            $state.Files++
                            $state.Bytes = [int64]($state.Bytes + (New-Object IO.FileInfo -ArgumentList $entry).Length)
                        }
                    }
                }
                catch {
                    $state.Errors++
                }
            }
        }
        catch {
            $state.Errors++
        }
    }

    return $state
}

function Add-CleanupSkip {
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    $State.Skipped++

    $itemsProperty = $State.PSObject.Properties["SkippedItems"]
    if ($null -ne $itemsProperty -and $State.SkippedItems.Count -lt 200) {
        $State.SkippedItems.Add(("{0} :: {1}" -f $Path, $Reason))
    }
}

function Remove-SafeTree {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$State
    )

    try {
        $attributes = [IO.File]::GetAttributes($Path)
    }
    catch {
        Add-CleanupSkip -State $State -Path $Path -Reason ("attributes: {0}" -f $_.Exception.Message)
        return
    }

    if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Add-CleanupSkip -State $State -Path $Path -Reason "reparse-point"
        return
    }

    if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
        try {
            $children = [IO.Directory]::GetFileSystemEntries($Path)
        }
        catch {
            Add-CleanupSkip -State $State -Path $Path -Reason ("directory-enumeration: {0}" -f $_.Exception.Message)
            return
        }

        foreach ($child in $children) {
            Remove-SafeTree -Path $child -State $State
        }

        try {
            [IO.Directory]::Delete($Path, $false)
            $State.Folders++
        }
        catch {
            Add-CleanupSkip -State $State -Path $Path -Reason ("directory-delete: {0}" -f $_.Exception.Message)
        }
        return
    }

    $size = [int64]0
    try {
        $size = [int64](New-Object IO.FileInfo -ArgumentList $Path).Length
    }
    catch {
    }

    try {
        if (($attributes -band [IO.FileAttributes]::ReadOnly) -ne 0) {
            [IO.File]::SetAttributes($Path, ($attributes -bxor [IO.FileAttributes]::ReadOnly))
        }
        [IO.File]::Delete($Path)
        $State.Files++
        $State.Bytes = [int64]($State.Bytes + $size)
    }
    catch {
        Add-CleanupSkip -State $State -Path $Path -Reason ("file-delete: {0}" -f $_.Exception.Message)
    }
}

function Remove-CleanupTarget {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$State
    )

    $safePath = Assert-SafeCleanupTarget -Path $Path

    if ([IO.File]::Exists($safePath)) {
        Remove-SafeTree -Path $safePath -State $State
        return
    }

    if (-not [IO.Directory]::Exists($safePath)) {
        return
    }

    try {
        $children = [IO.Directory]::GetFileSystemEntries($safePath)
    }
    catch {
        $State.SkippedCategories.Add($safePath)
        return
    }

    foreach ($child in $children) {
        Remove-SafeTree -Path $child -State $State
    }
}

function Initialize-RecycleBinNative {
    if ($null -eq ([System.Management.Automation.PSTypeName]"RecycleBinNative").Type) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class RecycleBinNative
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHEmptyRecycleBin(IntPtr hwnd, string rootPath, uint flags);
}
'@
    }
}

function Get-RecycleBinShellItemCount {
    $shell = $null
    $recycleBin = $null
    $items = $null

    try {
        $shell = New-Object -ComObject Shell.Application
        $recycleBin = $shell.Namespace(0xA)
        if ($null -eq $recycleBin) {
            return -1
        }

        $items = $recycleBin.Items()
        if ($null -eq $items) {
            return 0
        }

        return [int]$items.Count
    }
    catch {
        return -1
    }
    finally {
        foreach ($comObject in @($items, $recycleBin, $shell)) {
            if ($null -ne $comObject) {
                try {
                    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($comObject)
                }
                catch {
                }
            }
        }
    }
}

function Get-RecycleBinFilesystemStatus {
    $sid = (Get-CleanupUserContext).Sid
    $entries = New-Object System.Collections.Generic.List[string]
    $keys = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)
    $accessible = $true

    foreach ($drive in ([IO.DriveInfo]::GetDrives())) {
        if (-not $drive.IsReady) {
            continue
        }
        if ($drive.DriveType -ne [IO.DriveType]::Fixed -and $drive.DriveType -ne [IO.DriveType]::Removable) {
            continue
        }

        $sidPath = Join-Path $drive.RootDirectory.FullName ("`$Recycle.Bin\{0}" -f $sid)
        if (-not [IO.Directory]::Exists($sidPath)) {
            continue
        }

        try {
            foreach ($entry in [IO.Directory]::GetFileSystemEntries($sidPath)) {
                $name = [IO.Path]::GetFileName($entry)
                if ($name -like '$I*' -or $name -like '$R*') {
                    $entries.Add($entry)
                    $null = $keys.Add($name.Substring(2))
                }
            }
        }
        catch {
            $accessible = $false
        }
    }

    return [pscustomobject]@{
        Count = $keys.Count
        Entries = @($entries)
        Accessible = $accessible
    }
}

function Get-RecycleBinItemCount {
    $shellCount = Get-RecycleBinShellItemCount
    $filesystemStatus = Get-RecycleBinFilesystemStatus

    if ($filesystemStatus.Accessible) {
        return [int]$filesystemStatus.Count
    }

    if ($shellCount -ge 0) {
        return [int]$shellCount
    }

    return -1
}

function Clear-RecycleBinVerified {
    param([int]$TimeoutSeconds = 10)

    $beforeCount = Get-RecycleBinItemCount
    $filesystemBefore = Get-RecycleBinFilesystemStatus
    Initialize-RecycleBinNative
    $method = "ShellApi"
    $apiResult = [RecycleBinNative]::SHEmptyRecycleBin([IntPtr]::Zero, $null, 7)
    $errorMessage = ""

    if ($apiResult -ne 0 -and $apiResult -ne 1) {
        try {
            Clear-RecycleBin -Force -ErrorAction Stop
            $method = "Clear-RecycleBin"
        }
        catch {
            $errorMessage = $_.Exception.Message
        }
    }

    $filesystemState = [pscustomobject]@{
        Bytes = [int64]0
        Files = [int64]0
        Folders = [int64]0
        Skipped = [int64]0
        Errors = [int64]0
        SkippedCategories = New-Object System.Collections.Generic.List[string]
    }

    $filesystemAfterApi = Get-RecycleBinFilesystemStatus
    foreach ($entry in $filesystemAfterApi.Entries) {
        Remove-SafeTree -Path $entry -State $filesystemState
    }

    if ($filesystemAfterApi.Count -gt 0) {
        $method = "ShellApi+Filesystem"
    }

    $afterCount = $beforeCount
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $afterCount = Get-RecycleBinItemCount
        if ($afterCount -eq 0 -or $afterCount -lt 0) {
            break
        }
    }

    $verified = ($afterCount -eq 0)
    $apiSucceeded = ($apiResult -eq 0 -or $apiResult -eq 1 -or $method -eq "Clear-RecycleBin")
    $success = $verified -or ($afterCount -lt 0 -and $apiSucceeded)

    if (-not $success -and [string]::IsNullOrWhiteSpace($errorMessage)) {
        if ($afterCount -ge 0) {
            $errorMessage = "Recycle bin still contains $afterCount item(s) after cleanup."
        }
        else {
            $errorMessage = "Recycle bin item count could not be verified."
        }
    }

    return [pscustomobject]@{
        Success = $success
        BeforeCount = $beforeCount
        AfterCount = $afterCount
        Verified = $verified
        Method = $method
        ErrorMessage = $errorMessage
    }
}

function Invoke-Cleanup {
    param([Parameter(Mandatory = $true)][string[]]$CategoryIds)

    $state = [pscustomobject]@{
        Bytes = [int64]0
        Files = [int64]0
        Folders = [int64]0
        Skipped = [int64]0
        Errors = [int64]0
        RecycleBinCleared = $false
        RecycleBinVerified = $false
        RecycleBinBeforeCount = -1
        RecycleBinAfterCount = -1
        SkippedCategories = New-Object System.Collections.Generic.List[string]
        SkippedItems = New-Object System.Collections.Generic.List[string]
    }

    $allCategories = Get-CleanupCategories
    foreach ($categoryId in $CategoryIds) {
        $category = $allCategories | Where-Object { $_.Id -eq $categoryId } | Select-Object -First 1
        if ($null -eq $category) {
            continue
        }

        $categoryLabel = Get-Text $category.NameKey

        if ($category.RequiresAdmin -and -not $script:IsAdmin) {
            $state.SkippedCategories.Add($categoryLabel)
            continue
        }

        if ($category.Type -eq "browser" -and (Test-BrowserRunning)) {
            $state.SkippedCategories.Add($categoryLabel)
            continue
        }

        if ($category.Type -eq "recycle") {
            $recycleResult = Clear-RecycleBinVerified
            if ($recycleResult.Success) {
                $state.RecycleBinCleared = $true
                $state.RecycleBinVerified = [bool]$recycleResult.Verified
                $state.RecycleBinBeforeCount = [int]$recycleResult.BeforeCount
                $state.RecycleBinAfterCount = [int]$recycleResult.AfterCount
            }
            else {
                $state.SkippedCategories.Add($categoryLabel)
                Write-LogMessage ("Recycle bin cleanup failed. Before={0}; After={1}; Method={2}; Error={3}" -f $recycleResult.BeforeCount, $recycleResult.AfterCount, $recycleResult.Method, $recycleResult.ErrorMessage) "WARN"
            }
            continue
        }

        foreach ($target in (Resolve-CleanupTargets -Category $category)) {
            try {
                Remove-CleanupTarget -Path $target -State $state
            }
            catch {
                $state.Errors++
            }
        }
    }

    return $state
}

function Get-DefaultSettings {
    $categories = @{}
    foreach ($category in (Get-CleanupCategories)) {
        $categories[$category.Id] = [bool]$category.DefaultEnabled
    }

    return [pscustomobject]@{
        version = 1
        autoCleanEnabled = $false
        categories = $categories
    }
}

function Read-Settings {
    $settings = Get-DefaultSettings

    if (-not (Test-Path -LiteralPath $script:ConfigPath -PathType Leaf)) {
        return $settings
    }

    try {
        $raw = Get-Content -LiteralPath $script:ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json

        $autoProperty = $raw.PSObject.Properties["autoCleanEnabled"]
        if ($null -ne $autoProperty) {
            $settings.autoCleanEnabled = [bool]$autoProperty.Value
        }

        $categoryProperty = $raw.PSObject.Properties["categories"]
        if ($null -ne $categoryProperty) {
            foreach ($category in (Get-CleanupCategories)) {
                $entry = $categoryProperty.Value.PSObject.Properties[$category.Id]
                if ($null -ne $entry) {
                    $settings.categories[$category.Id] = [bool]$entry.Value
                }
            }
        }
    }
    catch {
    }

    return $settings
}

function Save-Settings {
    param([Parameter(Mandatory = $true)]$Settings)

    Initialize-AppStorage
    $json = $Settings | ConvertTo-Json -Depth 5
    Set-Content -LiteralPath $script:ConfigPath -Value $json -Encoding UTF8
}

function Get-SystemDriveStatus {
    try {
        $drive = New-Object IO.DriveInfo("C")
        $used = [int64]($drive.TotalSize - $drive.AvailableFreeSpace)
        $percent = [int]([math]::Round(($used / $drive.TotalSize) * 100))
        return [pscustomobject]@{
            Available = $true
            Total = [int64]$drive.TotalSize
            Free = [int64]$drive.AvailableFreeSpace
            Used = $used
            UsedPercent = [math]::Max(0, [math]::Min(100, $percent))
        }
    }
    catch {
        return [pscustomobject]@{
            Available = $false
            Total = [int64]0
            Free = [int64]0
            Used = [int64]0
            UsedPercent = 0
        }
    }
}

function Format-Bytes {
    param([Parameter(Mandatory = $true)][int64]$Bytes)

    $units = @("B", "KB", "MB", "GB", "TB")
    $value = [double]$Bytes
    $index = 0
    while ($value -ge 1024 -and $index -lt ($units.Count - 1)) {
        $value = $value / 1024
        $index++
    }

    if ($index -eq 0) {
        return "{0:N0} {1}" -f $value, $units[$index]
    }
    return "{0:N2} {1}" -f $value, $units[$index]
}

function New-TaskXml {
    param([Parameter(Mandatory = $true)][string]$InstalledScript)

    $userId = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $powerShellPath = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    $arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$InstalledScript`" -Scheduled"
    $workingDirectory = Split-Path -Parent $InstalledScript
    $subscription = "<QueryList><Query Id='0' Path='System'><Select Path='System'>*[System[(Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=13) or (Provider[@Name='User32'] and EventID=1074)]]</Select></Query></QueryList>"

    $safeUserId = [Security.SecurityElement]::Escape($userId)
    $safePowerShellPath = [Security.SecurityElement]::Escape($powerShellPath)
    $safeArguments = [Security.SecurityElement]::Escape($arguments)
    $safeWorkingDirectory = [Security.SecurityElement]::Escape($workingDirectory)
    $safeSubscription = [Security.SecurityElement]::Escape($subscription)

    return @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Run CDriveCleaner at shutdown, with a logon retry for unfinished cleanup.</Description>
  </RegistrationInfo>
  <Triggers>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>$safeSubscription</Subscription>
    </EventTrigger>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>$safeUserId</UserId>
      <Delay>PT2M</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>$safeUserId</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT3M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$safePowerShellPath</Command>
      <Arguments>$safeArguments</Arguments>
      <WorkingDirectory>$safeWorkingDirectory</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
"@
}

function Install-AutoCleanTask {
    Initialize-AppStorage

    $installedScript = Join-Path $script:EngineFolder "CDriveCleaner.ps1"
    $installedStrings = Join-Path $script:EngineFolder "strings.zh-CN.json"
    Copy-Item -LiteralPath $script:ScriptFile -Destination $installedScript -Force

    $sourceStrings = Join-Path $script:ScriptFolder "strings.zh-CN.json"
    if (Test-Path -LiteralPath $sourceStrings -PathType Leaf) {
        Copy-Item -LiteralPath $sourceStrings -Destination $installedStrings -Force
    }

    $taskXml = New-TaskXml -InstalledScript $installedScript
    [xml]$null = $taskXml

    $existing = Get-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        Unregister-ScheduledTask -TaskName $script:TaskName -Confirm:$false
    }

    Register-ScheduledTask -TaskName $script:TaskName -Xml $taskXml -Force | Out-Null
    Write-LogMessage "Automatic cleanup task installed."
    return $installedScript
}

function Uninstall-AutoCleanTask {
    $existing = Get-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        Unregister-ScheduledTask -TaskName $script:TaskName -Confirm:$false
    }
    Write-LogMessage "Automatic cleanup task removed."
}

function Test-AutoCleanTask {
    try {
        $task = Get-ScheduledTask -TaskName $script:TaskName -ErrorAction Stop
        return ($null -ne $task -and $task.State -ne "Disabled")
    }
    catch {
        return $false
    }
}

function Get-SelectedCategoryIds {
    param(
        [Parameter(Mandatory = $true)]$ListView,
        [switch]$CheckedOnly
    )

    if ($CheckedOnly) {
        return @($ListView.CheckedItems | ForEach-Object { [string]$_.Tag })
    }

    return @($ListView.Items | ForEach-Object { [string]$_.Tag })
}

function Update-DiskStatus {
    param(
        [Parameter(Mandatory = $true)]$DriveBar,
        [Parameter(Mandatory = $true)]$FreeLabel,
        [Parameter(Mandatory = $true)]$UsedLabel
    )

    $drive = Get-SystemDriveStatus
    if (-not $drive.Available) {
        $FreeLabel.Text = Get-Text "disk.unavailable"
        $UsedLabel.Text = ""
        $DriveBar.Value = 0
        return
    }

    $DriveBar.Value = $drive.UsedPercent
    $FreeLabel.Text = (Get-Text "disk.free") -f (Format-Bytes -Bytes $drive.Free)
    $UsedLabel.Text = (Get-Text "disk.used") -f (Format-Bytes -Bytes $drive.Used), (Format-Bytes -Bytes $drive.Total)
}

function Update-CategoryEstimates {
    param(
        [Parameter(Mandatory = $true)]$Form,
        [Parameter(Mandatory = $true)]$ListView
    )

    $previousCursor = $Form.Cursor
    $Form.UseWaitCursor = $true
    $Form.Cursor = [Windows.Forms.Cursors]::WaitCursor
    [Windows.Forms.Application]::DoEvents()

    try {
        foreach ($item in $ListView.Items) {
            $category = Get-CategoryById -Id ([string]$item.Tag)
            $item.SubItems[1].Text = Get-Text "list.working"
            [Windows.Forms.Application]::DoEvents()

            if ($null -eq $category) {
                $item.SubItems[1].Text = Get-Text "list.unavailable"
                continue
            }

            if ($category.Type -eq "recycle") {
                $recycleCount = Get-RecycleBinItemCount
                if ($recycleCount -ge 0) {
                    $item.SubItems[1].Text = (Get-Text "list.recycleItems") -f $recycleCount
                }
                else {
                    $item.SubItems[1].Text = Get-Text "list.notCounted"
                }
                continue
            }

            if ($category.RequiresAdmin -and -not $script:IsAdmin) {
                $item.SubItems[1].Text = Get-Text "list.needsAdmin"
                continue
            }

            if ($category.Type -eq "browser" -and (Test-BrowserRunning)) {
                $item.SubItems[1].Text = Get-Text "list.browserRunning"
                continue
            }

            $bytes = [int64]0
            foreach ($target in (Resolve-CleanupTargets -Category $category)) {
                $stats = Get-PathStatistics -Path $target
                $bytes = [int64]($bytes + $stats.Bytes)
            }

            if ($bytes -le 0) {
                $item.SubItems[1].Text = Get-Text "list.noTargets"
            }
            else {
                $item.SubItems[1].Text = Format-Bytes -Bytes $bytes
            }
        }
    }
    finally {
        $Form.Cursor = $previousCursor
        $Form.UseWaitCursor = $false
        [Windows.Forms.Application]::DoEvents()
    }
}

function Save-SelectionFromList {
    param(
        [Parameter(Mandatory = $true)]$ListView,
        [Parameter(Mandatory = $true)]$Settings
    )

    foreach ($item in $ListView.Items) {
        $id = [string]$item.Tag
        $Settings.categories[$id] = [bool]$item.Checked
    }

    Save-Settings -Settings $Settings
}

function Build-MainForm {
    param([Parameter(Mandatory = $true)]$Settings)

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [Windows.Forms.Application]::EnableVisualStyles()

    $backgroundColor = [Drawing.ColorTranslator]::FromHtml("#F3F6F8")
    $headerColor = [Drawing.ColorTranslator]::FromHtml("#17324D")
    $accentColor = [Drawing.ColorTranslator]::FromHtml("#0B7A75")
    $textColor = [Drawing.ColorTranslator]::FromHtml("#17212B")
    $mutedColor = [Drawing.ColorTranslator]::FromHtml("#5C6B7A")
    $borderColor = [Drawing.ColorTranslator]::FromHtml("#D9E0E6")
    $surfaceColor = [Drawing.Color]::White
    $warningColor = [Drawing.ColorTranslator]::FromHtml("#A35B00")

    $form = New-Object Windows.Forms.Form
    $form.Text = Get-Text "app.title"
    $form.ClientSize = New-Object Drawing.Size(940, 720)
    $form.MinimumSize = New-Object Drawing.Size(840, 640)
    $form.StartPosition = [Windows.Forms.FormStartPosition]::CenterScreen
    $form.BackColor = $backgroundColor
    $form.ForeColor = $textColor
    $form.Font = New-Object Drawing.Font("Segoe UI", 10)
    $form.AutoScaleMode = [Windows.Forms.AutoScaleMode]::Dpi
    $script:MainForm = $form

    $header = New-Object Windows.Forms.Panel
    $header.Dock = [Windows.Forms.DockStyle]::Top
    $header.Height = 106
    $header.BackColor = $headerColor

    $titleLabel = New-Object Windows.Forms.Label
    $titleLabel.Text = Get-Text "app.title"
    $titleLabel.ForeColor = [Drawing.Color]::White
    $titleLabel.Font = New-Object Drawing.Font("Segoe UI", 20, [Drawing.FontStyle]::Bold)
    $titleLabel.AutoSize = $true
    $titleLabel.Location = New-Object Drawing.Point(24, 17)
    $header.Controls.Add($titleLabel)

    $subtitleLabel = New-Object Windows.Forms.Label
    $subtitleLabel.Text = Get-Text "app.subtitle"
    $subtitleLabel.ForeColor = [Drawing.ColorTranslator]::FromHtml("#D9E8F2")
    $subtitleLabel.Font = New-Object Drawing.Font("Segoe UI", 10)
    $subtitleLabel.AutoSize = $true
    $subtitleLabel.Location = New-Object Drawing.Point(27, 62)
    $header.Controls.Add($subtitleLabel)

    $tagLabel = New-Object Windows.Forms.Label
    $tagLabel.Text = Get-Text "app.tag"
    $tagLabel.TextAlign = [Drawing.ContentAlignment]::MiddleCenter
    $tagLabel.ForeColor = [Drawing.Color]::White
    $tagLabel.BackColor = [Drawing.ColorTranslator]::FromHtml("#235C6E")
    $tagLabel.Font = New-Object Drawing.Font("Segoe UI", 9, [Drawing.FontStyle]::Bold)
    $tagLabel.Dock = [Windows.Forms.DockStyle]::Right
    $tagLabel.Width = 116
    $header.Controls.Add($tagLabel)

    $layout = New-Object Windows.Forms.TableLayoutPanel
    $layout.Dock = [Windows.Forms.DockStyle]::Fill
    $layout.Padding = New-Object Windows.Forms.Padding(22, 18, 22, 14)
    $layout.ColumnCount = 1
    $layout.RowCount = 4
    $layout.BackColor = $backgroundColor
    $null = $layout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute, 94)))
    $null = $layout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent, 100)))
    $null = $layout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute, 80)))
    $null = $layout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute, 30)))

    $diskPanel = New-Object Windows.Forms.TableLayoutPanel
    $diskPanel.Dock = [Windows.Forms.DockStyle]::Fill
    $diskPanel.BackColor = $surfaceColor
    $diskPanel.Padding = New-Object Windows.Forms.Padding(16, 12, 16, 10)
    $diskPanel.ColumnCount = 2
    $diskPanel.RowCount = 3
    $null = $diskPanel.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent, 60)))
    $null = $diskPanel.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent, 40)))
    $null = $diskPanel.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute, 28)))
    $null = $diskPanel.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute, 22)))
    $null = $diskPanel.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute, 22)))

    $diskTitle = New-Object Windows.Forms.Label
    $diskTitle.Text = Get-Text "disk.title"
    $diskTitle.Font = New-Object Drawing.Font("Segoe UI", 11, [Drawing.FontStyle]::Bold)
    $diskTitle.ForeColor = $textColor
    $diskTitle.Dock = [Windows.Forms.DockStyle]::Fill
    $diskTitle.TextAlign = [Drawing.ContentAlignment]::MiddleLeft

    $freeLabel = New-Object Windows.Forms.Label
    $freeLabel.Text = Get-Text "disk.loading"
    $freeLabel.ForeColor = $accentColor
    $freeLabel.Font = New-Object Drawing.Font("Segoe UI", 10, [Drawing.FontStyle]::Bold)
    $freeLabel.Dock = [Windows.Forms.DockStyle]::Fill
    $freeLabel.TextAlign = [Drawing.ContentAlignment]::MiddleRight
    $script:FreeLabel = $freeLabel

    $driveBar = New-Object Windows.Forms.ProgressBar
    $driveBar.Dock = [Windows.Forms.DockStyle]::Fill
    $driveBar.Minimum = 0
    $driveBar.Maximum = 100
    $driveBar.Style = [Windows.Forms.ProgressBarStyle]::Continuous
    $driveBar.Margin = New-Object Windows.Forms.Padding(0, 2, 0, 2)
    $script:DriveBar = $driveBar
    $diskPanel.Controls.Add($driveBar, 0, 1)
    $diskPanel.SetColumnSpan($driveBar, 2)

    $usedLabel = New-Object Windows.Forms.Label
    $usedLabel.Text = ""
    $usedLabel.ForeColor = $mutedColor
    $usedLabel.Dock = [Windows.Forms.DockStyle]::Fill
    $usedLabel.TextAlign = [Drawing.ContentAlignment]::MiddleLeft
    $script:UsedLabel = $usedLabel
    $diskPanel.Controls.Add($usedLabel, 0, 2)
    $diskPanel.SetColumnSpan($usedLabel, 2)

    $diskPanel.Controls.Add($diskTitle, 0, 0)
    $diskPanel.Controls.Add($freeLabel, 1, 0)
    $layout.Controls.Add($diskPanel, 0, 0)

    $listView = New-Object Windows.Forms.ListView
    $listView.Dock = [Windows.Forms.DockStyle]::Fill
    $listView.View = [Windows.Forms.View]::Details
    $listView.CheckBoxes = $true
    $listView.FullRowSelect = $true
    $listView.GridLines = $false
    $listView.BorderStyle = [Windows.Forms.BorderStyle]::FixedSingle
    $listView.HeaderStyle = [Windows.Forms.ColumnHeaderStyle]::Nonclickable
    $listView.HideSelection = $false
    $listView.BackColor = $surfaceColor
    $listView.ForeColor = $textColor
    $listView.Font = New-Object Drawing.Font("Segoe UI", 10)
    $script:MainListView = $listView
    $null = $listView.Columns.Add((Get-Text "list.item"), 280)
    $null = $listView.Columns.Add((Get-Text "list.estimate"), 140)
    $null = $listView.Columns.Add((Get-Text "list.description"), 440)

    foreach ($category in (Get-CleanupCategories)) {
        $categoryName = Get-Text $category.NameKey
        if ($category.RequiresAdmin) {
            $categoryName = "[{0}] {1}" -f (Get-Text "category.adminBadge"), $categoryName
        }

        $description = Get-Text $category.DescriptionKey
        if ($category.RequiresAdmin -and -not $script:IsAdmin) {
            $description = "{0} {1}" -f $description, (Get-Text "list.adminSuffix")
        }

        $item = New-Object Windows.Forms.ListViewItem($categoryName)
        $null = $item.SubItems.Add((Get-Text "list.notScanned"))
        $null = $item.SubItems.Add($description)
        $item.Tag = $category.Id
        $item.Checked = [bool]$Settings.categories[$category.Id]
        $null = $listView.Items.Add($item)
    }

    $listView.Add_Resize({
        $fixedWidth = 444
        if ($script:MainListView.ClientSize.Width -gt $fixedWidth) {
            $script:MainListView.Columns[2].Width = $script:MainListView.ClientSize.Width - $fixedWidth
        }
    })

    $layout.Controls.Add($listView, 0, 1)

    $bottomPanel = New-Object Windows.Forms.Panel
    $bottomPanel.Dock = [Windows.Forms.DockStyle]::Fill
    $bottomPanel.BackColor = $backgroundColor

    $autoCheck = New-Object Windows.Forms.CheckBox
    $autoCheck.Text = Get-Text "auto.label"
    $autoCheck.Font = New-Object Drawing.Font("Segoe UI", 10, [Drawing.FontStyle]::Bold)
    $autoCheck.ForeColor = $textColor
    $autoCheck.AutoSize = $true
    $autoCheck.Location = New-Object Drawing.Point(2, 7)
    $script:AutoCheck = $autoCheck
    $bottomPanel.Controls.Add($autoCheck)

    $autoNote = New-Object Windows.Forms.Label
    $autoNote.Text = Get-Text "auto.note"
    $autoNote.ForeColor = $mutedColor
    $autoNote.Font = New-Object Drawing.Font("Segoe UI", 9)
    $autoNote.AutoSize = $false
    $autoNote.Size = New-Object Drawing.Size(465, 36)
    $autoNote.Location = New-Object Drawing.Point(5, 36)
    $bottomPanel.Controls.Add($autoNote)

    $buttonPanel = New-Object Windows.Forms.FlowLayoutPanel
    $buttonPanel.FlowDirection = [Windows.Forms.FlowDirection]::RightToLeft
    $buttonPanel.WrapContents = $false
    $buttonPanel.Dock = [Windows.Forms.DockStyle]::Right
    $buttonPanel.Width = 430
    $buttonPanel.Padding = New-Object Windows.Forms.Padding(0, 8, 0, 0)
    $buttonPanel.BackColor = $backgroundColor

    $cleanButton = New-Object Windows.Forms.Button
    $cleanButton.Text = Get-Text "buttons.clean"
    $cleanButton.Size = New-Object Drawing.Size(124, 40)
    $cleanButton.BackColor = $accentColor
    $cleanButton.ForeColor = [Drawing.Color]::White
    $cleanButton.FlatStyle = [Windows.Forms.FlatStyle]::Flat
    $cleanButton.FlatAppearance.BorderSize = 0
    $cleanButton.Font = New-Object Drawing.Font("Segoe UI", 10, [Drawing.FontStyle]::Bold)
    $cleanButton.Cursor = [Windows.Forms.Cursors]::Hand
    $script:CleanButton = $cleanButton

    $scanButton = New-Object Windows.Forms.Button
    $scanButton.Text = Get-Text "buttons.scan"
    $scanButton.Size = New-Object Drawing.Size(112, 40)
    $scanButton.BackColor = [Drawing.Color]::White
    $scanButton.ForeColor = $textColor
    $scanButton.FlatStyle = [Windows.Forms.FlatStyle]::Flat
    $scanButton.FlatAppearance.BorderColor = $borderColor
    $scanButton.Cursor = [Windows.Forms.Cursors]::Hand
    $script:ScanButton = $scanButton

    $elevateButton = New-Object Windows.Forms.Button
    $elevateButton.Text = Get-Text "buttons.elevate"
    $elevateButton.Size = New-Object Drawing.Size(172, 40)
    $elevateButton.BackColor = [Drawing.Color]::White
    $elevateButton.ForeColor = $textColor
    $elevateButton.FlatStyle = [Windows.Forms.FlatStyle]::Flat
    $elevateButton.FlatAppearance.BorderColor = $borderColor
    $elevateButton.Cursor = [Windows.Forms.Cursors]::Hand
    $elevateButton.Visible = -not $script:IsAdmin
    $script:ElevateButton = $elevateButton

    $buttonPanel.Controls.Add($cleanButton)
    $buttonPanel.Controls.Add($scanButton)
    $buttonPanel.Controls.Add($elevateButton)
    $bottomPanel.Controls.Add($buttonPanel)
    $layout.Controls.Add($bottomPanel, 0, 2)

    $statusPanel = New-Object Windows.Forms.Panel
    $statusPanel.Dock = [Windows.Forms.DockStyle]::Fill
    $statusPanel.BackColor = $backgroundColor

    $statusLabel = New-Object Windows.Forms.Label
    $statusLabel.Text = Get-Text "status.ready"
    $statusLabel.ForeColor = $mutedColor
    $statusLabel.AutoSize = $false
    $statusLabel.Dock = [Windows.Forms.DockStyle]::Fill
    $statusLabel.TextAlign = [Drawing.ContentAlignment]::MiddleLeft
    $script:StatusLabel = $statusLabel

    $logLink = New-Object Windows.Forms.LinkLabel
    $logLink.Text = Get-Text "links.logs"
    $logLink.AutoSize = $true
    $logLink.LinkColor = $accentColor
    $logLink.ActiveLinkColor = $accentColor
    $logLink.Anchor = [Windows.Forms.AnchorStyles]::Top -bor [Windows.Forms.AnchorStyles]::Right
    $logLink.Location = New-Object Drawing.Point(($form.ClientSize.Width - 130), 6)

    $statusPanel.Controls.Add($statusLabel)
    $statusPanel.Controls.Add($logLink)
    $layout.Controls.Add($statusPanel, 0, 3)

    $form.Controls.Add($layout)
    $form.Controls.Add($header)

    $script:MainFormSettings = $Settings
    $script:SuppressAutoEvent = $true
    $autoCheck.Checked = [bool]$Settings.autoCleanEnabled
    $script:SuppressAutoEvent = $false

    $autoCheck.Add_CheckedChanged({
        if ($script:SuppressAutoEvent) {
            return
        }

        try {
            if ($script:AutoCheck.Checked) {
                $answer = [Windows.Forms.MessageBox]::Show(
                    (Get-Text "auto.enableConfirm"),
                    (Get-Text "auto.confirmTitle"),
                    [Windows.Forms.MessageBoxButtons]::YesNo,
                    [Windows.Forms.MessageBoxIcon]::Question
                )

                if ($answer -ne [Windows.Forms.DialogResult]::Yes) {
                    $script:SuppressAutoEvent = $true
                    $script:AutoCheck.Checked = $false
                    $script:SuppressAutoEvent = $false
                    return
                }

                Install-AutoCleanTask | Out-Null
                $script:MainFormSettings.autoCleanEnabled = $true
                Save-Settings -Settings $script:MainFormSettings
                $script:StatusLabel.Text = Get-Text "status.autoEnabled"
            }
            else {
                $answer = [Windows.Forms.MessageBox]::Show(
                    (Get-Text "auto.disableConfirm"),
                    (Get-Text "auto.confirmTitle"),
                    [Windows.Forms.MessageBoxButtons]::YesNo,
                    [Windows.Forms.MessageBoxIcon]::Question
                )

                if ($answer -ne [Windows.Forms.DialogResult]::Yes) {
                    $script:SuppressAutoEvent = $true
                    $script:AutoCheck.Checked = $true
                    $script:SuppressAutoEvent = $false
                    return
                }

                Uninstall-AutoCleanTask
                $script:MainFormSettings.autoCleanEnabled = $false
                Save-Settings -Settings $script:MainFormSettings
                $script:StatusLabel.Text = Get-Text "status.autoDisabled"
            }
        }
        catch {
            [Windows.Forms.MessageBox]::Show(
                ((Get-Text "auto.failed") -f $_.Exception.Message),
                (Get-Text "errors.title"),
                [Windows.Forms.MessageBoxButtons]::OK,
                [Windows.Forms.MessageBoxIcon]::Error
            ) | Out-Null

            $script:SuppressAutoEvent = $true
            $script:AutoCheck.Checked = [bool]$script:MainFormSettings.autoCleanEnabled
            $script:SuppressAutoEvent = $false
        }
    })

    $scanButton.Add_Click({
        $script:StatusLabel.Text = Get-Text "status.scanning"
        Update-CategoryEstimates -Form $script:MainForm -ListView $script:MainListView
        Update-DiskStatus -DriveBar $script:DriveBar -FreeLabel $script:FreeLabel -UsedLabel $script:UsedLabel
        $script:StatusLabel.Text = Get-Text "status.scanDone"
    })

    $cleanButton.Add_Click({
        $selectedIds = Get-SelectedCategoryIds -ListView $script:MainListView -CheckedOnly
        if ($selectedIds.Count -eq 0) {
            [Windows.Forms.MessageBox]::Show(
                (Get-Text "confirm.noSelection"),
                (Get-Text "app.title"),
                [Windows.Forms.MessageBoxButtons]::OK,
                [Windows.Forms.MessageBoxIcon]::Information
            ) | Out-Null
            return
        }

        $confirmText = Get-Text "confirm.clean"
        if ($selectedIds -contains "recycleBin") {
            $confirmText = "{0}`r`n`r`n{1}" -f $confirmText, (Get-Text "confirm.recycle")
        }

        $answer = [Windows.Forms.MessageBox]::Show(
            $confirmText,
            (Get-Text "confirm.title"),
            [Windows.Forms.MessageBoxButtons]::YesNo,
            [Windows.Forms.MessageBoxIcon]::Warning
        )

        if ($answer -ne [Windows.Forms.DialogResult]::Yes) {
            return
        }

        $script:MainForm.UseWaitCursor = $true
        $script:StatusLabel.Text = Get-Text "status.cleaning"
        [Windows.Forms.Application]::DoEvents()

        try {
            $cleanup = Invoke-Cleanup -CategoryIds $selectedIds
            $resultText = (Get-Text "result.summary") -f (Format-Bytes -Bytes $cleanup.Bytes), $cleanup.Files, $cleanup.Skipped, $cleanup.Errors

            if ($cleanup.RecycleBinCleared) {
                if ($cleanup.RecycleBinVerified -and $cleanup.RecycleBinBeforeCount -gt 0) {
                    $resultText = "{0}`r`n{1}" -f $resultText, ((Get-Text "result.recycleDoneVerified") -f $cleanup.RecycleBinBeforeCount)
                }
                elseif ($cleanup.RecycleBinBeforeCount -eq 0) {
                    $resultText = "{0}`r`n{1}" -f $resultText, (Get-Text "result.recycleAlreadyEmpty")
                }
                else {
                    $resultText = "{0}`r`n{1}" -f $resultText, (Get-Text "result.recycleDone")
                }
            }

            if ($cleanup.SkippedCategories.Count -gt 0) {
                $resultText = "{0}`r`n`r`n{1}" -f $resultText, ((Get-Text "result.skipped") -f ($cleanup.SkippedCategories -join ", "))
            }

            if ($cleanup.Skipped -gt 0) {
                $resultText = "{0}`r`n{1}" -f $resultText, (Get-Text "result.skippedLogged")
            }

            Write-LogMessage ("Manual cleanup complete. Bytes={0}; Files={1}; Folders={2}; Skipped={3}; Errors={4}" -f $cleanup.Bytes, $cleanup.Files, $cleanup.Folders, $cleanup.Skipped, $cleanup.Errors)
            if ($cleanup.SkippedItems.Count -gt 0) {
                Write-LogMessage ("Skipped items: {0}" -f (@($cleanup.SkippedItems | Select-Object -First 50) -join " | ")) "WARN"
            }
            $script:StatusLabel.Text = Get-Text "status.cleanDone"

            [Windows.Forms.MessageBox]::Show(
                $resultText,
                (Get-Text "result.title"),
                [Windows.Forms.MessageBoxButtons]::OK,
                [Windows.Forms.MessageBoxIcon]::Information
            ) | Out-Null
        }
        catch {
            Write-LogMessage ("Manual cleanup failed: {0}" -f $_.Exception.Message) "ERROR"
            [Windows.Forms.MessageBox]::Show(
                ((Get-Text "result.failed") -f $_.Exception.Message),
                (Get-Text "errors.title"),
                [Windows.Forms.MessageBoxButtons]::OK,
                [Windows.Forms.MessageBoxIcon]::Error
            ) | Out-Null
            $script:StatusLabel.Text = Get-Text "status.failed"
        }
        finally {
            $script:MainForm.UseWaitCursor = $false
        }

        Update-DiskStatus -DriveBar $script:DriveBar -FreeLabel $script:FreeLabel -UsedLabel $script:UsedLabel
        Update-CategoryEstimates -Form $script:MainForm -ListView $script:MainListView
    })

    $elevateButton.Add_Click({
        try {
            $powerShellPath = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
            $userContext = Get-CleanupUserContext
            $arguments = "-NoProfile -ExecutionPolicy Bypass -STA -File `"$script:ScriptFile`" -TargetUserSid `"$($userContext.Sid)`" -TargetLocalAppData `"$($userContext.LocalAppData)`" -TargetTemp `"$($userContext.Temp)`""
            Start-Process -FilePath $powerShellPath -Verb RunAs -ArgumentList $arguments
            $script:MainForm.Close()
        }
        catch {
            [Windows.Forms.MessageBox]::Show(
                (Get-Text "elevate.cancelled"),
                (Get-Text "app.title"),
                [Windows.Forms.MessageBoxButtons]::OK,
                [Windows.Forms.MessageBoxIcon]::Information
            ) | Out-Null
        }
    })

    $logLink.Add_LinkClicked({
        try {
            Start-Process -FilePath "explorer.exe" -ArgumentList ("`"{0}`"" -f $script:LogFolder)
        }
        catch {
        }
    })

    $form.Add_Shown({
        Update-DiskStatus -DriveBar $script:DriveBar -FreeLabel $script:FreeLabel -UsedLabel $script:UsedLabel
        if ($script:SkipInitialScan) {
            return
        }
        $script:StatusLabel.Text = Get-Text "status.scanning"
        Update-CategoryEstimates -Form $script:MainForm -ListView $script:MainListView
        $script:StatusLabel.Text = Get-Text "status.ready"
    })

    $form.Add_FormClosing({
        try {
            Save-SelectionFromList -ListView $script:MainListView -Settings $script:MainFormSettings
        }
        catch {
        }
    })

    return $form
}

function Show-MainWindow {
    param([Parameter(Mandatory = $true)]$Settings)

    $form = Build-MainForm -Settings $Settings
    [Windows.Forms.Application]::Run($form)
}

function Invoke-SelfTest {
    $categories = Get-CleanupCategories
    if ($categories.Count -lt 8) {
        throw "Expected at least 8 cleanup categories."
    }

    $ids = @($categories | ForEach-Object { $_.Id })
    if (($ids | Sort-Object -Unique).Count -ne $ids.Count) {
        throw "Cleanup category ids are not unique."
    }

    foreach ($category in $categories) {
        if ([string]::IsNullOrWhiteSpace((Get-Text $category.NameKey))) {
            throw "Missing category label: $($category.NameKey)"
        }
        if ([string]::IsNullOrWhiteSpace((Get-Text $category.DescriptionKey))) {
            throw "Missing category description: $($category.DescriptionKey)"
        }
    }

    Initialize-RecycleBinNative
    $recycleItems = Get-RecycleBinItemCount
    $recycleApiState = "not-run"
    if ($recycleItems -eq 0) {
        $recycleProbe = Clear-RecycleBinVerified -TimeoutSeconds 1
        if (-not $recycleProbe.Success) {
            throw "Recycle bin API self-test failed: $($recycleProbe.ErrorMessage)"
        }
        $recycleApiState = $recycleProbe.Method
    }

    $sampleScript = Join-Path $script:ScriptFolder "CDriveCleaner.ps1"
    [xml]$taskXml = New-TaskXml -InstalledScript $sampleScript
    if ($null -eq $taskXml.Task) {
        throw "Task XML did not produce a Task element."
    }

    $testRoot = Join-Path $script:AppRoot ("selftest-{0}" -f [Guid]::NewGuid().ToString("N"))
    try {
        $null = [IO.Directory]::CreateDirectory((Join-Path $testRoot "nested"))
        [IO.File]::WriteAllText((Join-Path $testRoot "one.tmp"), "test")
        [IO.File]::WriteAllText((Join-Path $testRoot "nested\two.tmp"), "test")

        $deleteState = [pscustomobject]@{
            Bytes = [int64]0
            Files = [int64]0
            Folders = [int64]0
            Skipped = [int64]0
            Errors = [int64]0
            SkippedCategories = New-Object System.Collections.Generic.List[string]
        }

        Remove-CleanupTarget -Path $testRoot -State $deleteState
        if ([IO.Directory]::GetFileSystemEntries($testRoot).Count -ne 0) {
            throw "Safe deletion self-test did not empty the test folder."
        }
        if ($deleteState.Files -ne 2) {
            throw "Safe deletion self-test expected 2 files, got $($deleteState.Files)."
        }
    }
    finally {
        if ([IO.Directory]::Exists($testRoot)) {
            [IO.Directory]::Delete($testRoot, $true)
        }
    }

    Write-Output ("SELF_TEST_OK categories={0} admin={1} recycleItems={2} recycleApi={3}" -f $categories.Count, $script:IsAdmin, $recycleItems, $recycleApiState)
}

function Invoke-DryRun {
    $settings = Read-Settings
    $selectedIds = @()

    if (-not [string]::IsNullOrWhiteSpace($Categories)) {
        $selectedIds = @($Categories.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    else {
        $selectedIds = @(Get-CleanupCategories | Where-Object { [bool]$settings.categories[$_.Id] } | ForEach-Object { $_.Id })
    }

    Write-Output ("Selected categories: {0}" -f ($selectedIds -join ", "))

    foreach ($category in (Get-CleanupCategories | Where-Object { $selectedIds -contains $_.Id })) {
        if ($category.RequiresAdmin -and -not $script:IsAdmin) {
            Write-Output ("SKIP {0} requires administrator" -f $category.Id)
            continue
        }

        if ($category.Type -eq "browser" -and (Test-BrowserRunning)) {
            Write-Output ("SKIP {0} browser is running" -f $category.Id)
            continue
        }

        if ($category.Type -eq "recycle") {
            Write-Output ("DRY-RUN {0} currentItems={1} would clear recycle bin" -f $category.Id, (Get-RecycleBinItemCount))
            continue
        }

        $bytes = [int64]0
        $targets = @(Resolve-CleanupTargets -Category $category)
        foreach ($target in $targets) {
            $stats = Get-PathStatistics -Path $target
            $bytes = [int64]($bytes + $stats.Bytes)
        }

        Write-Output ("DRY-RUN {0} targets={1} bytes={2} size={3}" -f $category.Id, $targets.Count, $bytes, (Format-Bytes -Bytes $bytes))
    }
}

function Invoke-ScheduledCleanup {
    $settings = Read-Settings
    if (-not $settings.autoCleanEnabled) {
        Write-LogMessage "Scheduled cleanup skipped because automatic cleanup is disabled."
        return
    }

    $mutex = New-Object Threading.Mutex($false, "Local\CDriveCleaner_AutoClean")
    $hasLock = $false

    try {
        $hasLock = $mutex.WaitOne(0)
        if (-not $hasLock) {
            Write-LogMessage "Scheduled cleanup skipped because another cleanup is running." "WARN"
            return
        }

        $selectedIds = @(Get-CleanupCategories | Where-Object { [bool]$settings.categories[$_.Id] } | ForEach-Object { $_.Id })
        $cleanup = Invoke-Cleanup -CategoryIds $selectedIds
        Write-LogMessage ("Scheduled cleanup complete. Bytes={0}; Files={1}; Folders={2}; Skipped={3}; Errors={4}" -f $cleanup.Bytes, $cleanup.Files, $cleanup.Folders, $cleanup.Skipped, $cleanup.Errors)
        if ($cleanup.SkippedItems.Count -gt 0) {
            Write-LogMessage ("Skipped items: {0}" -f (@($cleanup.SkippedItems | Select-Object -First 50) -join " | ")) "WARN"
        }
    }
    catch {
        Write-LogMessage ("Scheduled cleanup failed: {0}" -f $_.Exception.Message) "ERROR"
    }
    finally {
        if ($hasLock) {
            $mutex.ReleaseMutex()
        }
        $mutex.Dispose()
    }
}

Initialize-AppStorage

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

if ($ListCategories) {
    foreach ($category in (Get-CleanupCategories)) {
        $scope = if ($category.RequiresAdmin) { "admin" } else { "user" }
        Write-Output ("{0}`t{1}`tdefault={2}`t{3}" -f $category.Id, $scope, $category.DefaultEnabled, $category.Type)
    }
    exit 0
}

if ($UninstallAutoClean) {
    try {
        Uninstall-AutoCleanTask
        $settings = Read-Settings
        $settings.autoCleanEnabled = $false
        Save-Settings -Settings $settings
        Write-Output (Get-Text "uninstall.done")
        exit 0
    }
    catch {
        Write-Output ((Get-Text "uninstall.failed") -f $_.Exception.Message)
        exit 1
    }
}

if ($Scheduled) {
    Invoke-ScheduledCleanup
    exit 0
}

if ($DryRun) {
    Invoke-DryRun
    exit 0
}

if ($UiSmokeTest) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    [Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::CatchException)
    $script:UiSmokeError = $null
    $script:UiSmokeShown = $false
    $script:SkipInitialScan = $true

    $settings = Read-Settings
    $form = Build-MainForm -Settings $settings
    $script:UiSmokeForm = $form
    $smokeTimer = New-Object Windows.Forms.Timer
    $script:UiSmokeTimer = $smokeTimer
    $smokeTimer.Interval = 900
    $smokeTimer.Add_Tick({
        $script:UiSmokeTimer.Stop()
        $script:UiSmokeForm.Close()
    })

    $exceptionHandler = [Threading.ThreadExceptionEventHandler]{
        param($sender, $eventArgs)
        $script:UiSmokeError = $eventArgs.Exception
        $script:UiSmokeTimer.Stop()
        $script:UiSmokeForm.Close()
    }

    [Windows.Forms.Application]::add_ThreadException($exceptionHandler)
    $form.Add_Shown({
        $script:UiSmokeShown = $true
        $script:UiSmokeTimer.Start()
    })

    [Windows.Forms.Application]::Run($form)
    [Windows.Forms.Application]::remove_ThreadException($exceptionHandler)
    $smokeTimer.Dispose()
    $form.Dispose()

    if ($null -ne $script:UiSmokeError) {
        throw $script:UiSmokeError
    }
    if (-not $script:UiSmokeShown) {
        throw "The main form did not raise its Shown event."
    }

    Write-Output "UI_SMOKE_TEST_OK"
    exit 0
}

if ($UiSelfTest -or -not [string]::IsNullOrWhiteSpace($ScreenshotPath)) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $settings = Read-Settings
    $form = Build-MainForm -Settings $settings

    if (-not [string]::IsNullOrWhiteSpace($ScreenshotPath)) {
        $script:SkipInitialScan = $true
        $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
        $form.Location = New-Object Drawing.Point(-2200, -2200)

        $captureTimer = New-Object Windows.Forms.Timer
        $captureTimer.Interval = 350
        $captureTimer.Add_Tick({
            $captureTimer.Stop()
            $bitmap = New-Object Drawing.Bitmap($form.Width, $form.Height)
            $form.DrawToBitmap($bitmap, (New-Object Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
            $bitmap.Save($ScreenshotPath, [Drawing.Imaging.ImageFormat]::Png)
            $bitmap.Dispose()
            $form.Close()
        })

        $form.Show()
        $captureTimer.Start()
        [Windows.Forms.Application]::Run($form)
        $captureTimer.Dispose()
    }
    else {
        $form.CreateControl()
        $form.PerformLayout()
    }

    $form.Dispose()
    Write-Output "UI_SELF_TEST_OK"
    exit 0
}

$initialSettings = Read-Settings
if ($initialSettings.autoCleanEnabled -and -not (Test-AutoCleanTask)) {
    $initialSettings.autoCleanEnabled = $false
    Save-Settings -Settings $initialSettings
}

Show-MainWindow -Settings $initialSettings
