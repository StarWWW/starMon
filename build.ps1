# StarMon build script
#
# Builds without Visual Studio, using only the system .NET SDK.
#
#   .\build.ps1                    build
#   .\build.ps1 -Test              build, then run the self-test
#   .\build.ps1 -Render window     build, then draw a piece of the interface
#
# Two things make this project awkward to build with the SDK's MSBuild alone,
# and both are handled here.
#
#  1. It targets .NET Framework 4.8, whose reference assemblies the SDK does
#     not ship, and it uses Microsoft.Management.Infrastructure, which is not
#     in the reference pack at all. Both are pointed at explicitly below.
#     Staying on net48 is not nostalgia: that assembly and System.Management
#     need NuGet packages on modern .NET, and this machine cannot reach NuGet.
#
#  2. The interface is WPF, so the .xaml files have to be compiled to BAML
#     before the C# compiler runs - and the markup compiler ships inside the
#     WindowsDesktop SDK rather than with the C# targets, which a non-SDK-style
#     project does not import on its own.
#
# There used to be a third: StarMon.resx held icons, bitmaps and a font, and
# the SDK's GenerateResource task cannot serialize any of those, so this script
# pre-generated the resource bundle under Windows PowerShell first. The resx
# went with the Windows Forms interface, and that whole mechanism with it.

param(
    [switch] $Test,
    [string[]] $Render,
    [string] $Configuration = "Release",
    [string] $Version = "1.1.0.0",
    [string] $VersionWord = "Release"
)

# The version the build stamps into the binary.
#
# The csproj only rewrites the assembly attributes when both AssemblyVersion
# and AssemblyVersionWord are passed to it, and this script passed neither -
# so every build made here carried the placeholder from All\Version.cs, and
# the About page, the tray menu and the command-line header all announced
# themselves as "0.0-None". Four dot-separated integers, or the csproj rejects
# it; the word is what follows the dash in the product version.

# -Render builds the unelevated host and draws a piece of the interface to a
# PNG. It exists as a switch rather than a note in a comment because the host
# it needs is only built by -Test: rendering with a stale binary looks exactly
# like rendering with a fresh one, and the picture quietly shows the previous
# build's design.
if ($Render) { $Test = $true }

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# The two paths below used to be hardcoded to one developer's machine, which
# meant this script built nowhere else - including on a CI runner. Both are now
# searched for, in order of preference, and each check insists on a file that
# proves the directory is the real thing rather than an empty husk.
#
# Set STARMON_REFASM or STARMON_MMI to skip the search and point at a directory
# directly.

function Find-Directory {
    param(
        [string]   $What,       # what is being looked for, for the error message
        [string[]] $Candidates, # directories to try, best first
        [string]   $MustContain,# a file that has to be in it
        [string]   $Remedy      # what the user should do if nothing matched
    )

    foreach ($candidate in $Candidates) {

        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }

        # Candidates may be wildcards (a NuGet package with a version in the
        # path, say), so resolve and prefer the highest-sorting match.
        $matched = Get-Item $candidate -ErrorAction SilentlyContinue |
                   Where-Object { $_.PSIsContainer } |
                   Sort-Object FullName

        foreach ($dir in $matched) {
            if (Test-Path (Join-Path $dir.FullName $MustContain)) { return $dir.FullName }
        }

    }

    throw "$What was not found. $Remedy"
}

# .NET Framework 4.8 reference assemblies. The SDK does not ship these; they
# come from the Developer Pack, or from the reference-assemblies NuGet package
# if one has already been restored.
$refasm = Find-Directory `
    -What "The .NET Framework 4.8 reference assemblies" `
    -MustContain "mscorlib.dll" `
    -Candidates @(
        $env:STARMON_REFASM,
        "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
        "$env:ProgramFiles\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
        "$(if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { "$env:USERPROFILE\.nuget\packages" })\microsoft.netframework.referenceassemblies.net48\*\build\.NETFramework\v4.8"
    ) `
    -Remedy ("Install the .NET Framework 4.8 Developer Pack from " +
             "https://dotnet.microsoft.com/download/dotnet-framework/net48, " +
             "or set STARMON_REFASM to a directory holding the v4.8 reference assemblies.")

# Microsoft.Management.Infrastructure - the CIM client the BIOS interface runs
# on. It is not in the reference pack at all, so it is taken from the GAC, or
# failing that from the copy kept beside the built binary.
$mmi = Find-Directory `
    -What "Microsoft.Management.Infrastructure" `
    -MustContain "Microsoft.Management.Infrastructure.dll" `
    -Candidates @(
        $env:STARMON_MMI,
        "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\Microsoft.Management.Infrastructure\v4.0_*",
        "$env:WINDIR\assembly\GAC_MSIL\Microsoft.Management.Infrastructure\*",
        (Join-Path $root "Bin")
    ) `
    -Remedy ("It ships with Windows Management Framework and is normally in the GAC. " +
             "Set STARMON_MMI to a directory containing the assembly.")

# The XAML markup compiler. The task assembly has to be loadable by the
# runtime MSBuild itself is on, and `dotnet msbuild` runs on the SDK's own
# .NET - so it is the modern build beside the tools directory that is wanted,
# not the net472 one sitting next to it.
#
# Which "modern" that is depends on the SDK: an SDK 10 ships net10.0, an SDK 8
# ships net8.0. This used to name net10.0 outright, so the script built only
# where the newest installed SDK happened to be that exact major version.
$wdSdk = Get-ChildItem "$env:ProgramFiles\dotnet\sdk\*\Sdks\Microsoft.NET.Sdk.WindowsDesktop" `
    -ErrorAction SilentlyContinue |
    Sort-Object { [version] ($_.FullName -replace '.*\\sdk\\([^\\]+)\\.*', '$1' -replace '-.*', '') } |
    Select-Object -Last 1

if (-not $wdSdk) {
    throw "The WindowsDesktop SDK was not found under $env:ProgramFiles\dotnet\sdk. " +
          "It carries the XAML markup compiler, without which the interface cannot " +
          "build. Install a .NET SDK that includes the Windows Desktop workload."
}

$winfx = Join-Path $wdSdk.FullName "targets\Microsoft.WinFX.targets"

# Highest netN.0 wins; net472 is the deliberate last resort, for the case
# where a future SDK stops shipping a cross-platform build of the task.
$pbt = Get-ChildItem (Join-Path $wdSdk.FullName "tools") -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName "PresentationBuildTasks.dll") } |
    Sort-Object {
        if ($_.Name -match '^net(\d+)\.(\d+)$') { [version] "$($Matches[1]).$($Matches[2])" }
        else { [version] "0.0" }
    } |
    Select-Object -Last 1 |
    ForEach-Object { Join-Path $_.FullName "PresentationBuildTasks.dll" }

if (-not $pbt) {
    throw "PresentationBuildTasks.dll was not found under $(Join-Path $wdSdk.FullName 'tools'). " +
          "The XAML markup compiler is part of the Windows Desktop workload."
}

if (-not (Test-Path $winfx)) { throw "Missing part of the XAML toolchain: $winfx" }

# Duplicate locale keys.
#
# Keep this file pure ASCII. Windows PowerShell reads a UTF-8 script with no
# byte-order mark as ANSI, and on a Turkish system an em dash decodes to three
# characters the last of which is a right curly quote - which PowerShell
# accepts as a string delimiter. One em dash in a message here closed its own
# string early and made the rest of the script unparseable.
#
# The two locale files are built with collection-initialiser indexer syntax,
# where a repeated key silently overwrites rather than failing. Three had crept
# in, and each one renamed something the author never looked at again: the log
# tab read "LOG" in capitals because a settings-card heading two hundred lines
# below reused its key, and the dashboard's fan card called itself "FANS &
# BOARD" for the same reason.
#
# TestLocale cannot catch this - by the time it has a dictionary the duplicates
# have already collapsed - so it is caught here, against the source, before
# anything is compiled.
$duplicates = @()

foreach ($file in @("Library\LocaleData.cs", "Library\LocaleDataTr.cs")) {

    $keys = Select-String -Path (Join-Path $root $file) -Pattern '^\s*\["([^"]+)"\]\s*=' -AllMatches |
            ForEach-Object { $_.Matches[0].Groups[1].Value }

    foreach ($group in ($keys | Group-Object | Where-Object Count -gt 1)) {
        $duplicates += "  $file : $($group.Name) defined $($group.Count) times"
    }

}

if ($duplicates.Count -gt 0) {
    Write-Host "Duplicate locale keys - the later definition silently wins:" -ForegroundColor Red
    $duplicates | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}

# Test methods that are never called.
#
# The runner finds suites by reflection, so a whole file cannot be forgotten
# any more. Inside a file it still can: a suite's Run() calls its checks by
# hand, and a method written but left out of that list compiles, is never
# executed, and is invisible in the count at the end - the suite reports a
# clean pass for work it did not do.
#
# Reflection cannot catch this, because a method that is never called looks
# exactly like one that is. It is caught here instead, against the source,
# the same way duplicate locale keys are: a method named Test* whose name
# appears only once in its file is a declaration with no caller.
$orphans = @()

foreach ($file in (Get-ChildItem (Join-Path $root "Test") -Filter "Test*.cs")) {

    $text = Get-Content $file.FullName -Raw

    $declared = [regex]::Matches($text, '(?m)^\s*(?:private|internal)\s+static\s+\w[\w<>,\[\]\s]*\s+(Test\w+)\s*\(') |
                ForEach-Object { $_.Groups[1].Value }

    foreach ($name in ($declared | Select-Object -Unique)) {

        $uses = [regex]::Matches($text, "\b$([regex]::Escape($name))\b").Count

        if ($uses -le 1) {
            $orphans += "  Test\$($file.Name) : $name is declared but never called"
        }

    }

}

if ($orphans.Count -gt 0) {
    Write-Host "Test methods with no caller - these never run:" -ForegroundColor Red
    $orphans | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "The version must be four dot-separated integers; got '$Version'."
}

Write-Host "Building ($Configuration, $Version-$VersionWord)..." -ForegroundColor Cyan

dotnet msbuild (Join-Path $root "StarMon.csproj") `
    /t:Build `
    /p:Configuration=$Configuration `
    /p:AssemblyVersion=$Version `
    /p:AssemblyVersionWord=$VersionWord `
    /p:FrameworkPathOverride=$refasm `
    /p:ReferencePath=$mmi `
    /p:WinFXTargets=$winfx `
    /p:_PresentationBuildTasksAssembly=$pbt `
    /v:minimal /nologo

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Built Bin\StarMon.exe" -ForegroundColor Green

if ($Test) {

    # The shipping executable's manifest asks for administrator rights, which
    # it needs to talk to the hardware but which makes it useless as a test
    # host: it cannot start without a consent prompt, and a run that does get
    # elevated is awkward to stop again.
    #
    # The tests touch no hardware, so the same sources are built a second time
    # with the manifest left off, under a different name and into a separate
    # output directory. Nothing else about the build changes, so what is tested
    # is the same code that ships.

    $testObj = "Obj\Test\"
    $testBin = "Bin\Test"

    Write-Host ""
    Write-Host "Building test host..." -ForegroundColor Cyan

    dotnet msbuild (Join-Path $root "StarMon.csproj") `
        /t:Build `
        /p:Configuration=$Configuration `
        /p:AssemblyVersion=$Version `
        /p:AssemblyVersionWord=$VersionWord `
        /p:FrameworkPathOverride=$refasm `
        /p:ReferencePath=$mmi `
        /p:WinFXTargets=$winfx `
        /p:_PresentationBuildTasksAssembly=$pbt `
        /p:ApplicationManifest= `
        /p:AssemblyName=StarMonTest `
        /p:OutputType=Exe `
        /p:IntermediateOutputPath=$testObj `
        /p:OutputPath=$testBin `
        /v:minimal /nologo

    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host ""

    if ($Render) {
        foreach ($surface in $Render) {
            & (Join-Path $root "$testBin\StarMonTest.exe") `
                -RenderUi $surface (Join-Path $root "Obj\$surface.png") 2 | Out-Host
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
        exit 0
    }

    & (Join-Path $root "$testBin\StarMonTest.exe") -SelfTest | Out-Host
    exit $LASTEXITCODE

}
