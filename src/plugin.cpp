#include "IUnityInterface.h"
#include "ankerl/unordered_dense.h"

#include <iostream>
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

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API OnUpdate() {
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
  if (s_DemoMap) {
    (*s_DemoMap)[key] = std::to_string(value);
  }
}

// Read from the map
int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetVal(int key) {
  if (s_DemoMap && s_DemoMap->contains(key)) {
    try {
      return std::stoi(s_DemoMap->at(key));
    } catch (...) {
      return -1;
    }
  }
  return -999; // Missing key
}

} // extern "C"
