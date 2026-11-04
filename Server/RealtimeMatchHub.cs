using System.Diagnostics;
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
    private readonly object gate = new();
    private readonly Dictionary<string, RealtimeRoom> rooms = new(StringComparer.Ordinal);
    private readonly HashSet<string> connectedPlayerIds = new(StringComparer.Ordinal);
    private int nextPlayerId;
    private int activeConnections;
    private bool disposed;

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
            if (!TryParseJoin(joinFrame, out var playerId, out var displayName, out var error))
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
                    connectedPlayerIds.Add(playerId);
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
                            room = new RealtimeRoom(Guid.NewGuid().ToString("N"));
                            rooms.Add(room.RoomId, room);
                        }

                        if (room.TryAddPlayer(Interlocked.Increment(ref nextPlayerId), playerId, displayName, connection, out player))
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
                if (!room.SubmitInput(player.PlayerId, input))
                {
                    await connection.SendAsync(new { type = "error", code = "stale_or_out_of_range_input" }, cancellationToken);
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
                room.RemovePlayer(player.PlayerId);
                lock (gate)
                {
                    connectedPlayerIds.Remove(player.ExternalId);
                    if (room.IsEmpty && rooms.Remove(room.RoomId)) room.Dispose();
                }
            }
            await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, "connection ended", CancellationToken.None);
        }
    }

    private static bool TryParseJoin(string frame, out string playerId, out string displayName, out string error)
    {
        playerId = string.Empty;
        displayName = string.Empty;
        error = "invalid_join";
        using var document = JsonDocument.Parse(frame);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.GetString() != "join") return false;
        if (!root.TryGetProperty("playerId", out var id) || id.ValueKind != JsonValueKind.String || !RequestValidation.IsPlayerId(id.GetString())) return false;
        if (id.GetString() != id.GetString()!.ToLowerInvariant()) return false;
        if (!root.TryGetProperty("displayName", out var name) || name.ValueKind != JsonValueKind.String) return false;
        var candidate = name.GetString()?.Trim() ?? string.Empty;
        if (candidate.Length is < 1 or > 24 || candidate.Any(character => !char.IsLetterOrDigit(character) && character is not ' ' and not '_' and not '-')) return false;
        playerId = id.GetString()!;
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
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly CancellationTokenSource stop = new();
        private Task? loop;
        private int tick;
        private int nextBotId = -1;
        private bool started;
        private bool botsFilled;
        private bool finished;
        private int? winnerPlayerId;
        private long matchStartedTimestamp;
        private bool disposed;

        public RealtimeRoom(string roomId) => RoomId = roomId;
        public string RoomId { get; }
        public long CreatedAt { get; } = Stopwatch.GetTimestamp();
        public bool IsEmpty { get { lock (sync) return players.Values.All(player => player.IsBot); } }
        public bool CanAcceptPlayer { get { lock (sync) return !disposed && players.Count < MaxPlayersPerRoom; } }

        public bool TryAddPlayer(int id, string externalId, string displayName, RealtimeConnection connection, out RealtimePlayer? added)
        {
            lock (sync)
            {
                if (disposed || players.Count >= MaxPlayersPerRoom)
                {
                    added = null;
                    return false;
                }
                var slot = players.Count;
                var player = new RealtimePlayer(id, externalId, displayName, false, connection,
                    new WireVector((slot % 4 - 1.5f) * 2.5f, slot / 4 == 0 ? -2.5f : 2.5f));
                players.Add(id, player);
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
                    players = players.Values.Select(player => new { playerId = player.PlayerId, displayName = player.DisplayName, isBot = player.IsBot }).ToArray()
                };
            }
        }

        public void StartLoop()
        {
            lock (sync) loop ??= Task.Run(RunLoopAsync);
        }

        public bool SubmitInput(int playerId, RealtimeInput input)
        {
            lock (sync)
            {
                var timestamp = Stopwatch.GetTimestamp();
                if (!players.TryGetValue(playerId, out var player) || player.IsBot || input.Tick <= player.LastInputTick ||
                    input.Tick > tick + TickRate * 4 || timestamp - player.LastInputTimestamp < Stopwatch.Frequency / 60) return false;
                player.LastInputTick = input.Tick;
                player.LastInputTimestamp = timestamp;
                player.Movement = input.Movement;
                player.Aim = input.Aim;
                player.FireRequested = input.Fire;
                player.AbilityRequested = input.Ability;
                return true;
            }
        }

        public void RemovePlayer(int playerId)
        {
            lock (sync) players.Remove(playerId);
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
                                players.Add(botId, new RealtimePlayer(botId, string.Empty, $"Fill Bot {Math.Abs(botId)}", true, null,
                                    new WireVector((slot % 4 - 1.5f) * 2.5f, slot / 4 == 0 ? -2.5f : 2.5f)));
                            }
                            botsFilled = true;
                        }

                        tick++;
                        var matchElapsed = TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - matchStartedTimestamp) / (double)Stopwatch.Frequency);
                        var arenaBounds = GetArenaBounds(matchElapsed);
                        foreach (var player in players.Values) Simulate(player, tick, matchElapsed, arenaBounds);
                        ResolveShots();
                        ResolveBumps(matchElapsed);
                        ResolveMatchEnd(matchElapsed);
                        var phase = finished ? "finished" : GetPhase(matchElapsed);
                        var states = players.Values.Select(player => new
                        {
                            playerId = player.PlayerId,
                            position = new { x = player.Position.X, y = player.Position.Y },
                            velocity = new { x = player.Velocity.X, y = player.Velocity.Y },
                            health = player.Health,
                            alive = player.Alive,
                            eliminations = player.Eliminations,
                            shielded = tick < player.ShieldUntilTick
                        }).ToArray();
                        recipients = players.Values.Where(player => player.Connection is not null).Select(player => player.Connection!).ToArray();
                        snapshot = new
                        {
                            type = "snapshot", tick, players = states, phase,
                            elapsedSeconds = MathF.Min((float)matchElapsed.TotalSeconds, MatchDurationSeconds),
                            remainingSeconds = MathF.Max(0f, MatchDurationSeconds - (float)matchElapsed.TotalSeconds),
                            arenaBounds = new { halfWidth = arenaBounds.HalfWidth, halfHeight = arenaBounds.HalfHeight },
                            winnerPlayerId
                        };
                    }
                    if (snapshot is not null && recipients.Length > 0)
                        await Task.WhenAll(recipients.Select(connection => SendSnapshotSafelyAsync(connection, snapshot, token)));
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

        private void Simulate(RealtimePlayer player, int tick, TimeSpan matchElapsed, ArenaBounds arenaBounds)
        {
            const float dt = 1f / TickRate;
            if (!player.Alive || finished)
            {
                player.FireRequested = false;
                player.AbilityRequested = false;
                return;
            }
            var movement = player.Movement;
            if (player.IsBot)
            {
                var phase = tick * 0.015f + Math.Abs(player.PlayerId);
                movement = new WireVector(MathF.Cos(phase), MathF.Sin(phase));
                player.Aim = movement.LengthSquared > 0.001f ? movement.Normalized : new WireVector(0f, 1f);
            }
            var velocity = new WireVector(player.Velocity.X + movement.X * 30f * dt, player.Velocity.Y + movement.Y * 30f * dt);
            var speedSquared = velocity.LengthSquared;
            const float maxSpeed = 8f;
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
            if (player.AbilityRequested && tick >= player.NextAbilityTick)
            {
                player.ShieldUntilTick = tick + TickRate;
                player.NextAbilityTick = tick + TickRate * 8;
            }
            if (player.FireRequested && tick >= player.NextFireTick)
            {
                player.ShotPending = true;
                player.NextFireTick = tick + TickRate / 2;
            }
            if (player.Position.X < -arenaBounds.HalfWidth || player.Position.X > arenaBounds.HalfWidth ||
                player.Position.Y < -arenaBounds.HalfHeight || player.Position.Y > arenaBounds.HalfHeight)
            {
                player.Health -= matchElapsed.TotalSeconds >= 60d ? 1.5f : 0.75f;
                if (player.Health <= 0f) Eliminate(player, null);
            }
            player.FireRequested = false;
            player.AbilityRequested = false;
        }

        private void ResolveShots()
        {
            foreach (var shooter in players.Values.Where(player => player.Alive && player.ShotPending))
            {
                shooter.ShotPending = false;
                var direction = shooter.Aim.Normalized;
                RealtimePlayer? target = null;
                var nearestDistance = 8f;
                foreach (var candidate in players.Values)
                {
                    if (!candidate.Alive || candidate.PlayerId == shooter.PlayerId) continue;
                    var offset = candidate.Position - shooter.Position;
                    var along = Dot(offset, direction);
                    if (along is < 0.4f or > 8f) continue;
                    var perpendicular = offset - direction * along;
                    if (perpendicular.LengthSquared > 0.55f * 0.55f || along >= nearestDistance) continue;
                    target = candidate;
                    nearestDistance = along;
                }
                if (target is null) continue;
                var damage = tick < target.ShieldUntilTick ? 12f : 34f;
                target.Health = MathF.Max(0f, target.Health - damage);
                target.Velocity += direction * (tick < target.ShieldUntilTick ? 0.8f : 1.5f);
                if (target.Health <= 0f) Eliminate(target, shooter);
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
                var overlap = (minDistance - distance) * 0.5f;
                first.Position -= normal * overlap;
                second.Position += normal * overlap;
                var relativeSpeed = Dot(second.Velocity - first.Velocity, normal);
                if (relativeSpeed > 0f) continue;
                var suddenDeathMultiplier = matchElapsed.TotalSeconds >= 60d ? 1.5f : 1f;
                var baseImpulse = MathF.Max(0.5f, -relativeSpeed + 1f) * suddenDeathMultiplier;
                var firstFlankMultiplier = Dot(first.Aim, normal) < -0.5f ? 1.5f : 1f;
                var secondFlankMultiplier = Dot(second.Aim, normal * -1f) < -0.5f ? 1.5f : 1f;
                first.Velocity -= normal * (baseImpulse * firstFlankMultiplier);
                second.Velocity += normal * (baseImpulse * secondFlankMultiplier);
                if (tick - first.LastBumpDamageTick >= TickRate * 2 && tick - second.LastBumpDamageTick >= TickRate * 2)
                {
                    var bumpDamage = matchElapsed.TotalSeconds >= 60d ? 15f : 10f;
                    if (first.ShieldUntilTick <= tick) first.Health = MathF.Max(0f, first.Health - bumpDamage);
                    if (second.ShieldUntilTick <= tick) second.Health = MathF.Max(0f, second.Health - bumpDamage);
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
                winnerPlayerId = alivePlayers.OrderByDescending(player => player.Health)
                    .ThenByDescending(player => player.Eliminations).FirstOrDefault()?.PlayerId;
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
        public RealtimePlayer(int playerId, string externalId, string displayName, bool isBot, RealtimeConnection? connection, WireVector position)
        {
            PlayerId = playerId;
            ExternalId = externalId;
            DisplayName = displayName;
            IsBot = isBot;
            Connection = connection;
            Position = position;
        }

        public int PlayerId { get; }
        public string ExternalId { get; }
        public string DisplayName { get; }
        public bool IsBot { get; }
        public RealtimeConnection? Connection { get; }
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
        public int LastBumpDamageTick { get; set; }
        public bool FireRequested { get; set; }
        public bool AbilityRequested { get; set; }
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
