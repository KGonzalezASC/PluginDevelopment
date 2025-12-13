using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.InputSystem;

public unsafe class NativePluginTest : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    private const string DLL_NAME = "__Internal";
#else
    private const string DLL_NAME = "quantum-rosette";
#endif

    // [SuppressGCTransition]: 
    // Tells the .NET Runtime to skip the "GC Transition" preamble (setting up stack frames etc).
    // RESULT: Much faster calls (closer to raw C function speed).
    // RISK: If C++ code triggers a GC or takes a long time, it can stall the GC. Safe for "Leaf" functions like this.
    // NOTE: For WebGL (IL2CPP) this often optimizes the generated C++ glue code significantly.
    [DllImport(DLL_NAME)]
    [SuppressGCTransition]
    private static extern void ExecuteBatch(IntPtr buffer, int length);
    
    
    //ptr attribute must match the exported c function (default is cdecl)
    [DllImport(DLL_NAME)]
    private static extern void RegisterCallback(IntPtr cb);
    
    // Unity calls it on its own, declaring it conflicts with the reserved function
    /*[DllImport(DLL_NAME)]
    private static extern void UnityPluginLoad(IntPtr unityInterfaces);*/
    
    [DllImport(DLL_NAME)]
    private static extern void OnUpdate();
    
    [DllImport(DLL_NAME)]
    private static extern int GetVal(int key);

    private Keyboard keyboard = null;

    void Start()
    {
        keyboard = Keyboard.current;
        Debug.Log($"Attempting to load {DLL_NAME}...");

        // Pass our static C# function pointer to C++
        // '&OnCPPMessage' compiles to the raw memory address of the function.
        // This is ZERO OVERHEAD compared to 'Marshal.GetFunctionPointerForDelegate' which creates a wrapper thunk.
        // this closest as we can get to raw C function calls as if it were native code
        IntPtr fnPtr = (IntPtr)(delegate*<int, void>)&OnCPPMessage;
        RegisterCallback(fnPtr);
    }

    void Update()
    {
        OnUpdate();

        if (Time.frameCount % 120 == 0) // Check less often
        {
             Debug.Log($"[C++] (Key 0): {GetVal(0)} | (Key 10): {GetVal(10)}");
        }

        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            SendBatch();
        }
    }

    // [UnmanagedCallersOnly]: 
    // Compiles this method specifically to be called from native code (cdecl/stdcall).
    // Does NOT create a managed Delegate object = ZERO GC ALLOCATION per frame.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnCPPMessage(int value)
    {
        // Simple log from the C++ callback
        Debug.Log($"<color=lime>Callback from C++: {value}</color>");
    }

    private void SendBatch()
    {
        // COMMAND BUFFER PROTOCOL (Binary):
        // OpCode 1 (Set): [1][Key][Val] (12 bytes)
        // OpCode 2 (Msg): [2][Val]      (8 bytes)
        
        int bufferSize = 1024;
        
        // Allocator.TempJob/Persistent: Managed by Unity, check for leaks.
        // UnsafeUtility.Malloc: Raw standard malloc. WE MUST FREE IT.
        // ZER0 GC IMPACT: This allocation is strictly unmanaged.
        void* buffer = UnsafeUtility.Malloc(bufferSize, 4, Allocator.Temp);

        try
        {
            // Write commands directly to memory (pointer arithmetic)
            int* cursor = (int*)buffer;

            // Command 1: Set Key 10 to random value
            int newVal = UnityEngine.Random.Range(1000, 9999);
            *cursor++ = 1;      // OpCode 1
            *cursor++ = 10;     // Key
            *cursor++ = newVal; // Value

            // Command 2: Trigger callback
            *cursor++ = 2;      // OpCode 2
            *cursor++ = 777;    // Payload

            int bytesWritten = (int)((byte*)cursor - (byte*)buffer);

            // Execute Batch
            // fast call due to [SuppressGCTransition]
            ExecuteBatch((IntPtr)buffer, bytesWritten);

            Debug.Log($"<color=cyan>Sent Batch ({bytesWritten} bytes). Key 10 set to {newVal}</color>");
        }
        finally
        {
            // Immediate free. No GC cleanup later.
            UnsafeUtility.Free(buffer, Allocator.Temp);
        }
    }
}
