#pragma once

// Unity Native Plugin API
// Modernized formatting and C++ usage where allowed
// ABI, naming, visibility and behavior remain compatible with Unity

#if defined(_WIN32) || defined(_WIN64) || defined(WINAPI_FAMILY)
#define UNITY_INTERFACE_API __stdcall
#define UNITY_INTERFACE_EXPORT __declspec(dllexport)
#elif defined(__CYGWIN32__)
#define UNITY_INTERFACE_API __stdcall
#define UNITY_INTERFACE_EXPORT __declspec(dllexport)
#elif defined(__MACH__) || defined(__ANDROID__) || defined(__linux__) ||       \
    defined(LUMIN)
#define UNITY_INTERFACE_API
#define UNITY_INTERFACE_EXPORT __attribute__((visibility("default")))
#else
#define UNITY_INTERFACE_API
#define UNITY_INTERFACE_EXPORT
#endif

//------------------------------------------------------------
// UnityInterfaceGUID
//------------------------------------------------------------
struct UnityInterfaceGUID {
#ifdef __cplusplus
  constexpr UnityInterfaceGUID(unsigned long long high,
                               unsigned long long low) noexcept
      : m_GUIDHigh(high), m_GUIDLow(low) {}

  constexpr UnityInterfaceGUID(const UnityInterfaceGUID &other) noexcept =
      default;
  UnityInterfaceGUID &
  operator=(const UnityInterfaceGUID &other) noexcept = default;

  [[nodiscard]] constexpr bool
  Equals(const UnityInterfaceGUID &other) const noexcept {
    return m_GUIDHigh == other.m_GUIDHigh && m_GUIDLow == other.m_GUIDLow;
  }

  [[nodiscard]] constexpr bool
  LessThan(const UnityInterfaceGUID &other) const noexcept {
    if (m_GUIDHigh < other.m_GUIDHigh)
      return true;
    if (m_GUIDHigh > other.m_GUIDHigh)
      return false;
    return m_GUIDLow < other.m_GUIDLow;
  }
#endif

  unsigned long long m_GUIDHigh{};
  unsigned long long m_GUIDLow{};
};

#ifdef __cplusplus
inline constexpr bool operator==(const UnityInterfaceGUID &a,
                                 const UnityInterfaceGUID &b) noexcept {
  return a.Equals(b);
}
inline constexpr bool operator!=(const UnityInterfaceGUID &a,
                                 const UnityInterfaceGUID &b) noexcept {
  return !a.Equals(b);
}
inline constexpr bool operator<(const UnityInterfaceGUID &a,
                                const UnityInterfaceGUID &b) noexcept {
  return a.LessThan(b);
}
inline constexpr bool operator>(const UnityInterfaceGUID &a,
                                const UnityInterfaceGUID &b) noexcept {
  return b.LessThan(a);
}
inline constexpr bool operator<=(const UnityInterfaceGUID &a,
                                 const UnityInterfaceGUID &b) noexcept {
  return !(a > b);
}
inline constexpr bool operator>=(const UnityInterfaceGUID &a,
                                 const UnityInterfaceGUID &b) noexcept {
  return !(a < b);
}
#else
typedef struct UnityInterfaceGUID UnityInterfaceGUID;
#endif

//------------------------------------------------------------
// Interface registration helpers
//------------------------------------------------------------
#ifdef __cplusplus

#define UNITY_DECLARE_INTERFACE(NAME) struct NAME : IUnityInterface

template <typename TYPE>
inline const UnityInterfaceGUID GetUnityInterfaceGUID();

#define UNITY_REGISTER_INTERFACE_GUID(HASHH, HASHL, TYPE)                      \
  template <> inline const UnityInterfaceGUID GetUnityInterfaceGUID<TYPE>() {  \
    return UnityInterfaceGUID(HASHH, HASHL);                                   \
  }

#define UNITY_REGISTER_INTERFACE_GUID_IN_NAMESPACE(HASHH, HASHL, TYPE,         \
                                                   NAMESPACE)                  \
  const UnityInterfaceGUID TYPE##_GUID(HASHH, HASHL);                          \
  template <>                                                                  \
  inline const UnityInterfaceGUID GetUnityInterfaceGUID<NAMESPACE::TYPE>() {   \
    return UnityInterfaceGUID(HASHH, HASHL);                                   \
  }

#define UNITY_GET_INTERFACE_GUID(TYPE) GetUnityInterfaceGUID<TYPE>()

#else

#define UNITY_DECLARE_INTERFACE(NAME)                                          \
  typedef struct NAME NAME;                                                    \
  struct NAME

#define UNITY_REGISTER_INTERFACE_GUID(HASHH, HASHL, TYPE)                      \
  const UnityInterfaceGUID TYPE##_GUID = {HASHH, HASHL};

#define UNITY_REGISTER_INTERFACE_GUID_IN_NAMESPACE(HASHH, HASHL, TYPE,         \
                                                   NAMESPACE)

#define UNITY_GET_INTERFACE_GUID(TYPE) TYPE##_GUID

#endif

#define UNITY_GET_INTERFACE(INTERFACES, TYPE)                                  \
  (TYPE *)INTERFACES->GetInterfaceSplit(                                       \
      UNITY_GET_INTERFACE_GUID(TYPE).m_GUIDHigh,                               \
      UNITY_GET_INTERFACE_GUID(TYPE).m_GUIDLow)

//------------------------------------------------------------
// IUnityInterface
//------------------------------------------------------------
#ifdef __cplusplus
struct IUnityInterface {};
#else
typedef void IUnityInterface;
#endif

//------------------------------------------------------------
// IUnityInterfaces registry
//------------------------------------------------------------
typedef struct IUnityInterfaces {
  IUnityInterface *(UNITY_INTERFACE_API *GetInterface)(UnityInterfaceGUID guid);
  void(UNITY_INTERFACE_API *RegisterInterface)(UnityInterfaceGUID guid,
                                               IUnityInterface *ptr);

  IUnityInterface *(UNITY_INTERFACE_API *GetInterfaceSplit)(
      unsigned long long high, unsigned long long low);
  void(UNITY_INTERFACE_API *RegisterInterfaceSplit)(unsigned long long high,
                                                    unsigned long long low,
                                                    IUnityInterface *ptr);

#ifdef __cplusplus
  template <typename INTERFACE> INTERFACE *Get() {
    return static_cast<INTERFACE *>(
        GetInterface(GetUnityInterfaceGUID<INTERFACE>()));
  }

  template <typename INTERFACE> void Register(IUnityInterface *ptr) {
    RegisterInterface(GetUnityInterfaceGUID<INTERFACE>(), ptr);
  }
#endif

} IUnityInterfaces;

//------------------------------------------------------------
// Plugin load and unload entrypoints
//------------------------------------------------------------
#ifdef __cplusplus
extern "C" {
#endif

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
UnityPluginLoad(IUnityInterfaces *unityInterfaces);
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload();

#ifdef __cplusplus
}
#endif

//------------------------------------------------------------
// Basic Unity types
//------------------------------------------------------------
struct RenderSurfaceBase;
typedef struct RenderSurfaceBase *UnityRenderBuffer;

typedef unsigned int UnityTextureID;
