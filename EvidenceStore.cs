using UnityEngine;

namespace KerbalismContractScienceBridge
{
    internal static class Log
    {
        internal static bool Verbose { get; set; }

        internal static void Info(string message)
        {
            Debug.Log("[KerbalismContractScienceBridge] " + message);
        }

        internal static void DebugMessage(string message)
        {
            if (Verbose)
                Debug.Log("[KerbalismContractScienceBridge] " + message);
        }

        internal static void Warning(string message)
        {
            Debug.LogWarning("[KerbalismContractScienceBridge] " + message);
        }

        internal static void Error(string message)
        {
            Debug.LogError("[KerbalismContractScienceBridge] " + message);
        }
    }
}
