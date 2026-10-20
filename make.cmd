@echo off
rem
rem  StarMon build script
rem  Portions copyright © 2023 Piotr Szczepański (GPL-3.0)
rem
set DOTNET_CLI_TELEMETRY_OPTOUT=1
setlocal
rem The .NET 10 SDK builds this on its own; Visual Studio is not needed.
set msbuild_flags=-p:AssemblyVersion=0.0.0.0 -p:AssemblyVersionWord=Manual -c Release
set op_scope=build clean kill prepare test usage
set op=%~1
set taskkill=%SystemRoot%\System32\taskkill.exe
set result_bin=StarMon.exe
set test_param=-Ec -Ec HPCM RPM1^^(2^^) RPM3^^(2^^) -Bios -Bios Backlight=Off Color=0080FF:00FF00:00FF00:FFFFFF Backlight=On -Prog -Task -Usage
pushd %~dps0
for %%p in (%op_scope%) do if "%op%"=="%%p" echo BEGIN %~n0 (%op%) & goto %op%
echo BEGIN %~n0 & goto usage

:build
call :TaskKill %result_bin%
where dotnet >nul 2>&1 || ( echo The dotnet command was not found. Install the .NET 10 SDK. & goto end )
dotnet msbuild StarMon.csproj -restore /t:Clean /v:minimal /nologo
dotnet publish StarMon.csproj %msbuild_flags% -v:minimal -nologo
goto end

:clean
call :TaskKill %result_bin%
dotnet msbuild StarMon.csproj -restore /t:Clean /v:minimal /nologo
rem Handled within .csproj now:
rem call :RecursivelyRemoveDir Bin
rem call :RecursivelyRemoveDir Obj
goto end

:kill
call :TaskKill %result_bin%
goto end

:prepare
dotnet restore StarMon.csproj
goto end

:test
if exist Bin\%result_bin% ( Bin\%result_bin% %test_param% ) ^
else if exist %result_bin% %result_bin% %test_param%
goto end

:usage
echo Usage: %~n0 ^<build^|clean^|kill^|prepare^|test^|usage^>
goto end

:RecursivelyRemoveDir
if not "%1"=="" for /d /r %%d in (*) do if exist %%~dpsd%1 rd /q /s %%~dpsd%1
exit /b

:TaskKill
%taskkill% /f /im "%1"
exit /b

:end
echo END %~n0
popd
