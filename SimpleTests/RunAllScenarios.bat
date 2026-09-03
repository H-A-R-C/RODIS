@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%..\RODIS.Core.csproj"

echo Running RODIS SimpleTests scenarios 01 to 48
echo.

for /L %%N in (1,1,48) do (
    call :RunScenario %%N
    if errorlevel 1 exit /b 1
)

echo.
echo All 48 scenarios completed successfully.
exit /b 0

:RunScenario
set "SCENARIO_NUMBER=0%~1"
set "SCENARIO_NUMBER=%SCENARIO_NUMBER:~-2%"
set "SCENARIO_DIR=%SCRIPT_DIR%Scenario%SCENARIO_NUMBER%"
set "SCENARIO_FILE=RODIS_RunSTEDILegacyVersion_Scenario%SCENARIO_NUMBER%.scn"

if not exist "%SCENARIO_DIR%\%SCENARIO_FILE%" (
    echo ERROR: Scenario file not found:
    echo   %SCENARIO_DIR%\%SCENARIO_FILE%
    exit /b 1
)

echo Running Scenario%SCENARIO_NUMBER%...

pushd "%SCENARIO_DIR%"
if errorlevel 1 (
    echo ERROR: Could not enter scenario directory:
    echo   %SCENARIO_DIR%
    exit /b 1
)

dotnet run --project "%PROJECT%" -c Release --no-build -- -RunSTEDILegacyVersion "%SCENARIO_FILE%"
set "RUN_RESULT=%ERRORLEVEL%"

popd

if not "%RUN_RESULT%"=="0" (
    echo ERROR: Scenario%SCENARIO_NUMBER% failed with exit code %RUN_RESULT%.
    exit /b %RUN_RESULT%
)

echo Scenario%SCENARIO_NUMBER% completed.
echo.
exit /b 0