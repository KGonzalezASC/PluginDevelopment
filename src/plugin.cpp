#include "IUnityInterface.h"
#include "ankerl/unordered_dense.h"

// #include <iostream>
#include <memory>
#include <string>

// Sample internal state using the requested library to verify everything links
// correctly
namespace {
IUnityInterfaces *s_UnityInterfaces = nullptr;
std::unique_ptr<ankerl::unordered_dense::map<int, std::string>> s_DemoMap;
} // namespace

extern "C" {

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
UnityPluginLoad(IUnityInterfaces *unityInterfaces) {
  s_UnityInterfaces = unityInterfaces;

  // Initialize our C++20 resource
  s_DemoMap =
      std::make_unique<ankerl::unordered_dense::map<int, std::string>>();
  s_DemoMap->emplace(1, "PluginLoaded");

  // In a real plugin, you would query for specific Unity interfaces here
  // e.g. IUnityGraphics* graphics = unityInterfaces->Get<IUnityGraphics>();
}

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
  if (s_DemoMap) {
    s_DemoMap->clear();
    s_DemoMap.reset();
  }

  s_UnityInterfaces = nullptr;
}

// --------------------------------------------------------------------------
// Lazy Initialization Helper
// --------------------------------------------------------------------------
// precise initialization timing can be tricky with Static Linking (WebGL).
// Unlike dynamic DLLs/SOs, "UnityPluginLoad" is not always automatically
// triggered by the engine's plugin loader in the same predictable way
// when code is statically linked into the main binary.
//
// To ensure the map exists before we try to Read/Write (which would cause
// a crash or silent failure), we lazily initialize it on the first API call.
void EnsureMap() {
  if (!s_DemoMap) {
    s_DemoMap =
        std::make_unique<ankerl::unordered_dense::map<int, std::string>>();
    s_DemoMap->emplace(1, "LazyLoaded");
  }
}

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API OnUpdate() {
  EnsureMap();
  if (s_DemoMap) {
    // Background task: Increment key 0 (Frame Count)
    int frames = 0;
    try {
      if (s_DemoMap->contains(0))
        frames = std::stoi((*s_DemoMap)[0]);
    } catch (...) {
    }
    (*s_DemoMap)[0] = std::to_string(++frames);
  }
}

// Write to the map
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SetVal(int key, int value) {
  EnsureMap();
  if (s_DemoMap) {
    (*s_DemoMap)[key] = std::to_string(value);
  }
}

// Read from the map
// Read from the map
int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetVal(int key) {
  EnsureMap();
  if (s_DemoMap && s_DemoMap->contains(key)) {
    try {
      return std::stoi(s_DemoMap->at(key));
    } catch (...) {
      return -1;
    }
  }
  return -999; // Missing key
}

// --------------------------------------------------------------------------
// Advanced Marshalling Demo
// --------------------------------------------------------------------------

// Function Pointer Type for C# Callback
// Equivalent to C# delegate* unmanaged<int, void>
typedef void (*StateUpdateCallback)(int val);

namespace {
StateUpdateCallback s_Callback = nullptr;
}

// Register a C# function pointer to be called from C++
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
RegisterCallback(StateUpdateCallback cb) {
  s_Callback = cb;
}

// Process a batch of commands from a raw byte buffer (Zero-GC)
// Buffer Format: Sequential commands.
// Command 1: [1 (int)][Key (int)][Value (int)] -> SetVal
// Command 2: [2 (int)][Value (int)]             -> Trigger Callback
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
ExecuteBatch(void *buffer, int length) {
  EnsureMap();

  if (!buffer || length < 4)
    return;

  unsigned char *ptr = (unsigned char *)buffer;
  unsigned char *end = ptr + length;

  while (ptr < end) {
    // Safety check: ensure we can read the OpCode
    if (ptr + sizeof(int) > end)
      break;

    int opCode = *(int *)ptr;
    ptr += sizeof(int);

    if (opCode == 1) // Set Value: [Key][Value]
    {
      if (ptr + 2 * sizeof(int) > end)
        break;
      int key = *(int *)ptr;
      ptr += sizeof(int);
      int val = *(int *)ptr;
      ptr += sizeof(int);

      (*s_DemoMap)[key] = std::to_string(val);
    } else if (opCode == 2) // Notify: [Value]
    {
      if (ptr + sizeof(int) > end)
        break;
      int val = *(int *)ptr;
      ptr += sizeof(int);

      // Call the C# function pointer directly
      // This bypasses Delegate marshalling overhead
      if (s_Callback)
        s_Callback(val);
    } else {
      // Unknown opcode, abort to prevent bad reads
      break;
    }
  }
}

} // extern "C"
