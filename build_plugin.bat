@echo off
echo ==============================================
echo Building Foobar2000 Plugin (foo_livelyric.dll)
echo ==============================================

:: Call the Visual Studio Developer Command Prompt environment
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat"

:: Build the solution (Win32, Release mode)
msbuild "e:\LiveLyricOverlay\fb2k_sdk\foobar2000\foo_sample\foo_sample.sln" /p:Configuration=Release /p:Platform=x86 /p:PlatformToolset=v143

echo.
echo If the build was successful, your plugin will be located at:
echo e:\LiveLyricOverlay\fb2k_sdk\foobar2000\foo_sample\Release\foo_livelyric.dll
echo.
pause
