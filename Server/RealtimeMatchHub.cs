using System.Diagnostics;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

/// <summary>
/// First-party WebSocket matchmaking and authoritative movement/combat prototype.
/// Match receipts and rewards remain outside this transport and require trusted
/// attestation before they can be used for production rewards.
/// </summary>
public sealed class RealtimeMatchHub : IDisposable
{
    public const int TickRate = 30;
    public const int MaxPlayersPerRoom = 8;
    public const int MaxIncomingBytes = 4096;
    public const int MaxActiveConnections = 2048;
    public const int MaxActiveRooms = 256;
    public const int MatchDurationSeconds = 90;
    public static readonly TimeSpan BotFillDelay = TimeSpan.FromMilliseconds(2500);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlayerIdentityRepository identities;
    private readonly IProfileRepository profiles;
    private readonly TimeSpan disconnectGrace;
    private readonly GameplayTuningSettings gameplayTuning;
    private readonly object gate = new();
    private readonly Dictionary<string, RealtimeRoom> rooms = new(StringComparer.Ordinal);
    private readonly HashSet<string> connectedPlayerIds = new(StringComparer.Ordinal);
    private int nextPlayerId;
    private int activeConnections;
    private bool disposed;

    public RealtimeMatchHub(IPlayerIdentityRepository identities, IProfileRepository profiles, IConfiguration? configuration = null,
        GameplayTuningSettings? gameplayTuning = null)
    {
        this.identities = identities;
        this.profiles = profiles;
        var seconds = configuration?.GetValue<int?>("Realtime:DisconnectGraceSeconds") ?? 30;
        disconnectGrace = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 120));
        this.gameplayTuning = gameplayTuning ?? GameplayTuningSettings.From(configuration ?? new ConfigurationBuilder().Build());
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (Interlocked.Increment(ref activeConnections) > MaxActiveConnections)
        {
            Interlocked.Decrement(ref activeConnections);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await RunConnectionAsync(socket, context.RequestAborted);
        }
        finally
        {
            Interlocked.Decrement(ref activeConnections);
        }
    }

    private async Task RunConnectionAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var connection = new RealtimeConnection(socket);
        RealtimeRoom? room = null;
        RealtimePlayer? player = null;
        try
        {
            var joinFrame = await ReceiveFrameAsync(socket, cancellationToken);
            if (joinFrame is null) return;
            if (!TryParseJoin(joinFrame, identities, out var playerId, out var displayName, out var archetype, out var error))
            {
                await connection.SendAsync(new { type = "error", code = error }, cancellationToken);
                await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "invalid join", cancellationToken);
                return;
            }

            lock (gate)
            {
                if (disposed)
                {
                    error = "server_shutting_down";
                }
                else if (!connectedPlayerIds.Add(playerId))
                {
                    error = "player_already_connected";
                }
                else
                {
                    foreach (var candidate in rooms.Values.OrderBy(candidate => candidate.CreatedAt))
                    {
                        if (!candidate.TryResumePlayer(playerId, connection, disconnectGrace, out player)) continue;
                        room = candidate;
                        error = string.Empty;
                        break;
                    }
                    while (player is null)
                    {
                        room = rooms.Values
                            .Where(candidate => candidate.CanAcceptPlayer)
                            .OrderBy(candidate => candidate.CreatedAt)
                            .FirstOrDefault();
                        if (room is null)
                        {
                            if (rooms.Count >= MaxActiveRooms)
                            {
                                connectedPlayerIds.Remove(playerId);
                                error = "rooms_full";
                                break;
                            }
                            room = new RealtimeRoom(Guid.NewGuid().ToString("N"), profiles, gameplayTuning);
                            rooms.Add(room.RoomId, room);
                        }

                        if (room.TryAddPlayer(Interlocked.Increment(ref nextPlayerId), playerId, displayName, archetype, connection, out player))
                        {
                            error = string.Empty;
                            break;
                        }
                        if (room.IsEmpty) rooms.Remove(room.RoomId);
                        room = null;
                    }
                }
            }

            if (room is null || player is null)
            {
                await connection.SendAsync(new { type = "error", code = string.IsNullOrEmpty(error) ? "matchmaking_unavailable" : error }, cancellationToken);
                await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "join rejected", cancellationToken);
                return;
            }

            await connection.SendAsync(room.BuildJoinedMessage(player), cancellationToken);
            room.StartLoop();
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var frame = await ReceiveFrameAsync(socket, cancellationToken);
                if (frame is null) break;
                if (!TryParseInput(frame, out var input))
                {
                    await connection.SendAsync(new { type = "error", code = "invalid_input" }, cancellationToken);
                    break;
                }
                if (!room.SubmitInput(player.PlayerId, input, out var inputError))
                {
                    await connection.SendAsync(new { type = "error", code = inputError }, cancellationToken);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (JsonException)
        {
            if (socket.State == WebSocketState.Open)
            {
                try { await connection.SendAsync(new { type = "error", code = "invalid_json" }, CancellationToken.None); }
                catch (WebSocketException) { }
            }
        }
        finally
        {
            if (player is not null && room is not null)
            {
                lock (gate)
                {
                    connectedPlayerIds.Remove(player.ExternalId);
                    if (room.MarkDisconnected(player.PlayerId, connection))
                        _ = ExpireDisconnectedPlayerAsync(room, player.PlayerId, player.ExternalId);
                }
            }
            await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, "connection ended", CancellationToken.None);
        }
    }

    private async Task ExpireDisconnectedPlayerAsync(RealtimeRoom room, int playerId, string externalId)
    {
        await Task.Delay(disconnectGrace + TimeSpan.FromMilliseconds(100));
        lock (gate)
        {
            if (!room.ExpireDisconnectedPlayer(playerId, externalId, disconnectGrace)) return;
            if (room.IsEmpty && rooms.Remove(room.RoomId)) room.Dispose();
        }
    }

    private static bool TryParseJoin(string frame, IPlayerIdentityRepository identities, out string playerId, out string displayName, out string archetype, out string error)
    {
        playerId = string.Empty;
        displayName = string.Empty;
        archetype = "SpeedyBumper";
        error = "invalid_join";
        using var document = JsonDocument.Parse(frame);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.GetString() != "join") return false;
        if (!root.TryGetProperty("accessToken", out var credential) || credential.ValueKind != JsonValueKind.String ||
            !identities.TryAuthenticate(credential.GetString(), out var authenticatedPlayerId))
        {
            error = "invalid_credentials";
            return false;
        }
        if (!root.TryGetProperty("playerId", out var id) || id.ValueKind != JsonValueKind.String || !RequestValidation.IsPlayerId(id.GetString())) return false;
        if (id.GetString() != id.GetString()!.ToLowerInvariant()) return false;
        if (!string.Equals(id.GetString(), authenticatedPlayerId, StringComparison.Ordinal))
        {
            error = "invalid_credentials";
            return false;
        }
        if (!root.TryGetProperty("displayName", out var name) || name.ValueKind != JsonValueKind.String) return false;
        var candidate = name.GetString()?.Trim() ?? string.Empty;
        if (candidate.Length is < 1 or > 24 || candidate.Any(character => !char.IsLetterOrDigit(character) && character is not ' ' and not '_' and not '-')) return false;
        if (root.TryGetProperty("archetype", out var archetypeElement))
        {
            if (archetypeElement.ValueKind != JsonValueKind.String ||
                archetypeElement.GetString() is not ("SpeedyBumper" or "HeavyDozer" or "RicochetSpinner")) return false;
            archetype = archetypeElement.GetString()!;
        }
        playerId = authenticatedPlayerId;
        displayName = candidate;
        error = string.Empty;
        return true;
    }

    private static bool TryParseInput(string frame, out RealtimeInput input)
    {
        input = default;
        using var document = JsonDocument.Parse(frame);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryProperty(root, "type", out var type) || type.GetString() != "input" ||
            !TryProperty(root, "tick", out var tickElement) || !tickElement.TryGetInt32(out var tick) || tick < 0 ||
            !TryVector(root, "movement", out var movement) || !TryVector(root, "aim", out var aim) ||
            !TryBoolean(root, "fire", out var fire) || !TryBoolean(root, "ability", out var ability)) return false;

        if (MathF.Abs(movement.X) > 1f || MathF.Abs(movement.Y) > 1f || MathF.Abs(aim.X) > 1f || MathF.Abs(aim.Y) > 1f) return false;
        if (movement.LengthSquared > 1f) movement = movement.Normalized;
        if (aim.LengthSquared > 1f) aim = aim.Normalized;
        if (aim.LengthSquared < 0.0001f) aim = new WireVector(0f, 1f);
        input = new RealtimeInput(tick, movement, aim, fire, ability);
        return true;
    }

    private static bool TryProperty(JsonElement root, string name, out JsonElement value) =>
        root.TryGetProperty(name, out value);

    private static bool TryVector(JsonElement root, string name, out WireVector vector)
    {
        vector = default;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("x", out var xElement) || !xElement.TryGetSingle(out var x) || !float.IsFinite(x) ||
            !element.TryGetProperty("y", out var yElement) || !yElement.TryGetSingle(out var y) || !float.IsFinite(y)) return false;
        vector = new WireVector(x, y);
        return true;
    }

    private static bool TryBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = element.GetBoolean();
        return true;
    }

    private static async Task<string?> ReceiveFrameAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        using var message = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > MaxIncomingBytes)
                throw new WebSocketException(WebSocketError.InvalidMessageType);
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var bytes = message.ToArray();
        if (bytes.Length < 2 || bytes[^1] != (byte)'\n' || Array.IndexOf(bytes, (byte)'\n', 0, bytes.Length - 1) >= 0)
            throw new JsonException("Expected one newline-terminated JSON object per WebSocket message.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, bytes.Length - 1).TrimEnd('\r');
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        try { await socket.CloseAsync(status, description, cancellationToken); }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var room in rooms.Values) room.Dispose();
            rooms.Clear();
            connectedPlayerIds.Clear();
        }
    }

    private sealed class RealtimeRoom : IDisposable
    {
        private readonly object sync = new();
        private readonly Dictionary<int, RealtimePlayer> players = new();
        private readonly Dictionary<string, RealtimePlayer> realParticipants = new(StringComparer.Ordinal);
        private readonly IProfileRepository profiles;
        private readonly GameplayTuningSettings gameplayTuning;
        private readonly List<RealtimeShotSegment> shotSegments = new();
        private readonly List<RealtimeGemPickup> gems = new();
        private readonly List<RealtimePowerUpPickup> powerUps = new();
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly CancellationTokenSource stop = new();
        private Task? loop;
        private int tick;
        private int nextShotId;
        private int nextBotId = -1;
        private bool started;
        private bool botsFilled;
        private bool finished;
        private bool matchReceiptsIssued;
        private int? winnerPlayerId;
        private long matchStartedTimestamp;
        private bool disposed;

        public RealtimeRoom(string roomId, IProfileRepository profiles, GameplayTuningSettings gameplayTuning)
        {
            RoomId = roomId;
            this.profiles = profiles;
            this.gameplayTuning = gameplayTuning;
            var seed = StableSeed(roomId);
            var jitter = new WireVector(((int)(seed % 5) - 2) * 0.1f, ((int)((seed / 5) % 5) - 2) * 0.1f);
            WireVector Place(float x, float y) => new(x + jitter.X, y + jitter.Y);
            var gemPositions = new[]
            {
                Place(-2.6f, -1.3f), Place(4f, 0f), Place(2f, -4f),
                Place(6f, 0f), Place(-6f, 4f), Place(0f, 0f),
                Place(6f, 4f), Place(-4f, 0f), Place(5f, 3f),
                Place(-1f, 4f), Place(1f, -4f), Place(5f, -3f)
            };
            for (var index = 0; index < gemPositions.Length; index++)
                gems.Add(new RealtimeGemPickup(index + 1, gemPositions[index]));

            // Deterministic room-seeded drops are kept away from all player spawn slots.
            powerUps.Add(new RealtimePowerUpPickup(1, "MegaBullet", Place(-2.6f, -1.3f)));
            powerUps.Add(new RealtimePowerUpPickup(2, "Nitro", Place(-0.1f, -1.3f)));
            powerUps.Add(new RealtimePowerUpPickup(3, "Magnet", Place(2.4f, -1.3f)));
        }

        private static uint StableSeed(string value)
        {
            var hash = 2166136261u;
            foreach (var character in value) hash = (hash ^ character) * 16777619u;
            return hash;
        }
        public string RoomId { get; }
        public long CreatedAt { get; } = Stopwatch.GetTimestamp();
        public bool IsEmpty { get { lock (sync) return players.Values.All(player => player.IsBot); } }
        public bool CanAcceptPlayer { get { lock (sync) return !disposed && !finished && !started && players.Count < MaxPlayersPerRoom && players.Values.All(player => player.IsBot || player.Connection is not null); } }

        public bool TryResumePlayer(string externalId, RealtimeConnection connection, TimeSpan grace, out RealtimePlayer? resumed)
        {
            lock (sync)
            {
                if (disposed || finished || !realParticipants.TryGetValue(externalId, out var player) ||
                    player.Connection is not null || player.DisconnectedAtUtc is not DateTimeOffset disconnectedAt ||
                    DateTimeOffset.UtcNow - disconnectedAt > grace)
                {
                    resumed = null;
                    return false;
                }
                player.Connection = connection;
                player.DisconnectedAtUtc = null;
                resumed = player;
                return true;
            }
        }

        public bool MarkDisconnected(int playerId, RealtimeConnection connection)
        {
            lock (sync)
            {
                if (!players.TryGetValue(playerId, out var player) || !ReferenceEquals(player.Connection, connection)) return false;
                player.Connection = null;
                player.DisconnectedAtUtc = DateTimeOffset.UtcNow;
                player.Movement = default;
                player.FireBufferedUntilTick = -1;
                player.AbilityBufferedUntilTick = -1;
                return true;
            }
        }

        public bool ExpireDisconnectedPlayer(int playerId, string externalId, TimeSpan grace)
        {
            lock (sync)
            {
                if (!players.TryGetValue(playerId, out var player) || player.ExternalId != externalId || player.Connection is not null ||
                    player.DisconnectedAtUtc is not DateTimeOffset disconnectedAt || DateTimeOffset.UtcNow - disconnectedAt < grace) return false;
                players.Remove(playerId);
                realParticipants.Remove(externalId);
                return true;
            }
        }

        public bool TryAddPlayer(int id, string externalId, string displayName, string archetype, RealtimeConnection connection, out RealtimePlayer? added)
        {
            lock (sync)
            {
                if (disposed || players.Count >= MaxPlayersPerRoom)
                {
                    added = null;
                    return false;
                }
                var slot = players.Count;
                var player = new RealtimePlayer(id, externalId, displayName, archetype, false, connection,
                    new WireVector((slot % 4 - 1.5f) * 2.5f, slot / 4 == 0 ? -2.5f : 2.5f));
                players.Add(id, player);
                realParticipants[externalId] = player;
                added = player;
                return true;
            }
        }

        public object BuildJoinedMessage(RealtimePlayer joining)
        {
            lock (sync)
            {
                return new
                {
                    type = "joined",
                    roomId = RoomId,
                    playerId = joining.PlayerId,
                    tickRate = TickRate,
                    players = players.Values.Select(player => new { playerId = player.PlayerId, displayName = player.DisplayName, archetype = player.Archetype, isBot = player.IsBot }).ToArray()
                };
            }
        }

        public void StartLoop()
        {
            lock (sync) loop ??= Task.Run(RunLoopAsync);
        }

        public bool SubmitInput(int playerId, RealtimeInput input, out string error)
        {
            lock (sync)
            {
                var timestamp = Stopwatch.GetTimestamp();
                if (!players.TryGetValue(playerId, out var player) || player.IsBot)
                {
                    error = "unknown_player";
                    return false;
                }
                if (input.Tick <= player.LastInputTick)
                {
                    error = "stale_input_tick";
                    return false;
                }
                if (input.Tick > tick + TickRate * 4)
                {
                    error = "future_input_tick";
                    return false;
                }
                if (timestamp - player.LastInputTimestamp < Stopwatch.Frequency / 60)
                {
                    error = "input_rate_limited";
                    return false;
                }
                player.LastInputTick = input.Tick;
                player.LastInputTimestamp = timestamp;
                player.Movement = input.Movement;
                player.Aim = input.Aim;
                if (input.Fire) player.FireBufferedUntilTick = Math.Max(player.FireBufferedUntilTick, tick + TickRate / 5);
                if (input.Ability) player.AbilityBufferedUntilTick = Math.Max(player.AbilityBufferedUntilTick, tick + TickRate / 5);
                error = string.Empty;
                return true;
            }
        }

        private async Task RunLoopAsync()
        {
            var token = stop.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1000d / TickRate), token);
                    object? snapshot = null;
                    RealtimeConnection[] recipients = Array.Empty<RealtimeConnection>();
                    List<(RealtimeConnection Connection, object Receipt)> matchReceipts = new();
                    lock (sync)
                    {
                        if (disposed || players.Values.All(player => player.IsBot)) return;
                        var realPlayerCount = players.Values.Count(player => !player.IsBot);
                        if (!started && (realPlayerCount >= MaxPlayersPerRoom || elapsed.Elapsed >= BotFillDelay))
                        {
                            started = true;
                            matchStartedTimestamp = Stopwatch.GetTimestamp();
                        }
                        if (!started) continue;
                        if (!botsFilled && elapsed.Elapsed >= BotFillDelay)
                        {
                            while (players.Count < MaxPlayersPerRoom)
                            {
                                var botId = nextBotId--;
                                var slot = players.Count;
                                players.Add(botId, new RealtimePlayer(botId, string.Empty, $"Fill Bot {Math.Abs(botId)}", ArchetypeForBot(botId), true, null,
                                    new WireVector((slot % 4 - 1.5f) * 2.5f, slot / 4 == 0 ? -2.5f : 2.5f)));
                            }
                            botsFilled = true;
                        }

                        tick++;
                        var matchElapsed = TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - matchStartedTimestamp) / (double)Stopwatch.Frequency);
                        var arenaBounds = GetArenaBounds(matchElapsed);
                        shotSegments.Clear();
                        var tickPlayers = players.Values.ToArray();
                        foreach (var player in tickPlayers.Where(player => player.IsBot))
                            UpdateBotInput(player, tickPlayers, tick, arenaBounds);
                        foreach (var player in tickPlayers) Simulate(player, tick, matchElapsed, arenaBounds);
                        ResolvePickups(matchElapsed);
                        ResolveWhirlwinds();
                        ResolveShots(arenaBounds);
                        ResolveBumps(matchElapsed);
                        ResolveMatchEnd(matchElapsed);
                        if (finished && !matchReceiptsIssued)
                        {
                            matchReceiptsIssued = true;
                            foreach (var participant in realParticipants.Values)
                            {
                                profiles.TryCompleteMatch(participant.ExternalId, RoomId, participant.Gems,
                                    participant.Eliminations, out var profile, out var award);
                                if (participant.Connection is not null)
                                {
                                    matchReceipts.Add((participant.Connection, new
                                    {
                                        type = "match_result",
                                        matchId = RoomId,
                                        playerId = participant.ExternalId,
                                        gems = participant.Gems,
                                        eliminations = participant.Eliminations,
                                        award,
                                        profile
                                    }));
                                }
                            }
                        }
                        var phase = finished ? "finished" : GetPhase(matchElapsed);
                        var states = players.Values.Select(player => new
                        {
                            playerId = player.PlayerId,
                            archetype = player.Archetype,
                            position = new { x = player.Position.X, y = player.Position.Y },
                            velocity = new { x = player.Velocity.X, y = player.Velocity.Y },
                            health = player.Health,
                            alive = player.Alive,
                            eliminations = player.Eliminations,
                            shielded = tick < player.ShieldUntilTick,
                            whirling = tick < player.WhirlwindUntilTick,
                            gems = player.Gems,
                            nitroActive = tick < player.NitroUntilTick,
                            megaBulletActive = tick < player.MegaBulletUntilTick,
                            magnetActive = tick < player.MagnetUntilTick
                        }).ToArray();
                        recipients = players.Values.Where(player => player.Connection is not null).Select(player => player.Connection!).ToArray();
                        snapshot = new
                        {
                            type = "snapshot", tick, players = states, phase,
                            elapsedSeconds = MathF.Min((float)matchElapsed.TotalSeconds, MatchDurationSeconds),
                            remainingSeconds = MathF.Max(0f, MatchDurationSeconds - (float)matchElapsed.TotalSeconds),
                            arenaBounds = new { halfWidth = arenaBounds.HalfWidth, halfHeight = arenaBounds.HalfHeight },
                            winnerPlayerId,
                            gems = gems.Where(gem => gem.Available).Select(gem => new
                            {
                                pickupId = gem.PickupId,
                                position = new { x = gem.Position.X, y = gem.Position.Y },
                                value = matchElapsed.TotalSeconds >= 60d ? 2 : 1
                            }).ToArray(),
                            powerUps = powerUps.Where(powerUp => powerUp.Available).Select(powerUp => new
                            {
                                pickupId = powerUp.PickupId,
                                type = powerUp.Type,
                                position = new { x = powerUp.Position.X, y = powerUp.Position.Y }
                            }).ToArray(),
                            shots = shotSegments.Select(shot => new
                            {
                                shotId = shot.ShotId,
                                ownerPlayerId = shot.OwnerPlayerId,
                                segmentIndex = shot.SegmentIndex,
                                bounceIndex = shot.BounceIndex,
                                type = shot.Type,
                                start = new { x = shot.Start.X, y = shot.Start.Y },
                                end = new { x = shot.End.X, y = shot.End.Y },
                                hitPlayerId = shot.HitPlayerId,
                                hit = shot.Hit,
                                shieldBlocked = shot.ShieldBlocked,
                                deflected = shot.DeflectedBySpinner
                            }).ToArray()
                        };
                    }
                    if (snapshot is not null && recipients.Length > 0)
                        await Task.WhenAll(recipients.Select(connection => SendSnapshotSafelyAsync(connection, snapshot, token)));
                    if (matchReceipts.Count > 0)
                        await Task.WhenAll(matchReceipts.Select(receipt => SendMatchReceiptSafelyAsync(receipt.Connection, receipt.Receipt, token)));
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (WebSocketException)
            {
            }
        }

        private static async Task SendSnapshotSafelyAsync(RealtimeConnection connection, object snapshot, CancellationToken cancellationToken)
        {
            try { await connection.SendAsync(snapshot, cancellationToken); }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
        }

        private static async Task SendMatchReceiptSafelyAsync(RealtimeConnection connection, object receipt, CancellationToken cancellationToken)
        {
            try { await connection.SendAsync(receipt, cancellationToken); }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
        }

        private void UpdateBotInput(RealtimePlayer bot, IReadOnlyList<RealtimePlayer> tickPlayers, int tick, ArenaBounds arenaBounds)
        {
            var targets = tickPlayers
                .Where(candidate => candidate.Alive && candidate.PlayerId != bot.PlayerId)
                .Select(candidate => new
                {
                    Player = candidate,
                    DistanceSquared = (candidate.Position - bot.Position).LengthSquared
                })
                .OrderBy(candidate => candidate.DistanceSquared * (candidate.Player.IsBot ? 1.2f : 1f))
                .ToArray();

            var target = targets.FirstOrDefault()?.Player;
            var movement = new WireVector(0f, 0f);
            if (target is not null)
            {
                var toTarget = target.Position - bot.Position;
                var distanceSquared = toTarget.LengthSquared;
                var distance = MathF.Sqrt(distanceSquared);
                var toward = toTarget.Normalized;
                var orbitDirection = (Math.Abs(bot.PlayerId) % 2 == 0 ? 1f : -1f);
                var tangent = new WireVector(-toward.Y * orbitDirection, toward.X * orbitDirection);

                // Close in to bumping range, then strafe to make the bots harder to line up.
                movement = distance > 2.4f
                    ? toward + tangent * 0.22f
                    : tangent + toward * 0.15f;

                // Aim directly at the target because server shots are hitscan line checks.
                var aim = toward;
                var aimError = MathF.Sin(tick * 0.071f + Math.Abs(bot.PlayerId)) * 0.012f;
                aim = new WireVector(
                    aim.X * MathF.Cos(aimError) - aim.Y * MathF.Sin(aimError),
                    aim.X * MathF.Sin(aimError) + aim.Y * MathF.Cos(aimError)).Normalized;
                bot.Aim = aim.LengthSquared > 0.001f ? aim : new WireVector(0f, 1f);

                var along = Dot(toTarget, bot.Aim);
                var perpendicular = toTarget - bot.Aim * along;
                if (distanceSquared <= 11.5f * 11.5f && along >= 0.4f && perpendicular.LengthSquared <= 0.55f * 0.55f)
                    bot.FireBufferedUntilTick = Math.Max(bot.FireBufferedUntilTick, tick + TickRate / 5);

                if (tick >= bot.NextAbilityTick)
                {
                    var shouldUseAbility = bot.Archetype switch
                    {
                        "SpeedyBumper" => distance > 3.5f,
                        "HeavyDozer" => distance <= 2.8f,
                        "RicochetSpinner" => targets.Any(candidate => candidate.DistanceSquared <= 2.5f * 2.5f),
                        _ => false
                    };
                    if (shouldUseAbility)
                        bot.AbilityBufferedUntilTick = Math.Max(bot.AbilityBufferedUntilTick, tick + 1);
                }
            }
            else
            {
                bot.Aim = new WireVector(0f, 1f);
                movement = new WireVector(-bot.Position.X, -bot.Position.Y).Normalized;
            }

            // Collapse and sudden-death bounds shrink. Steer inward before boundary damage starts.
            var edgeSteering = new WireVector(
                bot.Position.X > arenaBounds.HalfWidth - 1.2f ? -(bot.Position.X - (arenaBounds.HalfWidth - 1.2f)) :
                    bot.Position.X < -arenaBounds.HalfWidth + 1.2f ? -((bot.Position.X + arenaBounds.HalfWidth - 1.2f)) : 0f,
                bot.Position.Y > arenaBounds.HalfHeight - 1.2f ? -(bot.Position.Y - (arenaBounds.HalfHeight - 1.2f)) :
                    bot.Position.Y < -arenaBounds.HalfHeight + 1.2f ? -((bot.Position.Y + arenaBounds.HalfHeight - 1.2f)) : 0f);
            if (edgeSteering.LengthSquared > 0.0001f)
                movement += edgeSteering.Normalized * 1.5f;

            bot.Movement = movement.LengthSquared > 1f ? movement.Normalized : movement;
        }

        private void Simulate(RealtimePlayer player, int tick, TimeSpan matchElapsed, ArenaBounds arenaBounds)
        {
            const float dt = 1f / TickRate;
            if (!player.Alive || finished)
            {
                player.FireBufferedUntilTick = -1;
                player.AbilityBufferedUntilTick = -1;
                return;
            }
            var movement = player.Movement;
            var speedMultiplier = player.Archetype switch
            {
                "SpeedyBumper" => 1.3f,
                "HeavyDozer" => 0.8f,
                _ => 1f
            };
            speedMultiplier *= gameplayTuning.SpeedMultiplier;
            if (tick < player.NitroUntilTick) speedMultiplier *= 1.5f;
            if (tick <= player.AbilityBufferedUntilTick && tick >= player.NextAbilityTick)
            {
                var abilityCooldown = player.Archetype switch
                {
                    "SpeedyBumper" => TickRate * 4,
                    "HeavyDozer" => TickRate * 7,
                    _ => TickRate * 5
                };
                switch (player.Archetype)
                {
                    case "SpeedyBumper":
                        var dash = movement.LengthSquared > 0.01f ? movement.Normalized : player.Aim.Normalized;
                        player.Velocity += dash * (14f * gameplayTuning.KnockbackMultiplier / MassScale(player.Archetype));
                        player.DashUntilTick = tick + TickRate * 2 / 5;
                        break;
                    case "HeavyDozer":
                        player.ShieldUntilTick = tick + TickRate * 3;
                        break;
                    case "RicochetSpinner":
                        player.WhirlwindUntilTick = tick + TickRate * 2;
                        player.WhirlwindHitPlayerIds.Clear();
                        break;
                }
                player.NextAbilityTick = tick + abilityCooldown;
                player.AbilityBufferedUntilTick = -1;
            }
            if (tick > player.AbilityBufferedUntilTick) player.AbilityBufferedUntilTick = -1;
            var velocity = new WireVector(player.Velocity.X + movement.X * 30f * dt, player.Velocity.Y + movement.Y * 30f * dt);
            var speedSquared = velocity.LengthSquared;
            var maxSpeed = 8f * speedMultiplier;
            if (speedSquared > maxSpeed * maxSpeed)
            {
                velocity = velocity.Normalized * maxSpeed;
                speedSquared = maxSpeed * maxSpeed;
            }
            var speed = MathF.Sqrt(speedSquared);
            if (speed > 0f)
            {
                var reduced = MathF.Max(0f, speed - 7f * dt);
                velocity = velocity * (reduced / speed);
            }
            var position = new WireVector(player.Position.X + velocity.X * dt, player.Position.Y + velocity.Y * dt);
            var x = Math.Clamp(position.X, -7.4f, 7.4f);
            var y = Math.Clamp(position.Y, -5.4f, 5.4f);
            if (x != position.X) velocity = new WireVector(0f, velocity.Y);
            if (y != position.Y) velocity = new WireVector(velocity.X, 0f);
            player.Position = new WireVector(x, y);
            player.Velocity = velocity;
            if (tick >= player.NextFireTick && tick <= player.FireBufferedUntilTick)
            {
                player.ShotPending = true;
                player.FireBufferedUntilTick = -1;
                var shotImpulse = player.Archetype switch
                {
                    "SpeedyBumper" => 4.5f / MassScale(player.Archetype),
                    "HeavyDozer" => 10.8f / MassScale(player.Archetype),
                    _ => 6f / MassScale(player.Archetype)
                };
                shotImpulse *= gameplayTuning.KnockbackMultiplier;
                if (tick < player.MegaBulletUntilTick) shotImpulse *= 1.8f;
                player.Velocity -= player.Aim.Normalized * shotImpulse;
                var cooldown = player.Archetype switch
                {
                    "SpeedyBumper" => Math.Max(1, (int)MathF.Ceiling(TickRate * 0.35f * gameplayTuning.ProjectileCooldownMultiplier * 0.65f)),
                    "HeavyDozer" => (int)MathF.Ceiling(TickRate * 0.35f * gameplayTuning.ProjectileCooldownMultiplier * 1.8f),
                    _ => (int)MathF.Ceiling(TickRate * 0.35f * gameplayTuning.ProjectileCooldownMultiplier * 1.15f)
                };
                player.NextFireTick = tick + cooldown;
            }
            if (tick > player.FireBufferedUntilTick) player.FireBufferedUntilTick = -1;
            if (player.Position.X < -arenaBounds.HalfWidth || player.Position.X > arenaBounds.HalfWidth ||
                player.Position.Y < -arenaBounds.HalfHeight || player.Position.Y > arenaBounds.HalfHeight)
            {
                player.Health -= (matchElapsed.TotalSeconds >= 60d ? 1.5f : 0.75f) * dt;
                if (player.Health <= 0f) Eliminate(player, null);
            }
        }

        private void ResolveShots(ArenaBounds arenaBounds)
        {
            foreach (var shooter in players.Values.Where(player => player.Alive && player.ShotPending))
            {
                shooter.ShotPending = false;
                var direction = shooter.Aim.Normalized;
                if (direction.LengthSquared < 0.001f) direction = new WireVector(0f, 1f);
                var spinnerLaser = shooter.Archetype == "RicochetSpinner";
                var shotId = ++nextShotId;
                var maxBounces = spinnerLaser ? 3 : 0;
                var remainingRange = spinnerLaser ? 48f : 12f;
                var origin = shooter.Position;
                var segmentIndex = 0;
                while (segmentIndex <= maxBounces && remainingRange > 0.05f)
                {
                    GetBoundaryDistance(origin, direction, arenaBounds, out var wallDistance, out var reflectX, out var reflectY);
                    var segmentLimit = MathF.Min(remainingRange, wallDistance);
                    RealtimePlayer? target = null;
                    var nearestDistance = segmentLimit;
                    foreach (var candidate in players.Values)
                    {
                        if (!candidate.Alive || candidate.PlayerId == shooter.PlayerId) continue;
                        var offset = candidate.Position - origin;
                        var along = Dot(offset, direction);
                        if (along is < 0.2f || along > segmentLimit || along >= nearestDistance) continue;
                        var perpendicular = offset - direction * along;
                        if (perpendicular.LengthSquared > 0.55f * 0.55f) continue;
                        target = candidate;
                        nearestDistance = along;
                    }

                    var endpoint = origin + direction * nearestDistance;
                    var blockedByShield = false;
                    var deflectedBySpinner = false;
                    if (target is not null)
                    {
                        var shieldActive = tick < target.ShieldUntilTick && target.Archetype == "HeavyDozer";
                        var incomingFromFront = shieldActive && Dot(target.Aim.Normalized, direction * -1f) >= 0.5f;
                        blockedByShield = incomingFromFront;
                        deflectedBySpinner = !incomingFromFront && target.Archetype == "RicochetSpinner" &&
                                             tick < target.WhirlwindUntilTick;
                        if (incomingFromFront)
                        {
                            var reflectedImpulse = 1.5f * gameplayTuning.KnockbackMultiplier * (tick < shooter.MegaBulletUntilTick ? 1.8f : 1f);
                            shooter.Velocity -= direction * (reflectedImpulse * 0.5f / MassScale(shooter.Archetype));
                        }
                        else if (deflectedBySpinner)
                        {
                            shooter.Velocity -= direction * (1.2f * gameplayTuning.KnockbackMultiplier / MassScale(shooter.Archetype));
                        }
                        else
                        {
                            var damage = 34f;
                            target.Health = MathF.Max(0f, target.Health - damage);
                            var targetImpulse = 1.5f * gameplayTuning.KnockbackMultiplier;
                            if (tick < shooter.MegaBulletUntilTick) targetImpulse *= 1.8f;
                            target.Velocity += direction * (targetImpulse / MassScale(target.Archetype));
                            if (target.Health <= 0f) Eliminate(target, shooter);
                        }
                    }

                    shotSegments.Add(new RealtimeShotSegment(shotId, shooter.PlayerId, segmentIndex, segmentIndex,
                        spinnerLaser ? "RicochetLaser" : "BumperPulse", origin, endpoint,
                        target?.PlayerId ?? 0, target is not null && !blockedByShield && !deflectedBySpinner,
                        blockedByShield, deflectedBySpinner));

                    if (target is not null)
                    {
                        if ((!blockedByShield && !deflectedBySpinner) || !spinnerLaser || segmentIndex >= maxBounces) break;
                        direction *= -1f;
                        remainingRange -= nearestDistance;
                        origin = endpoint + direction * 0.05f;
                        segmentIndex++;
                        continue;
                    }

                    remainingRange -= segmentLimit;
                    if (wallDistance > segmentLimit + 0.001f || segmentIndex >= maxBounces) break;
                    if (reflectX) direction = new WireVector(-direction.X, direction.Y);
                    if (reflectY) direction = new WireVector(direction.X, -direction.Y);
                    origin = endpoint + direction * 0.05f;
                    segmentIndex++;
                }
            }
        }

        private static void GetBoundaryDistance(WireVector origin, WireVector direction, ArenaBounds bounds,
            out float distance, out bool reflectX, out bool reflectY)
        {
            var distanceX = MathF.Abs(direction.X) < 0.0001f
                ? float.PositiveInfinity
                : (direction.X > 0f ? bounds.HalfWidth - origin.X : -bounds.HalfWidth - origin.X) / direction.X;
            var distanceY = MathF.Abs(direction.Y) < 0.0001f
                ? float.PositiveInfinity
                : (direction.Y > 0f ? bounds.HalfHeight - origin.Y : -bounds.HalfHeight - origin.Y) / direction.Y;
            distanceX = distanceX >= 0f ? distanceX : float.PositiveInfinity;
            distanceY = distanceY >= 0f ? distanceY : float.PositiveInfinity;
            distance = MathF.Min(distanceX, distanceY);
            if (float.IsInfinity(distance)) distance = float.MaxValue;
            reflectX = MathF.Abs(distanceX - distance) < 0.001f;
            reflectY = MathF.Abs(distanceY - distance) < 0.001f;
        }

        private void ResolvePickups(TimeSpan matchElapsed)
        {
            var elapsedSeconds = matchElapsed.TotalSeconds;
            foreach (var powerUp in powerUps)
            {
                if (!powerUp.Available && elapsedSeconds >= powerUp.RespawnAtSeconds)
                {
                    powerUp.Available = true;
                    powerUp.RespawnCount++;
                    var desired = PowerUpRespawnPositions[(powerUp.PickupId - 1 + powerUp.RespawnCount) % PowerUpRespawnPositions.Length];
                    var bounds = GetArenaBounds(matchElapsed);
                    powerUp.Position = new WireVector(
                        Math.Clamp(desired.X, -bounds.HalfWidth, bounds.HalfWidth),
                        Math.Clamp(desired.Y, -bounds.HalfHeight, bounds.HalfHeight));
                }
            }

            foreach (var gem in gems)
            {
                if (!gem.Available) continue;
                RealtimePlayer? collector = null;
                foreach (var player in players.Values)
                {
                    if (!player.Alive) continue;
                    var radius = tick < player.MagnetUntilTick ? 4f : 0.55f;
                    if ((player.Position - gem.Position).LengthSquared <= radius * radius)
                    {
                        collector = player;
                        break;
                    }
                }
                if (collector is null) continue;
                collector.Gems += elapsedSeconds >= 60d ? 2 : 1;
                gem.Available = false;
            }

            foreach (var powerUp in powerUps)
            {
                if (!powerUp.Available) continue;
                foreach (var player in players.Values)
                {
                    if (!player.Alive || (player.Position - powerUp.Position).LengthSquared > 0.7f * 0.7f) continue;
                    var activeUntil = tick + TickRate * 5;
                    switch (powerUp.Type)
                    {
                        case "Nitro": player.NitroUntilTick = Math.Max(player.NitroUntilTick, activeUntil); break;
                        case "MegaBullet": player.MegaBulletUntilTick = Math.Max(player.MegaBulletUntilTick, activeUntil); break;
                        case "Magnet": player.MagnetUntilTick = Math.Max(player.MagnetUntilTick, activeUntil); break;
                    }
                    powerUp.Available = false;
                    powerUp.RespawnAtSeconds = elapsedSeconds + 12d;
                    break;
                }
            }
        }

        private void ResolveWhirlwinds()
        {
            foreach (var spinner in players.Values.Where(player => player.Alive && tick < player.WhirlwindUntilTick))
            foreach (var target in players.Values)
            {
                if (!target.Alive || target.PlayerId == spinner.PlayerId || spinner.WhirlwindHitPlayerIds.Contains(target.PlayerId)) continue;
                var offset = target.Position - spinner.Position;
                if (offset.LengthSquared > 2.5f * 2.5f) continue;
                var direction = offset.Normalized;
                if (direction.LengthSquared < 0.001f) direction = new WireVector(1f, 0f);
                target.Velocity += direction * 6f;
                spinner.WhirlwindHitPlayerIds.Add(target.PlayerId);
            }
        }

        private void ResolveBumps(TimeSpan matchElapsed)
        {
            var alive = players.Values.Where(player => player.Alive).ToArray();
            for (var firstIndex = 0; firstIndex < alive.Length; firstIndex++)
            for (var secondIndex = firstIndex + 1; secondIndex < alive.Length; secondIndex++)
            {
                var first = alive[firstIndex];
                var second = alive[secondIndex];
                if (!first.Alive || !second.Alive) continue;
                var delta = second.Position - first.Position;
                var distanceSquared = delta.LengthSquared;
                const float minDistance = 1.1f;
                if (distanceSquared >= minDistance * minDistance) continue;
                var distance = MathF.Sqrt(distanceSquared);
                var normal = distance > 0.0001f ? delta * (1f / distance) : new WireVector(1f, 0f);
                var overlap = minDistance - distance;
                var firstInverseMass = 1f / MassScale(first.Archetype);
                var secondInverseMass = 1f / MassScale(second.Archetype);
                var inverseMassSum = firstInverseMass + secondInverseMass;
                first.Position -= normal * (overlap * firstInverseMass / inverseMassSum);
                second.Position += normal * (overlap * secondInverseMass / inverseMassSum);
                var relativeSpeed = Dot(second.Velocity - first.Velocity, normal);
                if (relativeSpeed > 0f) continue;
                var suddenDeathMultiplier = matchElapsed.TotalSeconds >= 60d ? 1.5f : 1f;
                var firstDashMultiplier = tick < first.DashUntilTick ? 2.5f : 1f;
                var secondDashMultiplier = tick < second.DashUntilTick ? 2.5f : 1f;
                var baseImpulse = MathF.Max(0.5f, -relativeSpeed + 1f) * suddenDeathMultiplier * gameplayTuning.KnockbackMultiplier;
                var firstFlankMultiplier = Dot(first.Aim, normal) < -0.5f ? 1.5f : 1f;
                var secondFlankMultiplier = Dot(second.Aim, normal * -1f) < -0.5f ? 1.5f : 1f;
                first.Velocity -= normal * (baseImpulse * firstFlankMultiplier * secondDashMultiplier / MassScale(first.Archetype));
                second.Velocity += normal * (baseImpulse * secondFlankMultiplier * firstDashMultiplier / MassScale(second.Archetype));
                if (tick - first.LastBumpDamageTick >= TickRate * 2 && tick - second.LastBumpDamageTick >= TickRate * 2)
                {
                    var bumpDamage = matchElapsed.TotalSeconds >= 60d ? 15f : 10f;
                    if (first.Archetype != "HeavyDozer" || first.ShieldUntilTick <= tick) first.Health = MathF.Max(0f, first.Health - bumpDamage);
                    if (second.Archetype != "HeavyDozer" || second.ShieldUntilTick <= tick) second.Health = MathF.Max(0f, second.Health - bumpDamage);
                    first.LastBumpDamageTick = second.LastBumpDamageTick = tick;
                    if (first.Health <= 0f) Eliminate(first, second);
                    if (second.Health <= 0f) Eliminate(second, first);
                }
            }
        }

        private void ResolveMatchEnd(TimeSpan matchElapsed)
        {
            if (finished) return;
            var alivePlayers = players.Values.Where(player => player.Alive).ToArray();
            if (alivePlayers.Length <= 1 && players.Count >= 2)
            {
                finished = true;
                winnerPlayerId = alivePlayers.FirstOrDefault()?.PlayerId;
                return;
            }
            if (matchElapsed.TotalSeconds >= MatchDurationSeconds)
            {
                finished = true;
                winnerPlayerId = alivePlayers.OrderByDescending(player => player.Gems)
                    .ThenByDescending(player => player.Eliminations)
                    .ThenByDescending(player => player.Health).FirstOrDefault()?.PlayerId;
            }
        }

        private void Eliminate(RealtimePlayer player, RealtimePlayer? killer)
        {
            if (!player.Alive) return;
            player.Alive = false;
            player.Health = 0f;
            player.Velocity = new WireVector(0f, 0f);
            if (killer is not null && killer.PlayerId != player.PlayerId) killer.Eliminations++;
        }

        private static string GetPhase(TimeSpan elapsed) => elapsed.TotalSeconds >= MatchDurationSeconds
            ? "finished"
            : elapsed.TotalSeconds >= 60d ? "sudden_death"
            : elapsed.TotalSeconds >= 30d ? "collapse" : "opening";

        private static ArenaBounds GetArenaBounds(TimeSpan elapsed)
        {
            const float initialHalfWidth = 7.4f;
            const float initialHalfHeight = 5.4f;
            const float finalHalfSize = 1.5f;
            if (elapsed.TotalSeconds < 30d) return new ArenaBounds(initialHalfWidth, initialHalfHeight);
            if (elapsed.TotalSeconds >= 60d) return new ArenaBounds(finalHalfSize, finalHalfSize);

            var shrinkStep = Math.Clamp((int)((elapsed.TotalSeconds - 30d) / 5d) + 1, 1, 6);
            var progress = shrinkStep / 6f;
            return new ArenaBounds(
                initialHalfWidth + (finalHalfSize - initialHalfWidth) * progress,
                initialHalfHeight + (finalHalfSize - initialHalfHeight) * progress);
        }

        private static float Dot(WireVector left, WireVector right) => left.X * right.X + left.Y * right.Y;

        private static string ArchetypeForBot(int botId) => (Math.Abs(botId) % 3) switch
        {
            1 => "HeavyDozer",
            2 => "RicochetSpinner",
            _ => "SpeedyBumper"
        };

        private static float MassScale(string archetype) => archetype switch
        {
            "SpeedyBumper" => 0.7f,
            "HeavyDozer" => 1.5f,
            _ => 1f
        };

        private static readonly WireVector[] PowerUpRespawnPositions =
        {
            new(-5f, 1f), new(0f, -4f), new(5f, 1f), new(0f, 4f), new(5f, -3.5f), new(-5f, -3.5f)
        };

        private sealed class RealtimeGemPickup
        {
            public RealtimeGemPickup(int pickupId, WireVector position) => (PickupId, Position) = (pickupId, position);
            public int PickupId { get; }
            public WireVector Position { get; }
            public bool Available { get; set; } = true;
        }

        private sealed class RealtimePowerUpPickup
        {
            public RealtimePowerUpPickup(int pickupId, string type, WireVector position) =>
                (PickupId, Type, Position) = (pickupId, type, position);
            public int PickupId { get; }
            public string Type { get; }
            public WireVector Position { get; set; }
            public bool Available { get; set; } = true;
            public double RespawnAtSeconds { get; set; }
            public int RespawnCount { get; set; }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                stop.Cancel();
                stop.Dispose();
                foreach (var player in players.Values) player.Connection?.Dispose();
                players.Clear();
            }
        }
    }

    private sealed class RealtimePlayer
    {
        public RealtimePlayer(int playerId, string externalId, string displayName, string archetype, bool isBot, RealtimeConnection? connection, WireVector position)
        {
            PlayerId = playerId;
            ExternalId = externalId;
            DisplayName = displayName;
            Archetype = archetype;
            IsBot = isBot;
            Connection = connection;
            Position = position;
        }

        public int PlayerId { get; }
        public string ExternalId { get; }
        public string DisplayName { get; }
        public string Archetype { get; }
        public bool IsBot { get; }
        public RealtimeConnection? Connection { get; set; }
        public DateTimeOffset? DisconnectedAtUtc { get; set; }
        public WireVector Position { get; set; }
        public WireVector Velocity { get; set; }
        public WireVector Movement { get; set; }
        public WireVector Aim { get; set; } = new(0f, 1f);
        public float Health { get; set; } = 100f;
        public bool Alive { get; set; } = true;
        public int Eliminations { get; set; }
        public int LastInputTick { get; set; } = -1;
        public long LastInputTimestamp { get; set; }
        public int NextFireTick { get; set; }
        public int NextAbilityTick { get; set; }
        public int ShieldUntilTick { get; set; }
        public int WhirlwindUntilTick { get; set; }
        public int DashUntilTick { get; set; }
        public int LastBumpDamageTick { get; set; }
        public int FireBufferedUntilTick { get; set; } = -1;
        public int Gems { get; set; }
        public int NitroUntilTick { get; set; }
        public int MegaBulletUntilTick { get; set; }
        public int MagnetUntilTick { get; set; }
        public HashSet<int> WhirlwindHitPlayerIds { get; } = new();
        public int AbilityBufferedUntilTick { get; set; } = -1;
        public bool ShotPending { get; set; }
    }

    private sealed class RealtimeConnection : IDisposable
    {
        private readonly WebSocket socket;
        private readonly SemaphoreSlim sendGate = new(1, 1);

        public RealtimeConnection(WebSocket socket) => this.socket = socket;

        public async Task SendAsync(object payload, CancellationToken cancellationToken)
        {
            if (socket.State != WebSocketState.Open) return;
            var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            var frame = new byte[json.Length + 1];
            Buffer.BlockCopy(json, 0, frame, 0, json.Length);
            frame[^1] = (byte)'\n';
            await sendGate.WaitAsync(cancellationToken);
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.SendAsync(frame.AsMemory(), WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                sendGate.Release();
            }
        }

        public void Dispose() => sendGate.Dispose();
    }

    private readonly record struct RealtimeInput(int Tick, WireVector Movement, WireVector Aim, bool Fire, bool Ability);
    private sealed record RealtimeShotSegment(int ShotId, int OwnerPlayerId, int SegmentIndex, int BounceIndex,
        string Type, WireVector Start, WireVector End, int HitPlayerId, bool Hit, bool ShieldBlocked,
        bool DeflectedBySpinner);
    private readonly record struct ArenaBounds(float HalfWidth, float HalfHeight);
    private readonly record struct WireVector(float X, float Y)
    {
        public float LengthSquared => X * X + Y * Y;
        public WireVector Normalized
        {
            get
            {
                var length = MathF.Sqrt(LengthSquared);
                return length > 0f ? new WireVector(X / length, Y / length) : new WireVector(0f, 0f);
            }
        }
        public static WireVector operator +(WireVector left, WireVector right) => new(left.X + right.X, left.Y + right.Y);
        public static WireVector operator -(WireVector left, WireVector right) => new(left.X - right.X, left.Y - right.Y);
        public static WireVector operator *(WireVector vector, float scalar) => new(vector.X * scalar, vector.Y * scalar);
    }
}
