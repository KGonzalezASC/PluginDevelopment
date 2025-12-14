@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
if %ERRORLEVEL% NEQ 0 (
    echo Error: Could not setup x64 environment.
    exit /b 1
)

if not exist build mkdir build

echo Building blind-fish.dll (x64)...
cl.exe /std:c++latest /EHsc /LD /Iinclude /Ilib /Fobuild\ /Febuild\blind-fish.dll src\*.cpp
if %ERRORLEVEL% NEQ 0 (
    echo Build failed.
    exit /b 1
)

echo Build success.
