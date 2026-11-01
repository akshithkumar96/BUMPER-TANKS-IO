#if FUSION_PRESENT
using System;
using Fusion;
using UnityEngine;

namespace BumperTanks.Network
{
    // Activated only after the Photon Fusion SDK is imported and FUSION_PRESENT is defined.
    public sealed class FusionIntegration : MonoBehaviour, IRealtimeSession
    {
        public event Action<TankSnapshot> SnapshotReceived;
        public bool IsConnected => runner != null && runner.IsRunning;

        private NetworkRunner runner;

        public async void Connect(string roomName)
        {
            runner = gameObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = true;
            await runner.StartGame(new StartGameArgs { GameMode = GameMode.Shared, SessionName = roomName });
        }

        public void SubmitInput(PlayerInputFrame input)
        {
            // NetworkBehaviour implementations consume this frame in FixedUpdateNetwork.
        }

        public void Publish(TankSnapshot snapshot) => SnapshotReceived?.Invoke(snapshot);

        public void Disconnect()
        {
            if (runner != null) runner.Shutdown();
        }

        public void Dispose() => Disconnect();
    }
}
#endif
