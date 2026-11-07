using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Collections;
#else
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
#endif
#if !UNITY_WEBGL || UNITY_EDITOR
using System.IO;
using System.Net.WebSockets;
#endif

namespace BumperTanks.Network
{
    /// <summary>
    /// Realtime WebSocket client. Native builds use ClientWebSocket; WebGL uses the browser WebSocket API.
    /// Incoming events are dispatched from Update so consumers can safely touch Unity objects.
    /// </summary>
    [Preserve]
    public sealed class WebSocketRealtimeSession : MonoBehaviour, IRealtimeSession
    {
        public const int MaxFrameBytes = 16 * 1024;
        private const int MaxReconnectAttempts = 3;
        private static int nextWebGlId;

        public event Action<TankSnapshot> SnapshotReceived;
        public event Action<RealtimePickupFrame> PickupFrameReceived;
        public event Action<RealtimeMatchReceipt> MatchReceiptReceived;
        public event Action<RealtimeRoomJoined> RoomJoined;
        public event Action<string> Error;
        public event Action Disconnected;

        public bool IsConnected { get; private set; }
        public bool IsConnecting { get; private set; }
        public string RoomId { get; private set; }
        public int PlayerId { get; private set; } = -1;
        public int TickRate { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        private readonly Queue<Action> mainThreadQueue = new();
#else
        private readonly ConcurrentQueue<Action> mainThreadQueue = new();
#endif
        private Uri endpoint;
        private string playerId;
        private string accessToken;
        private string displayName;
        private string archetype = "SpeedyBumper";
        private int webGlId;
        private bool explicitDisconnect;
        private bool reconnectScheduled;
        private int reconnectAttempts;
        private int transportGeneration;
        private int latestSnapshotTick = -1;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void BumperSocketConnect(string url, int id, string receiver);
        [DllImport("__Internal")] private static extern void BumperSocketSend(int id, string message);
        [DllImport("__Internal")] private static extern void BumperSocketClose(int id);
#else
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private readonly SemaphoreSlim sendLock = new(1, 1);
#endif

        public void Connect(string url, string stablePlayerId, string name, string tankArchetype = "SpeedyBumper", string bearerToken = "")
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out endpoint) ||
                (endpoint.Scheme != "ws" && endpoint.Scheme != "wss"))
            {
                QueueError("Realtime endpoint must be a ws:// or wss:// URL.");
                return;
            }
            if (!IsStablePlayerId(stablePlayerId))
            {
                QueueError("Realtime player ID must be a 32-character profile ID.");
                return;
            }

            playerId = stablePlayerId;
            accessToken = bearerToken ?? string.Empty;
            displayName = NormalizeDisplayName(name);
            archetype = RealtimeMessageCodec.NormalizeArchetype(tankArchetype);
            explicitDisconnect = false;
            ConnectTransport();
        }

        /// <summary>Reopens the last configured room connection with bounded exponential backoff.</summary>
        public void Reconnect()
        {
            if (endpoint == null || string.IsNullOrEmpty(playerId) || IsConnecting || IsConnected) return;
            explicitDisconnect = false;
            reconnectAttempts = 0;
            ScheduleReconnect();
        }

        public void SubmitInput(PlayerInputFrame input)
        {
            if (!IsConnected) return;
            SendFrame(RealtimeMessageCodec.EncodeInput(input) + "\n");
        }

        private void ConnectTransport()
        {
            if (IsConnecting || IsConnected || endpoint == null) return;
            IsConnecting = true;
            RoomId = null;
            PlayerId = -1;
            TickRate = 0;
            latestSnapshotTick = -1;
#if UNITY_WEBGL && !UNITY_EDITOR
            webGlId = ++nextWebGlId;
            transportGeneration = webGlId;
            try { BumperSocketConnect(endpoint.AbsoluteUri, webGlId, gameObject.name); }
            catch (Exception exception)
            {
                OnTransportError(exception.Message);
                OnTransportClosed(webGlId);
            }
#else
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(TimeSpan.FromSeconds(15));
            socket?.Dispose();
            socket = new ClientWebSocket();
            var generation = ++transportGeneration;
            _ = ConnectNativeAsync(socket, cancellation, generation);
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private async Task ConnectNativeAsync(ClientWebSocket current, CancellationTokenSource lifetime, int generation)
        {
            var token = lifetime.Token;
            try
            {
                await current.ConnectAsync(endpoint, token).ConfigureAwait(false);
                QueueMain(() => OnTransportOpen(generation));
                lifetime.CancelAfter(Timeout.Infinite);
                await ReceiveNativeAsync(current, token, generation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { QueueMain(() => OnTransportError(generation, exception.Message)); }
            finally
            {
                current.Dispose();
                QueueMain(() => OnTransportClosed(generation));
            }
        }

        private async Task ReceiveNativeAsync(ClientWebSocket current, CancellationToken token, int generation)
        {
            var buffer = new byte[4096];
            using var frame = new MemoryStream();
            while (!token.IsCancellationRequested && current.State == WebSocketState.Open)
            {
                var result = await current.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType != WebSocketMessageType.Text) throw new InvalidOperationException("Only text WebSocket frames are supported.");
                if (frame.Length + result.Count > MaxFrameBytes) throw new InvalidOperationException("Realtime message exceeded the 16 KB limit.");
                frame.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var text = Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length);
                frame.SetLength(0);
                QueueMain(() =>
                {
                    if (generation == transportGeneration) ReceiveText(text);
                });
            }
        }

        private async Task SendFrameNativeAsync(string text, ClientWebSocket current, CancellationToken token)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > MaxFrameBytes) throw new InvalidOperationException("Realtime message exceeded the 16 KB limit.");
            await sendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (current.State == WebSocketState.Open)
                    await current.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            }
            finally { sendLock.Release(); }
        }
#endif

        private string BuildJoinFrame() => RealtimeMessageCodec.EncodeJoin(playerId, displayName, archetype, accessToken) + "\n";

        private void SendFrame(string frame)
        {
            if (Encoding.UTF8.GetByteCount(frame) > MaxFrameBytes)
            {
                QueueError("Realtime message exceeded the 16 KB limit.");
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            try { BumperSocketSend(webGlId, frame); }
            catch (Exception exception) { QueueError(exception.Message); }
#else
            var current = socket;
            var generation = transportGeneration;
            var token = cancellation != null ? cancellation.Token : CancellationToken.None;
            if (current == null || current.State != WebSocketState.Open) return;
            _ = SendFrameNativeAsync(frame, current, token).ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                    QueueMain(() => OnTransportError(generation, task.Exception.GetBaseException().Message));
            }, TaskScheduler.Default);
#endif
        }

        // Called by the WebGL .jslib bridge. The native transport queues the same callbacks to Update.
        [Preserve] public void OnWebSocketOpen(string connectionId)
        {
            if (!int.TryParse(connectionId, out var id) || id != webGlId) return;
            OnTransportOpen(id);
        }

        [Preserve] public void OnWebSocketMessage(string payload)
        {
            var split = payload?.IndexOf('\n') ?? -1;
            if (split < 0 || !int.TryParse(payload.Substring(0, split), out var id) || id != webGlId) return;
            ReceiveText(payload.Substring(split + 1));
        }

        [Preserve] public void OnWebSocketError(string payload)
        {
            var split = payload?.IndexOf('\n') ?? -1;
            if (split < 0 || !int.TryParse(payload.Substring(0, split), out var id) || id != webGlId) return;
            OnTransportError(payload.Substring(split + 1));
        }

        [Preserve] public void OnWebSocketClose(string connectionId)
        {
            var split = connectionId?.IndexOf('\n') ?? -1;
            var idText = split >= 0 ? connectionId.Substring(0, split) : connectionId;
            if (int.TryParse(idText, out var id) && id == webGlId) OnTransportClosed(id);
        }

        private void ReceiveText(string text)
        {
            if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) > MaxFrameBytes)
            {
                OnTransportError("Realtime message was empty or exceeded the 16 KB limit.");
                return;
            }

            var lines = text.Split('\n');
            foreach (var line in lines)
            {
                var json = line.Trim('\r', '\n', ' ', '\t');
                if (json.Length == 0) continue;
                try { ProcessServerMessage(json); }
                catch (Exception exception) { OnTransportError("Invalid realtime message: " + exception.Message); }
            }
        }

        private void ProcessServerMessage(string json)
        {
            var messageType = RealtimeMessageCodec.ReadType(json);
            if (string.IsNullOrEmpty(messageType)) throw new FormatException("Message has no type.");
            switch (messageType)
            {
                case "joined":
                {
                    if (!RealtimeMessageCodec.TryReadJoined(json, out var joined))
                        throw new FormatException("Joined response is missing room/player/tick-rate data.");
                    RoomId = joined.RoomId;
                    PlayerId = joined.PlayerId;
                    TickRate = joined.TickRate;
                    RoomJoined?.Invoke(joined);
                    break;
                }
                case "snapshot":
                {
                    if (!RealtimeMessageCodec.TryReadSnapshotFrame(json, out var tick, out var snapshots, out var gems, out var powerUps))
                        throw new FormatException("Snapshot contains invalid tick/player state.");
                    if (tick < latestSnapshotTick) return;
                    PickupFrameReceived?.Invoke(new RealtimePickupFrame(tick, gems, powerUps));
                    foreach (var snapshot in snapshots)
                    {
                        if (snapshot.Tick < latestSnapshotTick) continue;
                        latestSnapshotTick = snapshot.Tick;
                        SnapshotReceived?.Invoke(snapshot);
                    }
                    break;
                }
                case "match_result":
                {
                    if (!RealtimeMessageCodec.TryReadMatchReceipt(json, out var receipt))
                        throw new FormatException("Match result is missing valid server reward data.");
                    MatchReceiptReceived?.Invoke(receipt);
                    break;
                }
                case "error":
                {
                    var error = RealtimeMessageCodec.ReadError(json);
                    QueueError(string.IsNullOrEmpty(error) ? "Realtime server rejected the request." : error);
                    break;
                }
                default: throw new FormatException("Unknown server message type '" + messageType + "'.");
            }
        }

        private void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            while (mainThreadQueue.TryDequeue(out var action)) action?.Invoke();
#endif
        }

        private void QueueMain(Action action)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            action?.Invoke();
#else
            mainThreadQueue.Enqueue(action);
#endif
        }

        private void QueueError(string message) => QueueMain(() => Error?.Invoke(message));

        private void OnTransportError(string message) => OnTransportError(transportGeneration, message);

        private void OnTransportError(int generation, string message)
        {
            if (generation != transportGeneration) return;
            IsConnecting = false;
            Error?.Invoke(string.IsNullOrEmpty(message) ? "Realtime connection failed." : message);
        }

        private void OnTransportOpen(int generation)
        {
            if (generation != transportGeneration) return;
            IsConnecting = false;
            IsConnected = true;
            reconnectAttempts = 0;
            SendFrame(BuildJoinFrame());
        }

        private void OnTransportClosed(int generation)
        {
            if (generation != transportGeneration) return;
            var wasConnected = IsConnected || IsConnecting;
            IsConnected = false;
            IsConnecting = false;
            if (wasConnected) Disconnected?.Invoke();
            if (!explicitDisconnect) ScheduleReconnect();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void ScheduleReconnect()
        {
            if (reconnectScheduled || explicitDisconnect || endpoint == null || reconnectAttempts >= MaxReconnectAttempts) return;
            reconnectScheduled = true;
            var delay = 0.5f * (1 << reconnectAttempts);
            reconnectAttempts++;
            StartCoroutine(ReconnectAfter(delay));
        }

        private IEnumerator ReconnectAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            reconnectScheduled = false;
            if (!explicitDisconnect && !IsConnected && !IsConnecting) ConnectTransport();
        }
#else
        private async void ScheduleReconnect()
        {
            if (reconnectScheduled || explicitDisconnect || endpoint == null || reconnectAttempts >= MaxReconnectAttempts) return;
            reconnectScheduled = true;
            var delay = 500 * (1 << reconnectAttempts);
            reconnectAttempts++;
            await Task.Delay(delay);
            QueueMain(() =>
            {
                reconnectScheduled = false;
                if (!explicitDisconnect && !IsConnected && !IsConnecting) ConnectTransport();
            });
        }
#endif

        public void Disconnect()
        {
            var wasConnected = IsConnected || IsConnecting;
            explicitDisconnect = true;
            IsConnected = false;
            IsConnecting = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (webGlId != 0) BumperSocketClose(webGlId);
#else
            cancellation?.Cancel();
            try { socket?.Abort(); } catch (ObjectDisposedException) { }
#endif
            if (wasConnected) Disconnected?.Invoke();
        }

        public void Dispose()
        {
            Disconnect();
#if !UNITY_WEBGL || UNITY_EDITOR
            socket?.Dispose();
            cancellation?.Dispose();
#endif
            SnapshotReceived = null;
            PickupFrameReceived = null;
            RoomJoined = null;
            Error = null;
            Disconnected = null;
        }

        private void OnDestroy() => Dispose();

        private static bool IsStablePlayerId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 32) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static string NormalizeDisplayName(string value)
        {
            var input = (value ?? string.Empty).Trim();
            var normalized = new StringBuilder(Math.Min(input.Length, 24));
            foreach (var character in input)
            {
                if (!char.IsLetterOrDigit(character) && character != ' ' && character != '_' && character != '-') continue;
                normalized.Append(character);
                if (normalized.Length == 24) break;
            }
            return normalized.Length == 0 ? "Player" : normalized.ToString();
        }
    }

    public readonly struct RealtimeRoomJoined
    {
        public RealtimeRoomJoined(string roomId, int playerId, int tickRate, RealtimePlayerInfo[] players)
        {
            RoomId = roomId;
            PlayerId = playerId;
            TickRate = tickRate;
            Players = players;
        }
        public string RoomId { get; }
        public int PlayerId { get; }
        public int TickRate { get; }
        public RealtimePlayerInfo[] Players { get; }
    }

    [Serializable] public sealed class RealtimePlayerInfo { public int playerId; public string displayName; public string archetype; public bool isBot; }
    [Serializable] internal sealed class RealtimeMessageHeader { public string type; }
    [Serializable] internal sealed class RealtimeJoinMessage { public string type; public string playerId; public string displayName; public string archetype; public string accessToken; }
    [Serializable] internal sealed class RealtimeInputMessage
    {
        public string type;
        public int tick;
        public RealtimeVector movement;
        public RealtimeVector aim;
        public bool fire;
        public bool ability;
    }
    [Serializable] internal sealed class RealtimeJoinedMessage
    {
        public string type;
        public string roomId;
        public int playerId;
        public int tickRate;
        public RealtimePlayerInfo[] players;
    }
    [Serializable] internal sealed class RealtimeSnapshotMessage
    {
        public string type;
        public int tick;
        public string phase;
        public float elapsedSeconds;
        public float remainingSeconds;
        public int winnerPlayerId;
        public RealtimeArenaBounds arenaBounds;
        public RealtimeGemPickupMessage[] gems;
        public RealtimePowerUpPickupMessage[] powerUps;
        public RealtimeSnapshotPlayer[] players;
    }
    [Serializable] internal sealed class RealtimeArenaBounds { public float halfWidth; public float halfHeight; }
    [Serializable] internal sealed class RealtimeGemPickupMessage
    {
        public int pickupId;
        public RealtimeVector position;
        public int value;
    }
    [Serializable] internal sealed class RealtimePowerUpPickupMessage
    {
        public int pickupId;
        public string type;
        public RealtimeVector position;
    }
    [Serializable] internal sealed class RealtimeSnapshotPlayer
    {
        public int playerId;
        public RealtimeVector position;
        public RealtimeVector velocity;
        public float health;
        public bool alive;
        public int eliminations;
        public bool shielded;
        public bool whirling;
        public string archetype;
        public int gems;
        public bool nitroActive;
        public bool megaBulletActive;
        public bool magnetActive;
    }
    [Serializable] internal sealed class RealtimeErrorMessage { public string type; public string message; public string code; }
    [Serializable] internal sealed class RealtimeVector { public float x; public float y; }
}
