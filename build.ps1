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
    [switch] $Resources,
    [string[]] $Render,
    [string] $Configuration = "Release"
)

# -Render builds the unelevated host and draws a piece of the interface to a
# PNG. It exists as a switch rather than a note in a comment because the host
# it needs is only built by -Test: rendering with a stale binary looks exactly
# like rendering with a fresh one, and the picture quietly shows the previous
# build's design.
if ($Render) { $Test = $true }

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$refasm = "C:\Users\star\Tools\net48-refasm\build\.NETFramework\v4.8"
$mmi    = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.Management.Infrastructure\v4.0_1.0.0.0__31bf3856ad364e35"

# The XAML markup compiler. The task assembly has to match the runtime MSBuild
# itself is on: `dotnet msbuild` runs on .NET 10, so it is the net10.0 copy
# that is loadable, not the net472 one sitting beside it.
$wdSdk = Get-ChildItem "$env:ProgramFiles\dotnet\sdk\*\Sdks\Microsoft.NET.Sdk.WindowsDesktop" `
    -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -Last 1

if (-not $wdSdk) {
    throw "The WindowsDesktop SDK was not found under $env:ProgramFiles\dotnet\sdk. " +
          "It carries the XAML markup compiler, without which the interface cannot build."
}

$winfx = Join-Path $wdSdk.FullName "targets\Microsoft.WinFX.targets"
$pbt   = Join-Path $wdSdk.FullName "tools\net10.0\PresentationBuildTasks.dll"

foreach ($path in @($winfx, $pbt)) {
    if (-not (Test-Path $path)) { throw "Missing part of the XAML toolchain: $path" }
}

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

Write-Host "Building ($Configuration)..." -ForegroundColor Cyan

dotnet msbuild (Join-Path $root "StarMon.csproj") `
    /t:Build `
    /p:Configuration=$Configuration `
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
