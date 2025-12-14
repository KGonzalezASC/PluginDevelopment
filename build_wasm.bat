@echo off
setlocal

REM Set Emscripten paths explicitly (SDK activation workaround)
set "EMSDK=%~dp0tools\emsdk"
set "EMSDK_NODE=%EMSDK%\node\22.16.0_64bit\bin\node.exe"
set "EMSDK_PYTHON=%EMSDK%\python\3.13.3_64bit\python.exe"
set "EM_CONFIG=%EMSDK%\upstream\emscripten\.emscripten"
set "EMSCRIPTEN=%EMSDK%\upstream\emscripten"
set "PATH=%EMSCRIPTEN%;%EMSDK%\upstream\bin;%EMSDK%\node\22.16.0_64bit\bin;%EMSDK%\python\3.13.3_64bit;%PATH%"

REM Alternatively, try calling emsdk_env.bat (may work if activated properly)
REM call "tools\emsdk\emsdk_env.bat"

if not exist build mkdir build

echo Building blind-fish.a (WebGL Static Library)...
@REM -O3: Release optimization
@REM -c: Compile to Object file (.o)
@REM -I...: Include paths
@REM -std=c++20: Language standard
call "%EMSCRIPTEN%\em++.bat" -O3 -c -o build/plugin.o src/plugin.cpp -Iinclude -Ilib -std=c++20

if %ERRORLEVEL% NEQ 0 (
    echo Compilation failed.
    exit /b 1
)

echo Archiving to blind-fish.a...
call "%EMSCRIPTEN%\emar.bat" rcs build/blind-fish.a build/plugin.o

if %ERRORLEVEL% NEQ 0 (
    echo Build failed.
    exit /b 1
)

echo Build success.
endlocal
