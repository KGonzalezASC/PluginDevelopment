using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

public class NativePluginTest : MonoBehaviour
{
    private const string DLL_NAME = "quantum-rosette";

    [DllImport(DLL_NAME)]
    private static extern void UnityPluginLoad(IntPtr unityInterfaces);

    [DllImport(DLL_NAME)]
    private static extern void UnityPluginUnload();

    [DllImport(DLL_NAME)]
    private static extern void OnUpdate();

    [DllImport(DLL_NAME)]
    private static extern void SetVal(int key, int value);

    [DllImport(DLL_NAME)]
    private static extern int GetVal(int key);

    private Keyboard keyboard = null;

    void Start()
    {
        keyboard = Keyboard.current;

        if (keyboard == null)
        {
            Debug.LogWarning("Keyboard not detected by the Input System.");
        }

        Debug.Log($"Attempting to load {DLL_NAME}...");

        SetVal(10, 42);
        Debug.Log($"Initial Check (Key 10): {GetVal(10)}");
    }

    void Update()
    {
        OnUpdate();

        if (Time.frameCount % 60 == 0)
        {
            int frame = GetVal(0);
            int userVal = GetVal(10);
            Debug.Log($"[C++] Frame (Key 0): {frame} | User (Key 10): {userVal}");
        }
        // using always new input system!
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            int newVal = UnityEngine.Random.Range(100, 999);
            SetVal(10, newVal);
            Debug.Log($"<color=cyan>Writing {newVal} to Key 10</color>");
        }
    }
}
