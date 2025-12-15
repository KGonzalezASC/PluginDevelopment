using FishNet.Object;
using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class GGPOSocketLayer : FishySingleton<GGPOSocketLayer>
{
    public const int RealPortBase = 7000;
    public const int TrapPortBase = 9000;
    private const int BUFFER_SIZE = 1028; //4k (4096 recc)

    [Tooltip("Log traffic? (Allocates strings, disable for Prod)")]
    [SerializeField] private bool _logTraffic = true;

    private int _myLocalGgpoPort; 
    private bool _isInitialized;

    private struct TrapListener
    {
        public Socket socket;
        public int targetClientId;
    }

    // Keep a List for convenient add/remove, but build an array snapshot for hot-path iteration.
    private readonly List<TrapListener> _outboundTraps = new();
    private TrapListener[] _trapArray = Array.Empty<TrapListener>();
    private int _trapCount;

    private readonly Dictionary<int, Socket> _inboundSocketMap = new();

    private byte[] _receiveBuffer;
    private EndPoint _reusableEndPoint; 

    public void Initialize(int localClientId, List<int> allPlayerIds)
    {
        Cleanup();

        _myLocalGgpoPort = RealPortBase + localClientId;
        _receiveBuffer = new byte[BUFFER_SIZE];
        _reusableEndPoint = new IPEndPoint(IPAddress.Any, 0);

        Debug.Log($"[GGPOSocketLayer] Initialized. Optimization Mode: Array snapshot.");

        foreach (var enemyId in allPlayerIds)
        {
            if (enemyId == localClientId) continue;

            int enemyTrapPort = TrapPortBase + enemyId;

            try
            {
                Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Blocking = false; 

                IPEndPoint localEp = new(IPAddress.Loopback, enemyTrapPort);
                socket.Bind(localEp);

                _outboundTraps.Add(new()
                { 
                    socket = socket, 
                    targetClientId = enemyId 
                });

                _inboundSocketMap.Add(enemyId, socket);

                Debug.Log($"[GGPOSocketLayer] Trap Set: 127.0.0.1:{enemyTrapPort} -> Maps to Client {enemyId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GGPOSocketLayer] Failed to bind Trap {enemyTrapPort}: {e.Message}");
            }
        }

        RebuildTrapArraySnapshot();
        _isInitialized = true;
    }

    /// <summary>
    /// Rebuilds _trapArray / _trapCount from the managed _outboundTraps list.
    /// This allocates an array only at rebuild time.
    /// </summary>
    private void RebuildTrapArraySnapshot()
    {
        int count = _outboundTraps.Count;
        if (count == 0)
        {
            _trapArray = Array.Empty<TrapListener>();
            _trapCount = 0;
            return;
        }

        _trapArray = new TrapListener[count];
        for (int i = 0; i < count; i++)
        {
            _trapArray[i] = _outboundTraps[i];
        }
        _trapCount = count;
    }

    private void Update()
    {
        if (!_isInitialized) return;

        // Fast, bounds-checked array loop. No allocations here.
        for (int i = 0; i < _trapCount; i++)
        {
            ref TrapListener trap = ref _trapArray[i];

            // If socket is null (defensive), skip
            if (trap.socket == null) continue;

            while (trap.socket.Available > 0) 
            {
                try
                {
                    // Direct read into reusable buffer and endpoint
                    var bytesRead = trap.socket.ReceiveFrom(_receiveBuffer, ref _reusableEndPoint);

                    if (bytesRead > 0)
                    {
                        // Use ArraySegment slice to avoid allocating new byte[]
                        ArraySegment<byte> payload = new(_receiveBuffer, 0, bytesRead);
                        HandleTrappedPacket(trap.targetClientId, payload);
                    }
                }
                catch (SocketException ex)
                {
                    // 10035 == WSAEWOULDBLOCK (non-blocking socket has no data)
                    if (ex.ErrorCode != 10035) 
                        Debug.LogError($"[GGPOSocketLayer] Socket Error: {ex.ErrorCode}");
                    break; // nothing more to read right now
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
    }

    // ===================================================================================
    // NETWORKING LOGIC
    // ===================================================================================

    private void HandleTrappedPacket(int targetClientId, ArraySegment<byte> data)
    {
        if (_logTraffic)
        {
             Debug.Log($"<color=#00FF00>[OUT]</color> Captured {data.Count} bytes for Client {targetClientId}. Routing...");
        }

        if (IsServerStarted)
        {
            RoutePacketAsServer(targetClientId, LocalConnection.ClientId, data);
        }
        else
        {
            ForwardPacketServerRpc(targetClientId, data);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ForwardPacketServerRpc(int targetClientId, ArraySegment<byte> data, NetworkConnection sender = null)
    {
        if (sender == null) return;
        RoutePacketAsServer(targetClientId, sender.ClientId, data);
    }

    private void RoutePacketAsServer(int targetClientId, int fromClientId, ArraySegment<byte> data)
    {
        if (targetClientId == LocalConnection.ClientId)
        {
            ReceivePacketFromRemote(fromClientId, data);
        }
        else 
        {
            if (NetworkManager.ServerManager.Clients.TryGetValue(targetClientId, out var conn))
            {
                ReceivePacketTargetRpc(conn, fromClientId, data);
            }
        }
    }

    [TargetRpc]
    private void ReceivePacketTargetRpc(NetworkConnection target, int fromClientId, ArraySegment<byte> data)
    {
        ReceivePacketFromRemote(fromClientId, data);
    }

    // ===================================================================================
    // INBOUND DELIVERY
    // ===================================================================================

    private void ReceivePacketFromRemote(int fromClientId, ArraySegment<byte> data)
    {
        if (!_isInitialized) return;

        if (_inboundSocketMap.TryGetValue(fromClientId, out var masqueradeSocket))
        {
            try
            {
                if (_logTraffic)
                {
                    Debug.Log($"<color=#00FFFF>[IN]</color> Received {data.Count} bytes from Client {fromClientId}. Spoofing...");
                }

                IPEndPoint dest = new(IPAddress.Loopback, _myLocalGgpoPort);
                masqueradeSocket.SendTo(data.Array!, data.Offset, data.Count, SocketFlags.None, dest);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GGPOSocketLayer] Failed to forward: {e.Message}");
            }
        }
    }

    // ===================================================================================
    // CLEANUP
    // ===================================================================================

    private void OnDestroy() => Cleanup();

    private void Cleanup()
    {
        _isInitialized = false;
        
        // Close outbound trap sockets
        foreach (var t in _outboundTraps) 
        {
            try { t.socket?.Close(); }
            catch
            {
                // ignored
            }
        }

        // Close inbound sockets as well (they reference the same Socket instances in this design)
        foreach (var kv in _inboundSocketMap)
        {
            try { kv.Value?.Close(); }
            catch
            {
                // ignored
            }
        }

        _outboundTraps.Clear();
        _inboundSocketMap.Clear();

        // Reset array snapshot
        _trapArray = Array.Empty<TrapListener>();
        _trapCount = 0;

        _receiveBuffer = null;
        _reusableEndPoint = null;
    }

    //probably don't need...
    private void AddTrapSnapshot(TrapListener t)
    {
        _outboundTraps.Add(t);
        RebuildTrapArraySnapshot();
    }

    private void RemoveTrapSnapshot(Predicate<TrapListener> predicate)
    {
        _outboundTraps.RemoveAll(predicate);
        RebuildTrapArraySnapshot();
    }
}
