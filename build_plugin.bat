@echo off
setlocal
echo ==============================================
echo Building Foobar2000 Plugin (foo_livelyric.dll)
echo ==============================================

:: Dynamically find VsDevCmd.bat using vswhere
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo Visual Studio Installer not found! Please install Visual Studio 2022.
    pause
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -property installationPath`) do (
    set "VS_PATH=%%i"
)

if not exist "%VS_PATH%\Common7\Tools\VsDevCmd.bat" (
    echo VsDevCmd.bat not found in %VS_PATH%!
    pause
    exit /b 1
)

call "%VS_PATH%\Common7\Tools\VsDevCmd.bat"

:: Build the project (x64, Release mode)
msbuild "e:\LiveLyricOverlay\fb2k_sdk_2025\foobar2000\foo_sample\foo_sample.vcxproj" /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143 /p:TargetName=foo_livelyric

echo.
if exist "e:\LiveLyricOverlay\fb2k_sdk_2025\foobar2000\foo_sample\x64\Release\foo_livelyric.dll" (
    echo [SUCCESS] If the build was successful, your plugin will be located at:
    echo e:\LiveLyricOverlay\fb2k_sdk_2025\foobar2000\foo_sample\x64\Release\foo_livelyric.dll
) else (
    echo [ERROR] The DLL was not generated. Please check the compilation errors above!
)
echo.
pause
