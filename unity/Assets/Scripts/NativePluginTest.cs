using UnityEngine;
using System.Runtime.InteropServices;

public class NativePluginTest : MonoBehaviour
{
    // Define the DLL name. 
    // For WebGL, it must be "__Internal".
    // For Editor, it should be the name of your built DLL (quantum-rosette).
#if UNITY_WEBGL && !UNITY_EDITOR
    const string DLL_NAME = "__Internal";
#else
    const string DLL_NAME = "blind-fish";
#endif

    [DllImport(DLL_NAME)]
    private static extern void InitializePlugin();

    [DllImport(DLL_NAME)]
    private static extern int AddNumbers(int a, int b);

    void Start()
    {
        Debug.Log("Initializing Plugin...");
        InitializePlugin();
        
        int a = 5;
        int b = 7;
        Debug.Log($"Calling AddNumbers({a}, {b})...");
        int result = AddNumbers(a, b);
        
        Debug.Log($"Result: {result}");
        
        if (result == 12)
        {
            Debug.Log("<color=green>Test PASSED: Plugin is working correctly.</color>");
        }
        else
        {
            Debug.LogError($"<color=red>Test FAILED: Expected 12 but got {result}</color>");
        }
    }
}
