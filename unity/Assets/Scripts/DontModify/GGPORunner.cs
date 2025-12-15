using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Managers;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityGGPO;


/// <summary>
/// GGPORunner class handles the initialization and running of GGPO sessions.
/// </summary>
public class GGPORunner : GameRunner
{
    private const int MAX_STATE_BYTES = 2048; 
    private Stack<NativeArray<byte>> _stateBufferPool = new Stack<NativeArray<byte>>();
    
    /// <summary>
    ///  Initializes the GGPO session with the provided players and local player index.
    /// </summary>
    public override void Init(GGPOPlayer[] players = null, int localPlayer = 0)
    {
        if (players == null) //this is fallback logic
        {
            Debug.Log("Fallback is used look into why");
            players = new GGPOPlayer[2];

            players[0].player_num = 1;
            players[0].port = 7000;
            players[0].ip_address = MiscUtils.GetLocalIPAddress();
            players[0].type = localPlayer == 0
                ? GGPOPlayerType.GGPO_PLAYERTYPE_LOCAL
                : GGPOPlayerType.GGPO_PLAYERTYPE_REMOTE;

            players[1].player_num = 2;
            players[1].port = 7001;
            players[1].ip_address = MiscUtils.GetLocalIPAddress();
            players[1].type = localPlayer == 1
                ? GGPOPlayerType.GGPO_PLAYERTYPE_LOCAL
                : GGPOPlayerType.GGPO_PLAYERTYPE_REMOTE;
        }

        SetPlayerControllers();
        InitInputHistories(players.Length);
        StartSession("FatalCounter", players);
    }

    public void StartSession(string name, GGPOPlayer[] ggpoPlayers)
    {
        unsafe
        {
            players = ggpoPlayers;
            playerCons[0].position = new Vector3(-10, 0, 0);
            playerCons[1].position = new Vector3(10, 0, 0);
            playerCons[0].opponent = playerCons[1];
            playerCons[1].opponent = playerCons[0];

            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].type == GGPOPlayerType.GGPO_PLAYERTYPE_LOCAL)
                {
                    if (i < playerCons.Length && playerCons[i] != null)
                    {
                        playerCons[i].InitializeLocalPlayer(InputSystem.actions, "Default");
                        Debug.Log($"Input initialized for local player at index {i}.");
                    }
                }
                //playerCons[i].LoadCharacterData(); //this technically should go in OnBeginGame but could work here since the
                //thing already exists
            }

            int localPort = 0;
        
            playerHandles = new int[players.Length];
            next = Utils.TimeGetTime();
        
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].type == GGPOPlayerType.GGPO_PLAYERTYPE_LOCAL)
                {
                    // Capture the port assigned by the Lobby (e.g., 7000 + MyClientID)
                    localPort = players[i].port;
                
                    players[i].port = 0;
                    players[i].ip_address = "";
                }
            }
        
            Debug.Log($"[GGPORunner] Starting GGPO Session on Local Port: {localPort}");
        
            //because this creates objects on both clients its better ensure both clients are insync before running
            int result = GGPO.Session.StartSession(OnBeginGame, OnAdvanceFrame,
                OnLoadGameState, OnLogGameState,
                OnSaveGameState, OnFreeBuffer,
                OnConnectedToPeer, OnSynchronizingWithPeer,
                OnSynchronizedWithPeer, OnRunning,
                OnConnectionInterrupted, OnConnectionResumed,
                OnDisconnectedFromPeer, OnTimeSync,
                name, players.Length, localPort);


            Debug.Log("Result of Starting Session: " + GGPO.GetErrorCodeMessage(result));

            GGPO.Session.SetDisconnectTimeout(3000);
            GGPO.Session.SetDisconnectNotifyStart(1000);

            for (int i = 0; i < players.Length; i++)
            {
                result = GGPO.Session.AddPlayer(players[i], out playerHandles[i]);
                Debug.Log("Result of player " + i + ": " + GGPO.GetErrorCodeMessage(result));

                if (players[i].type == GGPOPlayerType.GGPO_PLAYERTYPE_LOCAL)
                {
                
                    GGPO.Session.SetFrameDelay(playerHandles[i], 3);
                }
            }

            isRunning = true;
        }
    }

    #region prewarm_phase

    private bool _isPrewarming;

    //On client callback that delays the start of the game until the prewarming is done
    private bool OnBeginGame(string text)
    {
        // Don't do the work here directly.
        // Instead, start the coroutine and set the flag.
        if (!_isPrewarming)
        {
            _isPrewarming = true;
            StartCoroutine(PrewarmAndStartGame());
        }
        return true;
    }

    private IEnumerator PrewarmAndStartGame()
    {
        GameSessionManager.Instance.RegisterRunner(this);
        
        foreach (var p in playerCons)
        {
            p.LoadCharacterData();
        }
        // This coroutine now controls the loading flow.
        // It runs the heavy work...
        yield return StartCoroutine(ProjectileManager.Instance.PrewarmPools(3));
        // And when it's done, it tells our runner that it's safe to start the game.
        _isPrewarming = false;
        Debug.Log("Pre-warming finished, game is now running.");
    }
    
    #endregion





    #region SyncedPath

      protected override void Update() => UpdateFixedFastForward();
    
    private void UpdateFixedFastForward()
    {
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
        {
            PrintNetworkStats();
        }
        
        int now = Utils.TimeGetTime();
        int extraMs = Mathf.Max(0, (int)(next - now - 1));
        GGPO.Session.Idle(extraMs);

        // Limit catch-up to 8 frames per tick to prevent freezing
        int loops = 0;
        while (now >= next && loops < 8)
        {
            RunFrame();
            next += FrameToMs(1);
            loops++;
        }
    
        // Move Rendering OUTSIDE the simulation loop
        RenderGameState(); 
    }

    protected override void RunFrame()
    {
        if (!isRunning)
            return;

        int result = GGPO.OK;
        for (int i = 0; i < playerCons.Length; i++)
        {
            if (players[i].type == GGPOPlayerType.GGPO_PLAYERTYPE_LOCAL)
            {
                long input = playerCons[i].GetInputs();
                //currentPayloadBytes += sizeof(long); // or however big your input packet actually is
                result = GGPO.Session.AddLocalInput(playerHandles[i], input);
            }
        }

        if (GGPO.SUCCEEDED(result))
        {
            try
            {
                //var unityTransport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
                //FCUtils.PrintTransportMTU(unityTransport, currentPayloadBytes);
                long[] inputs = GGPO.Session.SynchronizeInput(playerCons.Length, out _);
                UpdateGameState(inputs);

                for (int i = 0; i < inputs.Length; i++)
                {
                    //RecordInputHistory(i, inputs[i],playerCons[i].logicFrame);
                    RecordInputHistory(i, inputs[i]);
                }
                //sanity check callback is called
                GGPO.Session.AdvanceFrame();
            }
            catch (Exception e)
            {
                Debug.Log("Error: " + e);
            }
        }

        message = GGPO.GetErrorCodeMessage(result);
    }
    
    //proactively happens in hotpath
    private unsafe bool OnSaveGameState(void** buffer, int* len, int* checksum, int frame)
    {
        var writer = new FastBufferWriter(2048, Allocator.Temp);
        try
        {
            for (int i = 0; i < playerCons.Length; i++)
            {
                playerCons[i].Serialize(ref writer);
            }
            ProjectileManager.Instance.SerializeAll(ref writer);

            // Allocate raw memory. 
            // GGPO C++ will hold this pointer. 
            // GGPO C++ will pass this pointer back to OnFreeBuffer later.
            void* ptr = UnsafeUtility.Malloc(writer.Length, 4, Allocator.Persistent);
        
            // Copy data from Writer to Ptr
            UnsafeUtility.MemCpy(ptr, writer.GetUnsafePtr(), writer.Length);

            // Assign outputs for GGPO
            *buffer = ptr;
            *len = writer.Length;
            *checksum = (int)Utils.CalcFletcher32( (byte*)ptr, writer.Length ); // Ensure CalcFletcher32 accepts byte* or use NativeArray wrapping just for calc
        
            return true; 
        }
        finally
        {
            writer.Dispose();
        }
    }


    
    #endregion

  
    
    //During a rollback
    private bool OnAdvanceFrame(int flags)
    {
        if (_isPrewarming)
        {
            return true; // Tell GGPO we are alive, but don't advance the frame. prevent prediction issues
        }

        long[] inputs = GGPO.Session.SynchronizeInput(2, out _);
        UpdateGameState(inputs);
        for (int i = 0; i < inputs.Length; i++)
        {
            RecordInputHistory(i, inputs[i]);
        }

        GGPO.Session.AdvanceFrame();
        return true;
    }

    private unsafe bool OnLoadGameState(IntPtr data, int length)
    {
        // Wrap pointer in a NativeArray view just for the Reader (NO allocation, just a struct wrapper)
        var view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
            (void*)data, length, Allocator.None);
        
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, AtomicSafetyHandle.Create());
#endif

        var reader = new FastBufferReader(view, Allocator.None);
        try
        {
            ProjectileManager.Instance.ClearAllProjectilesForLoad();
            for (int i = 0; i < playerCons.Length; i++)
            {
                playerCons[i].Deserialize(ref reader);
            }
            ProjectileManager.Instance.DeserializeAll(ref reader);
            return true;
        }
        finally
        {
            reader.Dispose();
            // Do NOT dispose 'view' here, we don't own the memory, GGPO does.
        }
    }
    private static unsafe bool OnLogGameState(string fileName, IntPtr data, int idfk) => true;
    

    private unsafe void OnFreeBuffer(IntPtr data)
    {
        // GGPO is done with this frame. Free the memory we Malloc'd in SaveGameState.
        if (data != IntPtr.Zero)
        {
            UnsafeUtility.Free((void*)data, Allocator.Persistent);
        }
    }

    private static bool OnConnectedToPeer(int connected_player) => true;

    public static bool OnSynchronizingWithPeer(int synchronizing_player, int synchronizing_count,
        int synchronizing_total) => true;

    public static bool OnSynchronizedWithPeer(int synchronized_player) => true;
    public static bool OnRunning() => true;
    private static bool OnConnectionInterrupted(int _, int __) => true;
    private static bool OnConnectionResumed(int _) => true;
    private static bool OnDisconnectedFromPeer(int _) => true;
    
    private void OnDestroy(){
        GameSessionManager.Instance.UnregisterRunner(this);
        GGPO.Session.CloseSession();
    }
    
    private bool OnTimeSync(int timesync_frames_ahead)
    {
        framesAhead = timesync_frames_ahead;
        return true;
    }
    
    //new 12/1
    private void PrintNetworkStats()
    {
        var sb = new StringBuilder();
        sb.AppendLine("--- GGPO Network Stats ---");

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].type == GGPOPlayerType.GGPO_PLAYERTYPE_REMOTE)
            {
                int handle = playerHandles[i];
                GGPONetworkStats stats;
                
                int result = GGPO.Session.GetNetworkStats(handle, out stats);

                if (GGPO.SUCCEEDED(result))
                {
                    sb.AppendLine($"Player {i} (Handle {handle}):");
                    sb.AppendLine($"  Ping: {stats.ping} ms");
                    sb.AppendLine($"  Bandwidth: {stats.kbps_sent} kbps");
                    sb.AppendLine($"  Queues - Send: {stats.send_queue_len}, Recv: {stats.recv_queue_len}");
                    sb.AppendLine($"  Sync - Local Behind: {stats.local_frames_behind}, Remote Behind: {stats.remote_frames_behind}");
                }
                else
                {
                    sb.AppendLine($"Player {i}: Failed to get stats ({GGPO.GetErrorCodeMessage(result)})");
                }
            }
        }

        Debug.Log(sb.ToString());   
    }
}