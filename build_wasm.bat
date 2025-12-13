@echo off
call "tools\emsdk\emsdk_env.bat"

if not exist build mkdir build

echo Building quantum-rosette.a (WebGL Static Library)...
@REM -O3: Release optimization
@REM -r: Generate a relocatable object (archive/static lib equivalent for Emscripten)
@REM -s WASM=1: Target WebAssembly (standard)
@REM -I...: Include paths
@REM -std=c++20: Language standard
@REM Compile to object file first
call em++ -O3 -s WASM=1 -c -o build/plugin.o src/plugin.cpp -Iinclude -Ilib -std=c++20

if %ERRORLEVEL% NEQ 0 (
    echo Compilation failed.
    exit /b 1
)

echo Archiving to quantum-rosette.a...
call emar rcs build/quantum-rosette.a build/plugin.o

if %ERRORLEVEL% NEQ 0 (
    echo Build failed.
    exit /b 1
)

echo Build success.
