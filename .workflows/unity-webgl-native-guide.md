# Unity WebGL Native Plugin (Zero-GC / High-Performance) Guide

This guide documents the workflow, architecture, and design patterns for building high-performance native C++ plugins for Unity, with a specific focus on **WebGL (WebAssembly)** support using Emscripten.

---

## 1. Project Structure & Build Pipeline

### Folder Setup
Maintain a clear separation between source C++, build artifacts, and Unity assets.

```text
/root
  ├── src/                    # C++ Source Code (.cpp, .h)
  ├── include/                # Private headers for plugin logic
  ├── lib/                    # Third-party header-only libraries (e.g., ankerl::unordered_dense)
  ├── unity/                  # Unity Project (Assets, ProjectSettings, etc.)
  ├── tools/
  │   └── emsdk/              # Local Emscripten SDK installation
  ├── build/                  # Intermediate build artifacts (.o, .obj)
  ├── build.bat               # Windows (MSVC) Build Script
  └── build_wasm.bat          # WebGL (Emscripten) Build Script
```

### Build Pipeline

#### Prerequisites
*   **Visual Studio 2022 (or newer)**: Required for the Windows build (`build.bat`). This guide assumes Visual Studio is installed with the "Desktop development with C++" workload.
*   **Emscripten SDK**: Required for the WebGL build (`build_wasm.bat`).

#### A. Emscripten Setup (Local)
**CRITICAL: First-Time Setup / Fresh Clone**
Even if the `tools/emsdk` folder exists, the Emscripten SDK Environment variables and binaries are **NOT** portable. You **MUST** run the following steps on every new machine or fresh clone.

*Failure to do this will result in: `'em++' is not recognized` errors when building.*

1.  **Ensure `emsdk` is present**:
    If `tools/emsdk` is missing or empty, clone it:
    ```bash
    git clone https://github.com/emscripten-core/emsdk.git tools/emsdk
    ```

2.  **Install & Activate (Required on every machine)**:
    This downloads the compiler binaries and generates the `.emscripten` config file.
    ```batch
    cd tools/emsdk
    REM Match Unity version!
    emsdk.bat install 3.1.39
    emsdk.bat activate 3.1.39
    ```

#### B. Build Script (`build_wasm.bat`)
We use a two-step process: **Compile** -> **Archive**.
*Do NOT use `em++ -r` directly as it may produce an object format Unity's linker rejects.*

**Key Steps:**
1.  **Environment**: Call `emsdk_env.bat` to set path.
2.  **Compile (`em++`)**:
    *   `-O3`: Release optimization.
    *   `-s WASM=1`: Output WebAssembly.
    *   `-c`: Compile to Object file (`.o`).
    *   `-std=c++20`: Modern C++ support.
3.  **Archive (`emar`)**:
    *   `rcs`: Create/replace archive index.
    *   Output: `build/quantum-rosette.a` (Static Library).

**Script Content:**
```batch
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
```

#### C. Windows Build Script (`build.bat`)
For local testing in the Unity Editor (Windows), we build a standard DLL using MSVC (`cl.exe`).

**Script Content:**
```batch
@echo off
@REM Adjust this path to match your Visual Studio version (e.g., 2022/Community)
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"

if %ERRORLEVEL% NEQ 0 (
    echo Error: Could not setup x64 environment.
    exit /b 1
)

if not exist build mkdir build

echo Building quantum-rosette.dll (x64)...
cl.exe /std:c++latest /EHsc /LD /Iinclude /Ilib /Fobuild\ /Febuild\quantum-rosette.dll src\*.cpp
if %ERRORLEVEL% NEQ 0 (
    echo Build failed.
    exit /b 1
)

echo Build success.
```

---

## 2. C# Attributes & Optimization

We prioritize **Zero-GC (Garbage Collection)** usage.
All marshalling errors on WebGL typically stem from using complex C# types (Delegates, Classes) in signatures. Use **primitive types only (IntPtr, int, float)**.

| Attribute / Feature | Purpose | WebGL Nuance |
| :--- | :--- | :--- |
| **`[DllImport("__Internal")]`** | Required for WebGL static linking. | Use a preprocessor directive to switch between `__Internal` (WebGL) and `"MyPlugin.dll"` (Editor). |
| **`[SuppressGCTransition]`** | Skips the "GC Safe Point" check when calling C++. | **Critical for speed**. Safe only if C++ function is fast and does not trigger GC callbacks. |
| **`[UnmanagedCallersOnly]`** | Compiles a C# method as a raw C-function (no Delegate overhead). | Required for reverse callbacks (C++ -> C#). |
| **`IntPtr` (in Signatures)** | Generic pointer type. | **Use this instead of `delegate*`** in `DllImport` signatures to avoid `MarshalDirectiveException` on WebGL. |
| **`UnsafeUtility.Malloc`** | Raw unmanaged memory allocation. | **Zero GC pressure**. You MUST manually `Free` this memory. |

### The "IntPtr" Pattern for Callbacks
Unity WebGL's marshaller struggles with C# 9 `delegate*` syntax in imports.
**Solution:**
1.  Define C# import as taking `IntPtr`.
2.  Cast your function pointer: `(IntPtr)(delegate* <int, void>)&MyStaticMethod`.
3.  Pass `IntPtr` to C++.

---

## 3. C++ Design Rules (Emscripten Specifics)

### A. Lazy Initialization
**Problem:** In WebGL (Static Linking), `UnityPluginLoad` is NOT guaranteed to run before your first script calls a function, or at all, depending on stripping/initialization order.
**Solution:** Use a "Lazy Init" pattern for global state.

```cpp
// Inside plugin.cpp
void EnsureState() {
    if (!s_GlobalMap) {
        s_GlobalMap = std::make_unique<MyMap>();
    }
}

// At start of EVERY exported function:
extern "C" void MyExportedFunc() {
    EnsureState();
    // ... code ...
}
```

### B. Header-Only Libraries
**Guideline:** Prefer header-only C++ libraries (e.g., `ankerl::unordered_dense`, `nlohmann::json`).
**Reason:** Adding strict compile/link steps for third-party `.lib` or `.a` files in Emscripten is painful and error-prone (symbol conflicts, standard lib mismatches). Include-only keeps the build script simple (`-Ilib`).

### C. No Exceptions (Mostly)
**Guideline:** Avoid relying on C++ Exceptions crossing the boundary.
**Reason:** Exception trapping in WASM adds significant overhead. Catch exceptions *inside* your C++ interface functions and return error codes to C#.

---

## 4. Unity Configuration

### Player Settings
1.  **Allow 'unsafe' Code**: `Project Settings > Player > Other Settings > Allow 'unsafe' Code`. (REQUIRED for `UnsafeUtility` and pointers).
2.  **Api Compatibility Level**: `.NET Standard 2.1` (Recommended).

### WebGL Settings
1.  **Publishing Settings**:
    *   **Enable Exceptions**: `None` (Best Perf) or `Explicitly Thrown` (Debug). *Full* is too slow.
    *   **Compression**: `Brotli` or `Gzip`.
2.  **Plugin Inspector (`.a` file)**:
    *   Select `build/quantum-rosette.a` in Project view.
    *   Check **WebGL** platform.
    *   Uncheck **Any Platform**.
    *   Ensure "Load on Startup" is checked (usually default).

---

## 5. Lessons Learned (Troubleshooting)

*   **`DlNotFoundException` on WebGL**: You forgot `[DllImport("__Internal")]`.
*   **`MarshalDirectiveException: System.IntPtr`**: You used a `delegate*` type in a P/Invoke signature. Change argument to `IntPtr`.
*   **State Resetting / Missing Data**: You assumed `UnityPluginLoad` ran. It didn't. Add `EnsureState()` checks.
*   **Build Fails (Linker Error)**: You likely used `em++ -r` instead of compile (`-c`) then archive (`emar`). Unity needs a standard static archive `.a`.
