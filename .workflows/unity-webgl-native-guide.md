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

#### A. Emscripten Setup (Local)
Avoid system-wide Emscripten installs to prevent version conflicts. Install a specific version compatible with your Unity version (check `Editor/Data/PlaybackEngines/WebGLSupport/BuildTools/Emscripten/emscripten-version.txt`).

1.  Clone `emsdk` into `tools/`.
2.  Install & Activate:
    ```bash
    cd tools/emsdk
    ./emsdk install 3.1.39  # Match Unity version!
    ./emsdk activate 3.1.39
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
    *   `-std=c++20` (or 17): Modern C++ support.
3.  **Archive (`emar`)**:
    *   `rcs`: Create/replace archive index.
    *   Output: `build/quantum-rosette.a` (Static Library).

**Example Script:**
```batch
call "tools\emsdk\emsdk_env.bat"
em++ -O3 -s WASM=1 -c -o build/plugin.o src/plugin.cpp -Iinclude -Ilib -std=c++20
emar rcs build/quantum-rosette.a build/plugin.o
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
