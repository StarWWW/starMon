# StarMon build script
#
# Builds with the .NET SDK alone, no Visual Studio needed.
#
#   .\build.ps1                    build
#   .\build.ps1 -Test              build, then run the self-test
#   .\build.ps1 -Render window     build, then draw a piece of the interface
#
# The program is published rather than just built: what ships is the single
# framework-dependent Bin\StarMon.exe that the publish step writes, and the
# ordinary build output it is made from stays under Obj. It needs the .NET 10
# SDK to build and the .NET 10 Desktop Runtime (x64) to run.
#
# This used to be most of what the script did: the project targeted .NET
# Framework 4.8, so the reference assemblies, the CIM client and the XAML
# markup compiler all had to be found on disk and handed to a non-SDK-style
# project by hand. The project is SDK-style on .NET 10 now, and the SDK and
# NuGet supply all three; what is left here is the checks no build can make
# and the version stamp.

param(
    [switch] $Test,
    [string[]] $Render,
    [string] $Configuration = "Release",
    [string] $Version = "1.4.0.0",
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

dotnet publish (Join-Path $root "StarMon.csproj") `
    -c $Configuration `
    -p:AssemblyVersion=$Version `
    -p:AssemblyVersionWord=$VersionWord `
    -v:minimal -nologo

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
    # output directory. Nothing else about the build changes - it is published
    # the same way, as one file - so what is tested is the same code that
    # ships, packaged the way it ships.

    $testObj = "Obj\Test\"
    $testOut = "Obj\Test\Out\"
    $testBin = "Bin\Test\"

    Write-Host ""
    Write-Host "Building test host..." -ForegroundColor Cyan

    dotnet publish (Join-Path $root "StarMon.csproj") `
        -c $Configuration `
        -p:AssemblyVersion=$Version `
        -p:AssemblyVersionWord=$VersionWord `
        -p:ApplicationManifest= `
        -p:AssemblyName=StarMonTest `
        -p:OutputType=Exe `
        -p:IntermediateOutputPath=$testObj `
        -p:OutputPath=$testOut `
        -p:PublishDir=$testBin `
        -v:minimal -nologo

    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host ""

    if ($Render) {
        foreach ($surface in $Render) {
            & (Join-Path $root "${testBin}StarMonTest.exe") `
                -RenderUi $surface (Join-Path $root "Obj\$surface.png") 2 | Out-Host
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
        exit 0
    }

    & (Join-Path $root "${testBin}StarMonTest.exe") -SelfTest | Out-Host
    exit $LASTEXITCODE

}
