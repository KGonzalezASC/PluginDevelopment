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
