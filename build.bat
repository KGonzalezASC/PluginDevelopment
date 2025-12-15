@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
if %ERRORLEVEL% NEQ 0 (
    echo Error: Could not setup x64 environment.
    exit /b 1
)

if not exist build mkdir build

echo Building blind-fish.dll (x64)...
cl.exe /D_WINDOWS /DGGPO_SHARED_LIB /DGGPO_SDK_EXPORT /std:c++latest /EHsc /LD /Iinclude /Ilib /Isrc/ggpo/include /Isrc/ggpo/ /Fobuild\ /Febuild\blind-fish.dll src\*.cpp src\ggpo\*.cpp src\ggpo\backends\*.cpp src\ggpo\network\*.cpp ws2_32.lib winmm.lib user32.lib
if %ERRORLEVEL% NEQ 0 (
    echo Build failed.
    exit /b 1
)

echo Build success.
