@echo off
setlocal DisableDelayedExpansion

REM Check if the required parameters are passed (NAME and SOURCE)
if "%~2" == "" (
    echo ERROR: Missing required parameters
    exit /b 1
)

REM Extract parameters without quotes
set "NAME=%~1"
set "SOURCE=%~2"

REM Remove trailing backslash or slash if applicable
if "%SOURCE:~-1%"=="\" set "SOURCE=%SOURCE:~0,-1%"
if "%SOURCE:~-1%"=="/" set "SOURCE=%SOURCE:~0,-1%"

REM Resolve source file:
set "SRCFILE="
if exist "%SOURCE%\%NAME%" (
    set "SRCFILE=%SOURCE%\%NAME%"
) else if exist "%SOURCE%" (
    set "SRCFILE=%SOURCE%"
) else (
    echo ERROR: Source not found: "%SOURCE%" or "%SOURCE%\%NAME%"
    exit /b 1
)

REM Verify Pulsar deployment and Local plugin folder
set "LOCAL_DIR=%AppData%\Pulsar\Legacy\Local"
if not exist "%LOCAL_DIR%" (
    echo Missing Local plugin folder: "%LOCAL_DIR%"
    echo Pulsar not installed? Skipping copy.
    exit /b 0
)

REM Deploy into dedicated subfolder for Pulsar folder-based plugin structure
set "PLUGIN_DIR=%LOCAL_DIR%\VolumetricClouds"
if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"

REM Copy plugin DLL
echo Copying "%SRCFILE%" to "%PLUGIN_DIR%\%NAME%"
copy /y "%SRCFILE%" "%PLUGIN_DIR%\%NAME%" >nul
if errorlevel 1 (
    echo ERROR: Could not copy "%NAME%", make sure the game is not running and try again.
    exit /b 1
)

REM Also copy as plugin.dll in case Pulsar expects standard naming
copy /y "%SRCFILE%" "%PLUGIN_DIR%\plugin.dll" >nul

REM Pulsar's binary-plugin layout requires a descriptor named plugin.xml.
set "PLUGIN_XML_SRC=%~dp0..\ClientPluginTemplate.xml"
if exist "%PLUGIN_XML_SRC%" (
    echo Copying plugin.xml to "%PLUGIN_DIR%\plugin.xml"
    copy /y "%PLUGIN_XML_SRC%" "%PLUGIN_DIR%\plugin.xml" >nul
)

REM The renderer compiles this source at runtime, so deploy it beside the DLL.
if exist "%SOURCE%\Shaders\VolumetricClouds.hlsl" (
    if not exist "%PLUGIN_DIR%\Shaders" mkdir "%PLUGIN_DIR%\Shaders"
    copy /y "%SOURCE%\Shaders\VolumetricClouds.hlsl" "%PLUGIN_DIR%\Shaders\VolumetricClouds.hlsl" >nul
)

REM Also deploy directly to %LOCAL_DIR% root for compatibility
copy /y "%SRCFILE%" "%LOCAL_DIR%\%NAME%" >nul

exit /b 0
