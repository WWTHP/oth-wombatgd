extern alias sysinterop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppFusion;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;

namespace AbominableSnowman
{
    /// <summary>
    /// Mod-to-mod messaging over the game's own Photon Fusion connection, using
    /// Fusion "reliable data" with a ReliableKey the game never uses.
    ///
    /// Receiving: the game's OverlandNetworkRunner.OnReliableDataReceived only acts
    /// on keys whose first int is 1 (its RuntimePlayer handshake), so unmodded
    /// players silently ignore our packets. We hook that function natively and
    /// swallow packets carrying our key before the game sees them.
    ///
    /// Topology: in host mode clients can only talk to the host, so clients send
    /// to the host and the host relays to everyone else. Sync therefore needs the
    /// host to have the mod; without it everything still works locally.
    ///
    /// The ReadOnlySpan&lt;byte&gt; parameters are handled as raw {pointer, length}
    /// structs rather than through Il2CppInterop's span wrappers.
    /// </summary>
    internal static unsafe class Net
    {
        private const int KeyMagic = 0x49544559; // "YETI"
        private const int Protocol = 1;
        private const int MaxPacket = 8 * 1024;

        internal enum Msg : byte { Hello = 1, Snapshot, Settings, Spawn, DespawnAll, Throw }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawKey { public int A, B, C, D; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawSpan { public IntPtr Ptr; public int Length; }

        // Native signature of OverlandNetworkRunner.OnReliableDataReceived (IL2CPP, x64):
        // (this, runner, PlayerRef player, ReliableKey* key, ReadOnlySpan<byte>* data, MethodInfo*)
        private static delegate* unmanaged<IntPtr, IntPtr, int, RawKey*, RawSpan*, IntPtr, void> _next;
        private static IntPtr _sendToServer, _sendToPlayer;
        private static IntPtr* _slot;
        private static IntPtr _recvInfo, _detourPtr;
        private static bool _initDone;
        private static bool _chained;            // another mod's hook sits between us and the game
        private static float _spawnedAt = -1f, _hookedAt = -1f;
        private const float HookDelay = 3f;      // seconds after spawn before hooking
        private const float NetWatchSeconds = 90f; // crash-guard window after hooking
        private static readonly Queue<(int from, byte[] data)> Inbox = new();

        public static string Status { get; private set; } = "waiting for the game to load";
        public static bool Ready { get; private set; }

        /// <summary>Handler for incoming messages: (type, sender, reader).</summary>
        public static Action<Msg, int, BinaryReader> OnMessage;

        /// <summary>Resolves the method handles. Hooking happens later, see <see cref="EnsureHooked"/>.</summary>
        public static void Init()
        {
            try
            {
                _sendToServer = MethodInfoPtr(typeof(NetworkRunner), nameof(NetworkRunner.SendReliableDataToServer));
                _sendToPlayer = MethodInfoPtr(typeof(NetworkRunner), nameof(NetworkRunner.SendReliableDataToPlayer));
                _recvInfo = MethodInfoPtr(typeof(OverlandNetworkRunner), nameof(OverlandNetworkRunner.OnReliableDataReceived));
                _initDone = true;
            }
            catch (Exception ex)
            {
                Status = "disabled: " + ex.Message;
                Mod.Log.Error("Multiplayer sync disabled, could not resolve Fusion methods: " + ex);
            }
        }

        /// <summary>
        /// Installs the receive hook once the local player has fully spawned, so the
        /// game's own join handshake never passes through mod code while loading.
        ///
        /// Instead of patching machine code (an earlier version did, and corrupted the
        /// game's RuntimePlayer handshake), we swap OverlandNetworkRunner's vtable entry
        /// for OnReliableDataReceived. Fusion calls it through INetworkRunnerCallbacks,
        /// i.e. via that table, and we call the original function pointer untouched.
        /// </summary>
        public static void EnsureHooked()
        {
            if (Ready)
            {
                if (_hookedAt >= 0f && UnityEngine.Time.unscaledTime - _hookedAt > NetWatchSeconds)
                {
                    CrashGuard.Clear("net");
                    _hookedAt = -1f;
                }
                return;
            }
            if (!_initDone) return;
            if (!Settings.MultiplayerSync.Value) { Status = "off"; return; }
            if (CrashGuard.IsDisabled("net")) { _initDone = false; Status = "disabled after a crash"; return; }
            bool spawned;
            try { spawned = OverlandNetworkRunner.IsLocalPlayerSpawned; }
            catch { return; }
            if (!spawned) { _spawnedAt = -1f; return; }
            if (_spawnedAt < 0f) _spawnedAt = UnityEngine.Time.unscaledTime;
            // Keep the game's join handshake well clear of mod code.
            if (UnityEngine.Time.unscaledTime - _spawnedAt < HookDelay) return;

            _initDone = false; // one attempt only
            try
            {
                IntPtr original = *(IntPtr*)_recvInfo; // MethodInfo.methodPointer
                var klass = Il2CppClassPointerStore<OverlandNetworkRunner>.NativeClassPtr;
                if (original == IntPtr.Zero || klass == IntPtr.Zero) throw new InvalidOperationException("null class or method");

                // Il2CppClass ends with VirtualInvokeData vtable[] = { methodPtr, MethodInfo* }.
                // Match on the MethodInfo only: another mod may already have replaced methodPtr.
                int found = 0;
                byte* p = (byte*)klass;
                for (int off = 0x40; off < 0x4000; off += 8)
                {
                    var e = (IntPtr*)(p + off);
                    if (e[1] == _recvInfo && e[0] != IntPtr.Zero) { _slot = e; found++; }
                }
                if (found != 1) throw new InvalidOperationException($"found {found} vtable entries for OnReliableDataReceived");

                CrashGuard.Enter("net"); // stays marked for NetWatchSeconds
                _hookedAt = UnityEngine.Time.unscaledTime;
                _next = (delegate* unmanaged<IntPtr, IntPtr, int, RawKey*, RawSpan*, IntPtr, void>)_slot[0];
                delegate* unmanaged<IntPtr, IntPtr, int, RawKey*, RawSpan*, IntPtr, void> detour = &Detour;
                _detourPtr = (IntPtr)detour;
                _chained = (IntPtr)_next != original;
                _slot[0] = _detourPtr;
                Ready = true;
                Status = "ready";
                Mod.Log.Msg(_chained ? "Multiplayer sync ready (chained after another mod's hook)."
                                     : "Multiplayer sync ready.");
            }
            catch (Exception ex)
            {
                Status = "disabled: " + ex.Message;
                Mod.Log.Warning("Multiplayer sync disabled (yetis still work locally): " + ex.Message);
            }
        }

        /// <summary>Restores the previous handler (on quit), unless another mod has chained on top of us.</summary>
        public static void Unhook()
        {
            if (_slot != null && _slot[0] == _detourPtr) _slot[0] = (IntPtr)_next;
            Ready = false;
            CrashGuard.Clear("net");
        }

        private static IntPtr MethodInfoPtr(Type type, string name)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
                         ?? throw new MissingMethodException(type.Name, name);
            var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method)
                        ?? throw new MissingFieldException(type.Name, "MethodInfo for " + name);
            var ptr = (IntPtr)field.GetValue(null);
            if (ptr == IntPtr.Zero) throw new InvalidOperationException("null MethodInfo for " + name);
            return ptr;
        }

        // ------------------------------------------------------------------ receive

        [sysinterop::System.Runtime.InteropServices.UnmanagedCallersOnly]
        private static void Detour(IntPtr self, IntPtr runner, int player, RawKey* key, RawSpan* data, IntPtr method)
        {
            // Chaining convention shared by independent mods (no mod depends on another):
            // handle our own key; if the next handler is another mod's, pass everything
            // else on; if it is the game's own, pass on only the game's keys (1, 2). The
            // game throws on any other key, and an exception crossing this native
            // callback kills the process.
            if (key == null) return;
            if (key->A != KeyMagic)
            {
                if (_chained || key->A == 1 || key->A == 2)
                    _next(self, runner, player, key, data, method);
                return;
            }
            if (key->B != Protocol) return;
            try
            {
                if (data == null || data->Ptr == IntPtr.Zero) return;
                int len = data->Length;
                if (len <= 1 || len > MaxPacket) return;
                var bytes = new byte[len];
                Marshal.Copy(data->Ptr, bytes, 0, len);
                // Handled later on the main thread, outside this native callback.
                lock (Inbox) { if (Inbox.Count < 256) Inbox.Enqueue((player, bytes)); }
            }
            catch (Exception ex)
            {
                Mod.Log.Warning("Bad yeti packet: " + ex.Message);
            }
        }

        /// <summary>Handles queued packets. Called every frame from the main thread.</summary>
        public static void ProcessInbox()
        {
            while (true)
            {
                (int from, byte[] data) msg;
                lock (Inbox) { if (Inbox.Count == 0) return; msg = Inbox.Dequeue(); }
                try { Receive(msg.from, msg.data); }
                catch (Exception ex) { Mod.Log.Warning("Bad yeti packet: " + ex.Message); }
            }
        }

        // Players we've heard from (i.e. who have the mod). Vanilla clients throw (and
        // log) on unknown reliable-data keys, so apart from the initial Hello we only
        // send to these. On a client, the only entry is the host.
        private static readonly HashSet<int> Peers = new();

        public static void ClearPeers() => Peers.Clear();

        private static void Receive(int sender, byte[] bytes)
        {
            var run = Runner();
            if (run == null) return;
            if (sender > 0) Peers.Add(sender);

            // The host forwards client messages to every other modded player.
            if (run.IsServer && !IsShared(run))
                foreach (var p in OtherPlayers(run))
                    if (p != sender && Peers.Contains(p)) SendRaw(run, p, bytes);

            using var r = new BinaryReader(new MemoryStream(bytes));
            var type = (Msg)r.ReadByte();
            OnMessage?.Invoke(type, sender, r);
        }

        // --------------------------------------------------------------------- send

        public static BinaryWriter Begin(Msg type, out MemoryStream ms)
        {
            ms = new MemoryStream(64);
            var w = new BinaryWriter(ms);
            w.Write((byte)type);
            return w;
        }

        /// <summary>Sends to everyone else in the session who has the mod.</summary>
        public static void Broadcast(MemoryStream ms)
        {
            var run = Runner();
            if (run == null || run.IsSinglePlayer) return;
            var bytes = ms.ToArray();
            bool hello = bytes.Length > 0 && bytes[0] == (byte)Msg.Hello;
            if (run.IsServer || IsShared(run))
            {
                foreach (var p in OtherPlayers(run))
                    if (hello || Peers.Contains(p)) SendRaw(run, p, bytes);
            }
            else if (hello || Peers.Count > 0)
                SendRaw(run, -1, bytes);
        }

        /// <summary>Sends to one player (host: directly; client: via the host).</summary>
        public static void SendTo(int player, MemoryStream ms)
        {
            var run = Runner();
            if (run == null || run.IsSinglePlayer) return;
            SendRaw(run, run.IsServer || IsShared(run) ? player : -1, ms.ToArray());
        }

        private static void SendRaw(NetworkRunner run, int player, byte[] bytes)
        {
            if (!Ready || bytes.Length > MaxPacket) return;
            var key = new RawKey { A = KeyMagic, B = Protocol };
            fixed (byte* p = bytes)
            {
                var span = new RawSpan { Ptr = (IntPtr)p, Length = bytes.Length };
                IntPtr exc = IntPtr.Zero;
                if (player < 0)
                {
                    void** args = stackalloc void*[2];
                    args[0] = &key;
                    args[1] = &span;
                    IL2CPP.il2cpp_runtime_invoke(_sendToServer, run.Pointer, args, ref exc);
                }
                else
                {
                    var pref = player;
                    void** args = stackalloc void*[3];
                    args[0] = &pref;
                    args[1] = &key;
                    args[2] = &span;
                    IL2CPP.il2cpp_runtime_invoke(_sendToPlayer, run.Pointer, args, ref exc);
                }
                if (exc != IntPtr.Zero)
                    Mod.Log.Warning($"Yeti packet to {(player < 0 ? "host" : "player " + player)} failed");
            }
        }

        // -------------------------------------------------------------------- state

        public static NetworkRunner Runner()
        {
            try
            {
                var onr = OverlandNetworkRunner.Instance;
                var run = onr != null ? onr.NetworkRunner : null;
                return run != null && run.IsRunning ? run : null;
            }
            catch { return null; }
        }

        public static bool IsShared(NetworkRunner run) => run.Topology.ToString() == "Shared";

        public static int LocalPlayer(NetworkRunner run) => run?.LocalPlayer.Raw ?? 0;

        /// <summary>True for the peer that answers Hello (host, or shared master client).</summary>
        public static bool IsAuthority(NetworkRunner run) =>
            run == null || run.IsSinglePlayer || run.IsServer || (IsShared(run) && run.IsSharedModeMasterClient);

        public static List<int> OtherPlayers(NetworkRunner run)
        {
            var list = new List<int>();
            try
            {
                int me = run.LocalPlayer.Raw;
                var en = run.ActivePlayers.GetEnumerator();
                var it = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (it.MoveNext())
                {
                    int raw = en.Current.Raw;
                    if (raw != me && raw > 0) list.Add(raw);
                }
            }
            catch (Exception ex)
            {
                Mod.Log.Warning("Could not list players: " + ex.Message);
            }
            return list;
        }

        public static string Describe()
        {
            var run = Runner();
            if (!Ready) return Status;
            if (run == null) return "offline";
            if (run.IsSinglePlayer) return "single player";
            string role = IsShared(run) ? "shared" : run.IsServer ? "host" : "client";
            return $"{role}, {OtherPlayers(run).Count} other player(s), {Yetis.ModdedPeers} with this mod";
        }
    }
}
