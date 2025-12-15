using System;
using System.Collections.Generic;
using System.Linq; //todo ZLinq!!!
using FishNet.Managing;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;
using UnityGGPO;

public class FishyLobby : NetworkBehaviour
{
    // ===== Shared Fields (Exist on both Server and Clients) =====
    [Header("UI & Network")] [SerializeField]
    private FishyLobbyUI lobbyUI;

    [SerializeField] private NetworkManager networkManager;
    [Header("Prefabs")] [SerializeField] private FishyPlayer fishyPlayerPrefab;

    // This is a local variable for each client to hold their own info before connecting.
    // It is purely UI/local. The authoritative LobbyPlayer instance is created server-side.
    private LobbyPlayer localPlayer;

    // =====================================================================================
    //                                  SERVER-SIDE LOGIC
    // This region contains fields and methods that are primarily managed and used by the server.
    // Clients do not directly modify this data.
    // =====================================================================================

    #region HSL/SSL

    // Track spawned players by clientId to prevent duplicates similar to playerRegistryService idea but lot simpler
    private readonly Dictionary<int, FishyPlayer> _spawnedPlayers = new();

    // only server/host can add or remove names from this list
    private readonly SyncList<string> _playerNames = new();

    // Track which clients have confirmed they have prepared their runner with the correct lobbyData
    // this is a pseudo async "scatter gather" approach as Fishnet lobby transition without this
    // would trick the host into always loading local runner
    private readonly HashSet<int> _readyClients = new();


    //[SerializeField] private GGPOSocketLayer ggpoSocketLayer;
    [SerializeField] private GGPOSocketLayer ggpoSocketLayer;


    // effectively server's start method
    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState != LocalConnectionState.Started)
            return;
        networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
    }

    private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            conn.OnLoadedStartScenes += ServerOnClientLoadedStartScenes;
        }
        else if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            conn.OnLoadedStartScenes -= ServerOnClientLoadedStartScenes;

            _readyClients.Remove(conn.ClientId);

            DespawnLobbyPlayer(conn.ClientId);

            if (_spawnedPlayers.Count > 0 && _readyClients.Count > 0)
            {
                Debug.LogWarning($"[Server] Client {conn.ClientId} disconnected during handshake. Aborting start.");
                _readyClients.Clear();
            }
        }
    }

    // ensure loaded bool to prevent fishnet spawn warning!
    private void ServerOnClientLoadedStartScenes(NetworkConnection conn, bool loaded)
    {
        if (!loaded) return;
        Debug.Log(
            $"[Manager] Condition 2/2 MET: Client {conn.ClientId} has loaded the scene. (1/2) would be an auth e.g lobby password");

        conn.OnLoadedStartScenes -= ServerOnClientLoadedStartScenes;

        // If this is the host's connection, spawn their player object now.
        // Host will not send the SendPlayerInfoServerRpc for themselves so we must spawn.
        if (conn.ClientId == 0)
        {
            // [SAFETY FIX] Ensure we don't spawn twice.
            // OnStartServer (Catch Up logic) might have already spawned the host if the scene was already loaded.
            if (_spawnedPlayers.ContainsKey(conn.ClientId)) return;

            // Create authoritative LobbyPlayer server-side for host.
            var hostPlayer = new LobbyPlayer
            {
                playerName = lobbyUI ? lobbyUI.GetPlayerName() : $"Host_{conn.ClientId}",
                clientId = conn.ClientId
            };

            SpawnLobbyPlayer(conn, hostPlayer);
        }
        // For remote clients, we still need to wait for their info RPC.
        // But we now know they are "ready" to receive spawned objects when that RPC arrives.
    }


    // Clients send only the minimal info: the desired display name.
    // Server validates and constructs the authoritative LobbyPlayer instance.
    [ServerRpc(RequireOwnership = false)]
    private void SendPlayerInfoServerRpc(string requestedPlayerName, NetworkConnection sender = null)
    {
        if (sender == null) return;

        // If we already have a spawned player for this connection, ignore repeat messages.
        if (_spawnedPlayers.ContainsKey(sender.ClientId)) return;

        // Validate name server-side. Minimal validation illustrated here.
        string safeName = SanitizePlayerName(requestedPlayerName, sender.ClientId);

        // Build authoritative LobbyPlayer on server.
        LobbyPlayer newPlayer = new LobbyPlayer
        {
            playerName = safeName,
            // Do not trust client IP. Leave empty or populate from server-side transport if you implement one.
            //ipAddress = string.Empty,
            clientId = sender.ClientId
        };

        SpawnLobbyPlayer(sender, newPlayer);
    }

    // updated for better SRP checking id was moved to the RPC call
    private void SpawnLobbyPlayer(NetworkConnection owner, LobbyPlayer data)
    {
        if (!IsServerStarted) return;

        FishyPlayer playerInstance = Instantiate(fishyPlayerPrefab);
        // give auth
        networkManager.ServerManager.Spawn(playerInstance.gameObject, owner);
        // The SyncVars will automatically send this initial state to the owning client and all other observers.
        playerInstance.Initialize(data);

        // Track the spawned object on the server.
        _spawnedPlayers[data.clientId] = playerInstance;

        // SERVER ACTION: Modify the SyncList. This is the source of the change.
        _playerNames.Add(data.playerName);

        Debug.Log($"[Server] Spawned player {data.playerName} with clientId {data.clientId}");
    }


    private void DespawnLobbyPlayer(int clientId)
    {
        if (_spawnedPlayers.TryGetValue(clientId, out FishyPlayer player))
        {
            // SERVER ACTION: Despawn the object. FishNet tells all clients to destroy it.
            networkManager.ServerManager.Despawn(player.gameObject);

            // SERVER ACTION: Modify the SyncList. This change will be sent to all clients.
            _playerNames.Remove(player.playerName.Value);

            _spawnedPlayers.Remove(clientId);
            Debug.Log($"[Server] Despawned player for ClientId {clientId}");
        }
    }


    public override void OnStartServer()
    {
        base.OnStartServer();

        networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;

        Debug.Log("[FishyLobby] OnStartServer - before maybe spawn ggpo");
        
        // [FIX] 'obj' is a component (GGPOSocketLayer). 
        var obj = Instantiate(ggpoSocketLayer);
        Debug.Log($"[FishyLobby] Instantiated ggpo prefab id={obj.GetInstanceID()}");
        
        // [FIX] FishNet Spawn() requires a GameObject.
        // Also using the instance field 'networkManager' for consistency.
        networkManager.ServerManager.Spawn(obj.gameObject);
        Debug.Log($"[FishyLobby] Spawned ggpo id={obj.GetInstanceID()}");


        // 2. CATCH UP: Handle clients that are ALREADY connected
        // (This runs when you return to the Lobby scene from the Game scene)
        foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
        {
            // Only process valid, active connections
            if (conn.IsActive)
            {
                // If it's the Host (Client 0), spawn them immediately because
                // the Host won't send the "SendPlayerInfo" RPC to themselves.
                if (conn.ClientId == 0)
                {
                    // Ensure we don't spawn twice
                    if (!_spawnedPlayers.ContainsKey(conn.ClientId))
                    {
                        // Create authoritative host player as above.
                        var hostPlayer = new LobbyPlayer
                        {
                            playerName = lobbyUI ? lobbyUI.GetPlayerName() : $"Host_{conn.ClientId}",
                            clientId = conn.ClientId
                        };

                        SpawnLobbyPlayer(conn, hostPlayer);
                    }
                }

                // Note: We do NOT need to manually spawn Remote Clients here.
                // Why? Because when the Lobby Scene loads on their screen,
                // their 'OnStartClient' will run, and they will send the
                // 'SendPlayerInfoServerRpc' again automatically.
            }
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (networkManager)
        {
            networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }
    }

    #endregion

    // =====================================================================================
    //                                  GAME START HANDSHAKE LOGIC
    // Replaces the old "fire and forget" StartGame with a Scatter-Gather pattern
    // =====================================================================================

    private void Awake()
    {
        localPlayer = new LobbyPlayer
        {
            playerName = lobbyUI ? lobbyUI.GetPlayerName() : "Player",
            clientId = -1 // host will get updated automatically
        };

        // Server and client can both respond to UI events
        // Hook up UI events
        lobbyUI.OnHostRequested += HandleHostRequest;
        lobbyUI.OnJoinRequested += HandleJoinRequest;
        lobbyUI.OnStartGameRequested += HandleGameRequest;
    }

    private void HandleGameRequest()
    {
        if (!IsServerStarted) return;
        StartGameHandshake();
    }

    // SCATTER: Step 1 - Server calculates data and sends it to ALL clients
    // This now uses a TargetRpc so each client receives the GGPO list already marked
    // with LOCAL vs REMOTE according to the view that client should have.
    [Server]
    private void StartGameHandshake()
    {
        // Reset confirmation tracking
        _readyClients.Clear();

        // Build a canonical list of players from the server's authoritative state
        List<FishyPlayer> playersOrdered = _spawnedPlayers.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();

        if (playersOrdered.Count == 0)
        {
            Debug.LogWarning("[Server] No players to start handshake for.");
            return;
        }

        var canonicalIds = new List<int>();
        var canonicalPorts = new List<ushort>();

        // Build static GGPO info that is shared except for the local/remote flag
        foreach (var p in playersOrdered)
        {
            canonicalIds.Add(p.clientId.Value);
            // deterministic port allocation remains server-side; this is an example
            canonicalPorts.Add((ushort)(7000 + p.clientId.Value));
            Debug.Log($"[Server] Packing player: {p.playerName.Value} | ID: {p.clientId.Value}");
        }

        Debug.Log($"[Server] Starting Handshake. Waiting for {_spawnedPlayers.Count} players to confirm.");

        // For each connected player, send a tailored GGPO list that marks the recipient as LOCAL
        foreach (var targetConn in networkManager.ServerManager.Clients.Values)
        {
            if (!targetConn.IsActive) continue;

            // Construct GGPOPlayer array for this target
            var arr = new List<GGPOPlayer>(canonicalIds.Count);
            for (int i = 0; i < canonicalIds.Count; i++)
            {
                var id = canonicalIds[i];
                
                // === LOGIC CHANGE HERE ===
                // If it's me (Local), use the REAL port base.
                // If it's an enemy (Remote), use the TRAP port base.
                ushort assignedPort;
                if (id == targetConn.ClientId)
                {
                    assignedPort = (ushort)(GGPOSocketLayer.RealPortBase + id);
                }
                else
                {
                    assignedPort = (ushort)(GGPOSocketLayer.TrapPortBase + id);
                }

                var ggpoPlayer = new GGPOPlayer
                {
                    ip_address = "127.0.0.1", 
                    port = assignedPort,
                    player_num = i + 1,
                    type = (id == targetConn.ClientId)
                        ? GGPOPlayerType.GGPO_PLAYERTYPE_LOCAL
                        : GGPOPlayerType.GGPO_PLAYERTYPE_REMOTE
                };
                arr.Add(ggpoPlayer);
            }

            // Send to the specific client via TargetRpc. The first parameter of a TargetRpc must be the NetworkConnection.
            PrepareGameSetupTargetRpc(targetConn, arr.ToArray(), canonicalIds.ToArray());
        }
    }

    // GATHER STEP:
    // Server sends a *tailored* GGPO player array to THIS specific client.
    // The client only needs to apply the data and reply "ready".
    // All local/remote determination and ordering is already handled server-side.
    [TargetRpc]
    private void PrepareGameSetupTargetRpc(NetworkConnection target, GGPOPlayer[] ggpoPlayers, int[] playerIds)
    {
        // ---- SAFETY CHECK -------------------------------------------------------
        // Ensures the GGPO player list and ID list are aligned.
        // Prevents out-of-range errors or mismatched player data.
        if (ggpoPlayers.Length != playerIds.Length)
        {
            Debug.LogError("GGPO Player count and ID count mismatch!");
            return;
        }

        // ---- AUTHORITATIVE CLIENT ID --------------------------------------------
        // The only correct way to get this client's ID in FishNet.
        int myClientId = LocalConnection.ClientId;

        // ---- GGPO LAYER INITIALIZATION ------------------------------------------
        // Initialize the socket layer for rollback.
        // Use singleton as because of staging from scene to scene the reference in fishy lobby will get de-referenced!!
        GGPOSocketLayer.Instance.Initialize(myClientId, new List<int>(playerIds));

        // ---- LOGGING ONLY (NO LOGIC HERE ANYMORE) -------------------------------
        // The server has *already* marked each GGPOPlayer entry as LOCAL or REMOTE.
        // The client no longer computes or overrides this — avoids logic duplication.
        for (int i = 0; i < ggpoPlayers.Length; i++)
        {
            bool isLocal = ggpoPlayers[i].type == GGPOPlayerType.GGPO_PLAYERTYPE_LOCAL;
            string role = isLocal ? "LOCAL" : "REMOTE";

            Debug.Log($"[Client] Player {i + 1}: {role} (ID: {playerIds[i]})");
        }

        // ---- SINGLE AUTHORITATIVE WRITE TO LOBBY DATA ---------------------------
        // The old version created an initial local GGPO list first, then overwrote it.
        // Now the client writes the final, server-approved data *once*.
        LobbyData.SetLobbyData(false, ggpoPlayers);

        Debug.Log($"[Client] Lobby Data set. Reporting ready to server. {LobbyData.PrintLobbyData()}");

        // ---- BARRIER: CLIENT IS READY -------------------------------------------
        // Old version included extra sync steps and race conditions.
        // Now the client simply acknowledges server instructions.
        ClientReadyServerRpc();
    }


    // BARRIER: Step 3 - Server tracks "Ready" signals. When everyone is ready, execute scene load.
    [ServerRpc(RequireOwnership = false)]
    private void ClientReadyServerRpc(NetworkConnection sender = null)
    {
        // Validate client exists
        if (sender != null && !_spawnedPlayers.ContainsKey(sender.ClientId)) return;
        if (_spawnedPlayers.Count == 0) return;

        _readyClients.Add(sender!.ClientId);

        // Check if everyone is ready (The Barrier)
        if (_readyClients.Count >= _spawnedPlayers.Count)
        {
            Debug.Log("[Server] All clients ready. Loading Game Scene.");
            ExecuteGameStart();
        }
    }

    [Server]
    private void ExecuteGameStart()
    {
        // Instead of letting clients load scene (e.g through unity scene management command it through the server)
        SceneLoadData sld = new SceneLoadData("Game")
        {
            ReplaceScenes = ReplaceOption.All
        };
        // sld.MovedNetworkObjects = new NetworkObject[] { ggpoSocketLayer };
        networkManager.SceneManager.LoadGlobalScenes(sld);
    }

    private void OnDestroy()
    {
        if (lobbyUI)
        {
            lobbyUI.OnHostRequested -= HandleHostRequest;
            lobbyUI.OnJoinRequested -= HandleJoinRequest;
            lobbyUI.OnStartGameRequested -= HandleGameRequest;
        }
    }

    // =====================================================================================
    //                                  CLIENT-SIDE LOGIC
    // This region contains code that runs on clients to react to network changes
    // or to send requests to the server.
    // =====================================================================================


    // UI callback
    private void HandleHostRequest()
    {
        networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
        networkManager.ServerManager.StartConnection();
        networkManager.ClientManager.StartConnection();
        lobbyUI.SetIpAddress(MiscUtils.GetLocalIPAddress());
        LanDiscoveryService.StartBroadcasting(networkManager.TransportManager.Transport.GetPort());
        lobbyUI.ToggleStartButton();
    }

    // UI callback
    private void HandleJoinRequest(string hostAddress) => JoinLobby(hostAddress);

    private async void JoinLobby(string address)
    {
        try
        {
            // If user typed something, just connect directly
            if (!string.IsNullOrWhiteSpace(address))
            {
                networkManager.ClientManager.StartConnection(address);
                return;
            }
        
            Debug.Log("[Client] No address provided. Starting LAN discovery...");

            // Wait for LAN host broadcast
            var result = await LanDiscoveryService.ListenOnceAsync();

            if (!result.success)
            {
                Debug.LogError("[Client] No LAN host found.");
                return;
            }

            string hostIp = result.hostIp;

            Debug.Log($"[Client] Connecting to discovered LAN host at {hostIp}");
            networkManager.ClientManager.StartConnection(hostIp);
        }
        catch (Exception e)
        {
            throw; // TODO handle exception
        }
    }

    // Let's refine the self-introduction logic.
    public override void OnStartClient()
    {
        base.OnStartClient();
        _playerNames.OnChange += OnPlayerNamesChanged;
        UpdateLobbyUI();

        // If we are a normal client (not the host), this is the perfect time
        // to tell the server our information. Only send the minimal data the server needs.
        if (!IsServerStarted)
        {
            SendPlayerInfoServerRpc(localPlayer.playerName);
        }
    }

    /// [Client-Side Event Handler]
    /// This method is EXECUTED ON THE CLIENT when FishNet delivers a change to the SyncList.
    /// This is the "check the mail" action.
    /// The `asServer` parameter is a handy way to know if this code is running because
    //  the server itself made the change (i.e., on the host machine). We often want to
    //  ignore that case to prevent the host from doing things twice.
    private void OnPlayerNamesChanged(SyncListOperation op, int index, string oldItem, string newItem, bool asServer)
    {
        UpdateLobbyUI();
    }

    private void UpdateLobbyUI()
    {
        lobbyUI.UpdatePlayerList(_playerNames.ToArray());
    }


    // Basic server-side name sanitization example
    private string SanitizePlayerName(string requested, int clientId)
    {
        if (string.IsNullOrWhiteSpace(requested)) return $"Player_{clientId}";
        string trimmed = requested.Trim();
        return trimmed.Length > 20 ? trimmed.Substring(0, 20) : trimmed;
    }
}