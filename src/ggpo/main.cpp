/* -----------------------------------------------------------------------
 * GGPO.net (http://ggpo.net)  -  Copyright 2009 GroundStorm Studios, LLC.
 *
 * Use of this software is governed by the MIT license that can be found
 * in the LICENSE file.
 */

#include "backends/p2p.h"
#include "backends/spectator.h"
#include "backends/synctest.h"
#include "ggponet.h"
#include "types.h"

extern "C" {
GGPO_API int __cdecl UggTimeGetTime() {
  return (int)Platform::GetCurrentTimeMS();
}

GGPO_API int __cdecl UggSleep(int ms) {
  Sleep(ms);
  return 0;
}

GGPO_API const char *__cdecl UggPluginVersion() { return "1.0.0"; }

GGPO_API int __cdecl UggPluginBuildNumber() { return 1; }

typedef void(__cdecl *LogDelegate)(const char *text);
static LogDelegate _log_callback = nullptr;

GGPO_API void __cdecl UggSetLogDelegate(LogDelegate cb) { _log_callback = cb; }

GGPO_API GGPOErrorCode __cdecl UggStartSession(GGPOSession **session,
                                               GGPOSessionCallbacks *cb,
                                               const char *game,
                                               int num_players, int input_size,
                                               unsigned short localport) {
  return ggpo_start_session(session, cb, game, num_players, input_size,
                            localport);
}

GGPO_API GGPOErrorCode __cdecl
UggStartSpectating(GGPOSession **session, GGPOSessionCallbacks *cb,
                   const char *game, int num_players, int input_size,
                   unsigned short local_port, char *host_ip,
                   unsigned short host_port) {
  return ggpo_start_spectating(session, cb, game, num_players, input_size,
                               local_port, host_ip, host_port);
}

GGPO_API GGPOErrorCode __cdecl UggSetDisconnectNotifyStart(GGPOSession *ggpo,
                                                           int timeout) {
  return ggpo_set_disconnect_notify_start(ggpo, timeout);
}

GGPO_API GGPOErrorCode __cdecl UggSetDisconnectTimeout(GGPOSession *ggpo,
                                                       int timeout) {
  return ggpo_set_disconnect_timeout(ggpo, timeout);
}

GGPO_API GGPOErrorCode __cdecl UggSynchronizeInput(GGPOSession *ggpo,
                                                   void *values, int size,
                                                   int *disconnect_flags) {
  return ggpo_synchronize_input(ggpo, values, size, disconnect_flags);
}

GGPO_API GGPOErrorCode __cdecl UggAddLocalInput(GGPOSession *ggpo,
                                                GGPOPlayerHandle player,
                                                void *values, int size) {
  return ggpo_add_local_input(ggpo, player, values, size);
}

GGPO_API GGPOErrorCode __cdecl UggCloseSession(GGPOSession *ggpo) {
  return ggpo_close_session(ggpo);
}

GGPO_API GGPOErrorCode __cdecl UggIdle(GGPOSession *ggpo, int timeout) {
  return ggpo_idle(ggpo, timeout);
}

GGPO_API GGPOErrorCode __cdecl
UggAddPlayer(GGPOSession *ggpo, GGPOPlayer *player, GGPOPlayerHandle *handle) {
  return ggpo_add_player(ggpo, player, handle);
}

GGPO_API GGPOErrorCode __cdecl UggDisconnectPlayer(GGPOSession *ggpo,
                                                   GGPOPlayerHandle player) {
  return ggpo_disconnect_player(ggpo, player);
}

GGPO_API GGPOErrorCode __cdecl
UggSetFrameDelay(GGPOSession *ggpo, GGPOPlayerHandle player, int frame_delay) {
  return ggpo_set_frame_delay(ggpo, player, frame_delay);
}

GGPO_API GGPOErrorCode __cdecl UggAdvanceFrame(GGPOSession *ggpo) {
  return ggpo_advance_frame(ggpo);
}

GGPO_API GGPOErrorCode __cdecl UggGetNetworkStats(GGPOSession *ggpo,
                                                  GGPOPlayerHandle player,
                                                  GGPONetworkStats *stats) {
  return ggpo_get_network_stats(ggpo, player, stats);
}

GGPO_API void __cdecl UggLog(GGPOSession *ggpo, const char *fmt, ...) {
  va_list args;
  va_start(args, fmt);
  ggpo_logv(ggpo, fmt, args);
  va_end(args);
}

GGPO_API GGPOErrorCode __cdecl UggTestStartSession(GGPOSession **ggpo,
                                                   GGPOSessionCallbacks *cb,
                                                   char *game, int num_players,
                                                   int input_size, int frames) {
  return ggpo_start_synctest(ggpo, cb, game, num_players, input_size, frames);
}
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved) {
  switch (fdwReason) {
  case DLL_PROCESS_ATTACH:
    srand(Platform::GetCurrentTimeMS() + Platform::GetProcessID());
    ggpo_initialize_winsock();
    break;
  case DLL_PROCESS_DETACH:
    ggpo_deinitialize_winsock();
    break;
  }
  return TRUE;
}

void ggpo_log(GGPOSession *ggpo, const char *fmt, ...) {
  va_list args;
  va_start(args, fmt);
  ggpo_logv(ggpo, fmt, args);
  va_end(args);
}

void ggpo_logv(GGPOSession *ggpo, const char *fmt, va_list args) {
  if (ggpo) {
    ggpo->Logv(fmt, args);
  }
}

GGPOErrorCode ggpo_initialize_winsock() {
#ifdef WIN32
  WORD wVersionRequested = MAKEWORD(2, 2);
  WSADATA wsaData;
  int err = WSAStartup(wVersionRequested, &wsaData);

  if (err != 0) {
    // Can comment out following lines for debugging purposes
    // printf("WSAStartup failed with error: %d\n", err);
    // DWORD lastError = WSAGetLastError();
    // printf("last error code: %d\n", lastError);
    ASSERT(FALSE && "Error initializing winsockets");
  }
#endif

  return GGPO_ERRORCODE_SUCCESS;
}

GGPOErrorCode ggpo_deinitialize_winsock() {
#ifdef WIN32
  // https://docs.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-wsacleanup
  int result = WSACleanup();
  if (result != 0) {
    ASSERT(FALSE && "Error de-initializing winsockets");
  }
#endif

  return GGPO_ERRORCODE_SUCCESS;
}

GGPOErrorCode ggpo_start_session(GGPOSession **session,
                                 GGPOSessionCallbacks *cb, const char *game,
                                 int num_players, int input_size,
                                 unsigned short localport) {
  *session = (GGPOSession *)new Peer2PeerBackend(cb, game, localport,
                                                 num_players, input_size);
  return GGPO_OK;
}

GGPOErrorCode ggpo_add_player(GGPOSession *ggpo, GGPOPlayer *player,
                              GGPOPlayerHandle *handle) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->AddPlayer(player, handle);
}

GGPOErrorCode ggpo_start_synctest(GGPOSession **ggpo, GGPOSessionCallbacks *cb,
                                  char *game, int num_players, int input_size,
                                  int frames) {
  *ggpo = (GGPOSession *)new SyncTestBackend(cb, game, frames, num_players);
  return GGPO_OK;
}

GGPOErrorCode ggpo_set_frame_delay(GGPOSession *ggpo, GGPOPlayerHandle player,
                                   int frame_delay) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->SetFrameDelay(player, frame_delay);
}

GGPOErrorCode ggpo_idle(GGPOSession *ggpo, int timeout) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->DoPoll(timeout);
}

GGPOErrorCode ggpo_add_local_input(GGPOSession *ggpo, GGPOPlayerHandle player,
                                   void *values, int size) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->AddLocalInput(player, values, size);
}

GGPOErrorCode ggpo_synchronize_input(GGPOSession *ggpo, void *values, int size,
                                     int *disconnect_flags) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->SyncInput(values, size, disconnect_flags);
}

GGPOErrorCode ggpo_disconnect_player(GGPOSession *ggpo,
                                     GGPOPlayerHandle player) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->DisconnectPlayer(player);
}

GGPOErrorCode ggpo_advance_frame(GGPOSession *ggpo) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->IncrementFrame();
}

GGPOErrorCode ggpo_client_chat(GGPOSession *ggpo, char *text) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->Chat(text);
}

GGPOErrorCode ggpo_get_network_stats(GGPOSession *ggpo, GGPOPlayerHandle player,
                                     GGPONetworkStats *stats) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->GetNetworkStats(stats, player);
}

GGPOErrorCode ggpo_close_session(GGPOSession *ggpo) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  delete ggpo;
  return GGPO_OK;
}

GGPOErrorCode ggpo_set_disconnect_timeout(GGPOSession *ggpo, int timeout) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->SetDisconnectTimeout(timeout);
}

GGPOErrorCode ggpo_set_disconnect_notify_start(GGPOSession *ggpo, int timeout) {
  if (!ggpo) {
    return GGPO_ERRORCODE_INVALID_SESSION;
  }
  return ggpo->SetDisconnectNotifyStart(timeout);
}

GGPOErrorCode ggpo_start_spectating(GGPOSession **session,
                                    GGPOSessionCallbacks *cb, const char *game,
                                    int num_players, int input_size,
                                    unsigned short local_port, char *host_ip,
                                    unsigned short host_port) {
  *session = (GGPOSession *)new SpectatorBackend(
      cb, game, local_port, num_players, input_size, host_ip, host_port);
  return GGPO_OK;
}
