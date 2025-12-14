# Unity WebGL Native Plugin Setup Guide

> **Purpose**: This guide enables an AI agent or developer to set up a complete Unity WebGL native C++ plugin project from scratch on a fresh Windows machine. Follow steps sequentially.

---

## Quick Reference

| Output | File | Platform |
|--------|------|----------|
| Static Library | `build/<plugin-name>.a` | WebGL |
| Dynamic Library | `build/<plugin-name>.dll` | Windows Editor |

---

## Prerequisites

Before starting, verify these are installed:

```powershell
# Check Git
git --version
# Expected: git version 2.x.x

# Check Python  
python --version
# Expected: Python 3.10+ (3.12+ recommended)

# Check Visual Studio (for Windows DLL builds)
# Must have "Desktop development with C++" workload installed
```

---

## Step 1: Create Project Structure

Create these directories and files:

```
<project-root>/
├── src/                          # C++ source files
│   └── plugin.cpp
├── include/                      # Private headers (can be empty)
├── lib/                          # Third-party header-only libs (can be empty)  
├── tools/
│   └── emsdk/                    # Emscripten SDK (created in Step 2)
├── unity/
│   └── Assets/
│       ├── Plugins/              # Built .a and .dll go here
│       └── Scripts/              # C# test scripts
├── build/                        # Build artifacts (auto-created)
├── .gitignore
├── .clangd                       # Clangd linter config
├── .vscode/
│   └── tasks.json                # VS Code build tasks
├── build.bat                     # Windows DLL build
└── build_wasm.bat                # WebGL .a build
```

### 1.1 Create `.gitignore`

```gitignore
# Build artifacts
build/
*.dll
*.exe
*.obj
*.lib
*.exp
*.pdb
*.ilk
*.a
*.o

# Emscripten SDK (CRITICAL - prevents file locking during install)
tools/emsdk/

# Unity
unity/Library/
unity/Logs/
unity/Temp/
unity/obj/
*.csproj
*.sln

# Editors
.vs/
.vscode/*
!.vscode/tasks.json
!.vscode/launch.json
!.vscode/extensions.json
.clangd/
*.user

# System
Thumbs.db
Desktop.ini
.DS_Store
```

### 1.2 Create `.clangd` (Clangd IntelliSense config)

```yaml
CompileFlags:
  Add: 
    - -std=c++20
    - --target=x86_64-pc-windows-msvc
    - -fms-compatibility
    - -I./lib
    - -I./include
```

### 1.3 Create `.vscode/tasks.json` (VS Code build integration)

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "Build Windows (DLL)",
            "type": "shell",
            "command": ".\\build.bat",
            "group": {
                "kind": "build",
                "isDefault": true
            },
            "problemMatcher": ["$msCompile"]
        },
        {
            "label": "Build WebGL (.a)",
            "type": "shell",
            "command": ".\\build_wasm.bat",
            "group": "build",
            "problemMatcher": ["$gcc"]
        }
    ]
}
```

> **Usage**: Press `Ctrl+Shift+B` in VS Code to run the default build task (Windows DLL).

### 1.4 Create `src/plugin.cpp`

```cpp
#include <cstdint>

#ifdef _WIN32
#define PLUGIN_API __declspec(dllexport)
#else
#define PLUGIN_API __attribute__((visibility("default")))
#endif

extern "C" {
PLUGIN_API void InitializePlugin() {}

PLUGIN_API int AddNumbers(int a, int b) { return a + b; }
}
```

---

## Step 2: Install Emscripten SDK

> **CRITICAL**: This step must be run on EVERY new machine or fresh clone. The SDK is NOT portable.

### 2.1 Clone the SDK

```powershell
cd <project-root>
git clone https://github.com/emscripten-core/emsdk.git tools/emsdk
```

### 2.2 Install Emscripten

```powershell
cd tools/emsdk

# Use Python directly (more reliable than emsdk.bat on Windows)
python emsdk.py install 3.1.39

# Wait for download and extraction to complete (may take 5-10 minutes)
```

**Version Guidance:**
| Unity Version | Recommended Emscripten |
|---------------|------------------------|
| Unity 2022.2+ | 3.1.8 or 3.1.39 |
| Unity 6 (2024) | 3.1.38 |
| General/Latest | 3.1.39+ |

### 2.3 Activate (May Fail - See 2.4)

```powershell
python emsdk.py activate 3.1.39
```

### 2.4 Create Manual Config (REQUIRED if activation fails)

If `em++` is not recognized after activation, create the config file manually:

**File**: `tools/emsdk/upstream/emscripten/.emscripten`

```python
import os
emsdk_path = 'C:/path/to/your/project/tools/emsdk'  # <-- UPDATE THIS TO YOUR ABSOLUTE PATH
LLVM_ROOT = emsdk_path + '/upstream/bin'
BINARYEN_ROOT = emsdk_path + '/upstream'
EMSCRIPTEN_ROOT = emsdk_path + '/upstream/emscripten'
NODE_JS = emsdk_path + '/node/22.16.0_64bit/bin/node.exe'
TEMP_DIR = emsdk_path + '/tmp'
```

**PowerShell command to create it** (update the path!):
```powershell
$emsdk_path = "C:/Users/YourName/Projects/your-project/tools/emsdk"
$content = @"
import os
emsdk_path = '$emsdk_path'
LLVM_ROOT = emsdk_path + '/upstream/bin'
BINARYEN_ROOT = emsdk_path + '/upstream'
EMSCRIPTEN_ROOT = emsdk_path + '/upstream/emscripten'
NODE_JS = emsdk_path + '/node/22.16.0_64bit/bin/node.exe'
TEMP_DIR = emsdk_path + '/tmp'
"@
[System.IO.File]::WriteAllText("$emsdk_path/upstream/emscripten/.emscripten", $content.Replace('\', '/'))
```

### 2.5 Verify Installation

```powershell
# Test em++ directly with full path
& "tools\emsdk\upstream\emscripten\em++.bat" --version

# Expected output: emcc (Emscripten gcc/clang-like replacement...) 3.1.39
```

---

## Step 3: Create Build Scripts

### 3.1 Create `build_wasm.bat` (WebGL Static Library)

> **Key Design**: Uses explicit paths to bypass SDK activation issues.

```batch
@echo off
setlocal

REM === Emscripten Paths (adjust node/python versions if different) ===
set "EMSDK=%~dp0tools\emsdk"
set "EMSDK_NODE=%EMSDK%\node\22.16.0_64bit\bin\node.exe"
set "EMSDK_PYTHON=%EMSDK%\python\3.13.3_64bit\python.exe"
set "EM_CONFIG=%EMSDK%\upstream\emscripten\.emscripten"
set "EMSCRIPTEN=%EMSDK%\upstream\emscripten"
set "PATH=%EMSCRIPTEN%;%EMSDK%\upstream\bin;%EMSDK%\node\22.16.0_64bit\bin;%EMSDK%\python\3.13.3_64bit;%PATH%"

if not exist build mkdir build

echo Building <plugin-name>.a (WebGL Static Library)...
call "%EMSCRIPTEN%\em++.bat" -O3 -c -o build/plugin.o src/plugin.cpp -Iinclude -Ilib -std=c++20

if %ERRORLEVEL% NEQ 0 (
    echo Compilation failed.
    exit /b 1
)

echo Archiving...
call "%EMSCRIPTEN%\emar.bat" rcs build/<plugin-name>.a build/plugin.o

if %ERRORLEVEL% NEQ 0 (
    echo Archive failed.
    exit /b 1
)

echo Build success: build/<plugin-name>.a
endlocal
```

**Replace `<plugin-name>` with your actual plugin name (e.g., `blind-fish`).**

### 3.2 Create `build.bat` (Windows DLL)

```batch
@echo off
REM === Adjust path to your Visual Studio installation ===
REM Common paths:
REM   VS 2022: "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
REM   VS 2019: "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars64.bat"

call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"

if %ERRORLEVEL% NEQ 0 (
    echo Error: Could not setup x64 environment. Check Visual Studio path.
    exit /b 1
)

if not exist build mkdir build

echo Building <plugin-name>.dll (x64)...
cl.exe /std:c++latest /EHsc /LD /Iinclude /Ilib /Fobuild\ /Febuild\<plugin-name>.dll src\*.cpp

if %ERRORLEVEL% NEQ 0 (
    echo Build failed.
    exit /b 1
)

echo Build success: build/<plugin-name>.dll
```

**Replace `<plugin-name>` with your actual plugin name.**

---

## Step 4: Build and Test

### 4.1 Build WebGL Library

```powershell
cd <project-root>
cmd /c "build_wasm.bat"
# Expected: Build success: build/<plugin-name>.a
```

### 4.2 Build Windows DLL

```powershell
cmd /c "build.bat"
# Expected: Build success: build/<plugin-name>.dll
```

### 4.3 Copy to Unity

```powershell
Copy-Item build\<plugin-name>.a unity\Assets\Plugins\
Copy-Item build\<plugin-name>.dll unity\Assets\Plugins\
```

---

## Step 5: Create Unity Test Script

**File**: `unity/Assets/Scripts/NativePluginTest.cs`

```csharp
using UnityEngine;
using System.Runtime.InteropServices;

public class NativePluginTest : MonoBehaviour
{
    // For WebGL: "__Internal" (static linking)
    // For Editor: "<plugin-name>" (loads .dll)
#if UNITY_WEBGL && !UNITY_EDITOR
    const string DLL_NAME = "__Internal";
#else
    const string DLL_NAME = "<plugin-name>";
#endif

    [DllImport(DLL_NAME)]
    private static extern void InitializePlugin();

    [DllImport(DLL_NAME)]
    private static extern int AddNumbers(int a, int b);

    void Start()
    {
        Debug.Log("Initializing Plugin...");
        InitializePlugin();
        
        int a = 5;
        int b = 7;
        int result = AddNumbers(a, b);
        
        Debug.Log($"AddNumbers({a}, {b}) = {result}");
        
        if (result == 12)
            Debug.Log("<color=green>Test PASSED: Plugin working correctly.</color>");
        else
            Debug.LogError($"<color=red>Test FAILED: Expected 12, got {result}</color>");
    }
}
```

**Replace `<plugin-name>` with your actual plugin name.**

---

## Step 6: Unity Configuration

### 6.1 Plugin Import Settings

After copying plugins to `Assets/Plugins/`:

**For `.a` file (WebGL):**
1. Select the `.a` file in Unity Project view
2. In Inspector, uncheck **Any Platform**
3. Check only **WebGL**
4. Click **Apply**

**For `.dll` file (Editor):**
1. Select the `.dll` file in Unity Project view
2. In Inspector, uncheck **Any Platform**  
3. Check only **Editor** and **Standalone Windows x64**
4. Click **Apply**

### 6.2 Project Settings

1. **Enable Unsafe Code**: `Edit > Project Settings > Player > Other Settings > Allow 'unsafe' Code` ✓
2. **API Compatibility**: `.NET Standard 2.1` (recommended)

### 6.3 WebGL Build Settings

1. `Edit > Project Settings > Player > WebGL > Publishing Settings`
2. **Enable Exceptions**: `None` (best performance) or `Explicitly Thrown Exceptions Only`
3. **Compression Format**: `Brotli` or `Gzip`

---

## Troubleshooting

| Error | Cause | Fix |
|-------|-------|-----|
| `'em++' is not recognized` | SDK not activated | Create `.emscripten` manually (Step 2.4) |
| `[WinError 32] Cannot access file` | File locked during install | Close VS Code, add `tools/emsdk/` to `.gitignore`, retry |
| `DllNotFoundException` on WebGL | Wrong DllImport name | Use `"__Internal"` for WebGL builds |
| `MarshalDirectiveException` | Using `delegate*` in signatures | Use `IntPtr` instead |
| `NODE_JS not set in config` | Missing `.emscripten` file | Create it manually (Step 2.4) |
| Build fails with linker errors | Used `em++ -r` | Use `em++ -c` then `emar rcs` |

---

## C++ Best Practices (Emscripten/WebGL)

### Use Lazy Initialization
`UnityPluginLoad` may not run before your code is called in WebGL.

```cpp
static std::unique_ptr<MyState> g_state;

void EnsureState() {
    if (!g_state) g_state = std::make_unique<MyState>();
}

extern "C" PLUGIN_API void MyFunction() {
    EnsureState();
    // ... use g_state ...
}
```

### Prefer Header-Only Libraries
Avoid linking external `.lib`/`.a` files. Use header-only libraries like:
- `nlohmann/json`
- `ankerl::unordered_dense`

### Avoid Exceptions Crossing Boundaries
Catch exceptions inside C++ and return error codes to C#.

### Use Primitive Types Only
WebGL marshalling fails with complex types. Use: `int`, `float`, `IntPtr`, raw pointers.

---

## C# Optimization Attributes

| Attribute | Purpose | When to Use |
|-----------|---------|-------------|
| `[SuppressGCTransition]` | Skip GC safe-point check | Fast C++ functions (<1μs) |
| `[UnmanagedCallersOnly]` | C# method callable from C++ | Reverse callbacks |
| `IntPtr` | Generic pointer | Instead of `delegate*` in signatures |

---

## Final Checklist

- [ ] Git and Python installed
- [ ] `tools/emsdk/` cloned and installed
- [ ] `.emscripten` config file exists (if activation failed)
- [ ] `build_wasm.bat` produces `.a` without errors
- [ ] `build.bat` produces `.dll` without errors
- [ ] Plugins copied to `unity/Assets/Plugins/`
- [ ] Plugin import settings configured (WebGL for .a, Editor for .dll)
- [ ] Test script attached to GameObject
- [ ] Console shows "Test PASSED" in Editor
- [ ] WebGL build shows "Test PASSED" in browser console
