extern alias sysinterop;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppFusion;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace WombatGD
{
    /// <summary>
    /// Shares each player's Wombat profile (snorkel side, engine sound, smoke
    /// settings, smoke-screen key) with other players who run the mod, over the
    /// game's own Photon Fusion session using "reliable data" on a key the game
    /// never uses.
    ///
    /// Receiving: an earlier version Harmony-patched NetworkRunner's reliable-data
    /// entry point. Il2CppInterop's trampoline mangled the by-value ReliableKey and
    /// ReadOnlySpan arguments, so the game's own join handshake arrived with a bad
    /// key and the map load hung. Now, like AbominableSnowman, we swap
    /// OverlandNetworkRunner's vtable entry for OnReliableDataReceived with a native
    /// [UnmanagedCallersOnly] function that receives the raw arguments and passes
    /// other keys on unchanged (see the chaining convention in Detour). The hook
    /// is installed a few seconds after the local player has spawned.
    ///
    /// Sending: unmodded players' handlers throw (Fusion catches and logs) on
    /// unknown keys, so each player gets one hello and after that only players we
    /// have heard from receive anything.
    ///
    /// Routing: in Shared mode everyone sends to everyone. In host/client mode
    /// clients send to the host, which relays to the other modded players (so the
    /// host must have the mod for sync to work).
    /// </summary>
    internal static unsafe class NetSync
    {
        private const int KeyMagic = 0x57474744; // "WGGD"
        private const float NetWatchSeconds = 90f; // crash-guard window after hooking
        private const int Protocol = 1;
        private const int MaxPacket = 512;
        private const float Heartbeat = 4f;      // resend so late joiners catch up
        private const float MinInterval = 0.15f;  // limit while dragging sliders
        private const float HookDelay = 3f;       // seconds after spawn before hooking

        [StructLayout(LayoutKind.Sequential)]
        private struct RawKey { public int A, B, C, D; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawSpan { public IntPtr Ptr; public int Length; }

        // Native signature of OverlandNetworkRunner.OnReliableDataReceived (IL2CPP, x64):
        // (this, runner, PlayerRef player, ReliableKey* key, ReadOnlySpan<byte>* data, MethodInfo*)
        private static delegate* unmanaged<IntPtr, IntPtr, int, RawKey*, RawSpan*, IntPtr, void> _next;
        private static IntPtr _sendToServer, _sendToPlayer, _recvInfo;
        private static IntPtr* _slot;
        private static IntPtr _detourPtr;
        private static bool _chained; // another mod's hook sits between us and the game
        private static bool _resolved;
        private static float _spawnedAt = -1f, _hookedAt = -1f;

        private static readonly Dictionary<IntPtr, uint> IdCache = new();
        private static readonly Dictionary<uint, CarAudioManager> Wombats = new();
        private static readonly HashSet<int> Peers = new();     // players known to have the mod
        private static readonly HashSet<int> Greeted = new();   // players we've sent a hello to
        private static readonly HashSet<uint> _remoteSeen = new();
        private static readonly Queue<(int from, byte[] data)> Inbox = new();
        private static byte[] _lastSent;
        private static float _lastSendTime = -100f;
        private static bool _loggedMode;

        public static bool Ready { get; private set; }
        public static string Status { get; private set; } = "waiting for the game to load";
        public static int RemoteCount => _remoteSeen.Count;

        /// <summary>The truck's Fusion network id (0 if unknown).</summary>
        public static uint VehicleId(CarAudioManager cam)
        {
            if (cam == null) return 0;
            if (IdCache.TryGetValue(cam.Pointer, out var id) && id != 0) return id;
            id = 0;
            try
            {
                var no = cam.transform.root.GetComponent<NetworkObject>();
                if (no != null) id = no.Id.Raw;
            }
            catch (Exception ex) { WombatGDMod.Log.Warning("VehicleId: " + ex.Message); }
            IdCache[cam.Pointer] = id;
            return id;
        }

        public static void Track(CarAudioManager cam)
        {
            uint id = VehicleId(cam);
            if (id != 0) Wombats[id] = cam;
        }

        public static void Forget(CarAudioManager cam)
        {
            if (IdCache.TryGetValue(cam.Pointer, out var id)) Wombats.Remove(id);
            IdCache.Remove(cam.Pointer);
        }

        /// <summary>Resolves method handles. Nothing is hooked here; see <see cref="EnsureHooked"/>.</summary>
        public static void Init()
        {
            try
            {
                _sendToServer = MethodInfoPtr(typeof(NetworkRunner), nameof(NetworkRunner.SendReliableDataToServer));
                _sendToPlayer = MethodInfoPtr(typeof(NetworkRunner), nameof(NetworkRunner.SendReliableDataToPlayer));
                _recvInfo = MethodInfoPtr(typeof(OverlandNetworkRunner), nameof(OverlandNetworkRunner.OnReliableDataReceived));
                _resolved = true;
            }
            catch (Exception ex)
            {
                Status = "unavailable";
                WombatGDMod.Log.Warning("Multiplayer sync disabled (could not resolve Fusion methods): " + ex.Message);
            }
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

        private static void EnsureHooked()
        {
            if (Ready)
            {
                if (_hookedAt >= 0f && Time.unscaledTime - _hookedAt > NetWatchSeconds)
                {
                    CrashGuard.Clear("net");
                    _hookedAt = -1f;
                }
                return;
            }
            if (!_resolved) return;
            if (!Settings.MultiplayerSync.Value) { Status = "off"; return; }
            if (CrashGuard.IsDisabled("net")) { _resolved = false; Status = "disabled after a crash"; return; }
            bool spawned;
            try { spawned = OverlandNetworkRunner.IsLocalPlayerSpawned; }
            catch { return; }
            if (!spawned) { _spawnedAt = -1f; return; }
            if (_spawnedAt < 0f) _spawnedAt = Time.unscaledTime;

            // A short wait after spawning keeps the game's join handshake well clear
            // of mod code and lets mods that insist on hooking first do so.
            if (Time.unscaledTime - _spawnedAt < HookDelay) return;
            IntPtr original = *(IntPtr*)_recvInfo;

            _resolved = false; // one attempt only
            try
            {
                var klass = Il2CppClassPointerStore<OverlandNetworkRunner>.NativeClassPtr;
                if (klass == IntPtr.Zero) throw new InvalidOperationException("null class");

                // Il2CppClass ends with VirtualInvokeData vtable[] = { methodPtr, MethodInfo* }.
                // Match on the MethodInfo only: another mod may already have replaced methodPtr.
                int found = 0;
                byte* p = (byte*)klass;
                for (int off = 0x40; off < 0x4000; off += 8)
                {
                    var e = (IntPtr*)(p + off);
                    if (e[1] == _recvInfo && e[0] != IntPtr.Zero) { _slot = e; found++; }
                }
                if (found != 1) throw new InvalidOperationException($"found {found} vtable entries");

                CrashGuard.Enter("net"); // stays marked for NetWatchSeconds
                _hookedAt = Time.unscaledTime;
                _next = (delegate* unmanaged<IntPtr, IntPtr, int, RawKey*, RawSpan*, IntPtr, void>)_slot[0];
                delegate* unmanaged<IntPtr, IntPtr, int, RawKey*, RawSpan*, IntPtr, void> detour = &Detour;
                _detourPtr = (IntPtr)detour;
                _chained = (IntPtr)_next != original;
                _slot[0] = _detourPtr;
                Ready = true;
                Status = "ready";
                WombatGDMod.Log.Msg(_chained ? "Multiplayer sync ready (chained after another mod's hook)."
                                             : "Multiplayer sync ready.");
            }
            catch (Exception ex)
            {
                Status = "unavailable";
                WombatGDMod.Log.Warning("Multiplayer sync disabled (everything still works locally): " + ex.Message);
            }
        }

        /// <summary>Restores the previous handler (on quit), if nobody has chained on top of us.</summary>
        public static void Unhook()
        {
            if (_slot != null && _slot[0] == _detourPtr) _slot[0] = (IntPtr)_next;
            Ready = false;
            CrashGuard.Clear("net");
        }

        // ------------------------------------------------------------------ receive

        [sysinterop::System.Runtime.InteropServices.UnmanagedCallersOnly]
        private static void Detour(IntPtr self, IntPtr runner, int player, RawKey* key, RawSpan* data, IntPtr method)
        {
            // Chaining convention (no mod depends on another): handle our own key; if
            // the next handler is another mod's, pass everything else on; if it is the
            // game's own, pass on only the game's keys (1, 2). The game throws on any
            // other key, and an exception crossing a native callback like this one
            // kills the process instantly.
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
                if (len <= 0 || len > MaxPacket) return;
                var bytes = new byte[len];
                Marshal.Copy(data->Ptr, bytes, 0, len);
                lock (Inbox) { if (Inbox.Count < 64) Inbox.Enqueue((player, bytes)); }
            }
            catch (Exception ex) { WombatGDMod.Log.Warning("Bad profile packet: " + ex.Message); }
        }

        private static void ProcessInbox(NetworkRunner run)
        {
            while (true)
            {
                (int from, byte[] data) msg;
                lock (Inbox) { if (Inbox.Count == 0) return; msg = Inbox.Dequeue(); }
                Receive(run, msg.from, msg.data);
            }
        }

        private static void Receive(NetworkRunner run, int from, byte[] data)
        {
            var p = Profile.Deserialize(data, out uint vehicleId);
            if (p == null) { WombatGDMod.Log.Warning("Ignored malformed WombatGD profile."); return; }

            if (Peers.Add(from > 0 ? from : -1)) _lastSendTime = -100f; // new modded peer: answer promptly

            // Host in host/client mode: pass it on to the other modded players.
            if (run.IsServer && !IsShared(run))
                foreach (var pl in OtherPlayers(run))
                    if (pl != from && Peers.Contains(pl)) SendRaw(run, pl, data);

            if (Wombats.TryGetValue(vehicleId, out var cam) && cam != null && CoalSmoke.IsLocalPlayer(cam)) return;
            bool first = _remoteSeen.Add(vehicleId);
            bool visualChange = Profile.SetRemote(vehicleId, p);
            if (first) WombatGDMod.Log.Msg($"Received WombatGD profile for truck {vehicleId}.");
            if (visualChange && cam != null && !cam.WasCollected) WombatGDMod.ApplyProfile(cam);
        }

        // --------------------------------------------------------------------- send

        /// <summary>Called every frame: hooks when safe, handles packets, broadcasts our profile.</summary>
        public static void Update(CarAudioManager localWombat)
        {
            EnsureHooked();
            if (!Ready) return;
            var run = Runner();
            if (run == null || run.IsSinglePlayer)
            {
                Status = "single player";
                Peers.Clear(); Greeted.Clear(); _loggedMode = false;
                return;
            }
            try
            {
                LogModeOnce(run);
                ProcessInbox(run);
                if (localWombat == null || localWombat.WasCollected) { Status = "no Wombat"; return; }

                float now = Time.unscaledTime;
                if (now - _lastSendTime < MinInterval) return;
                var data = Profile.Local.Serialize(VehicleId(localWombat));
                bool changed = _lastSent == null || !Same(data, _lastSent);
                bool newPlayers = HasUngreeted(run);
                if (!changed && !newPlayers && now - _lastSendTime < Heartbeat) return;

                Broadcast(run, data);
                _lastSent = data;
                _lastSendTime = now;
                Status = $"{Role(run)}, {Peers.Count} modded player(s), {_remoteSeen.Count} truck(s) synced";
            }
            catch (Exception ex)
            {
                Status = "error";
                WombatGDMod.Log.Warning("Profile send failed: " + ex.Message);
                _lastSendTime = Time.unscaledTime + 5f; // back off
            }
        }

        private static bool HasUngreeted(NetworkRunner run)
        {
            if (run.IsServer || IsShared(run))
            {
                foreach (var p in OtherPlayers(run)) if (!Greeted.Contains(p)) return true;
                return false;
            }
            return !Greeted.Contains(-1);
        }

        /// <summary>Sends to modded players, plus a one-time hello to anyone not yet greeted.</summary>
        private static void Broadcast(NetworkRunner run, byte[] data)
        {
            if (run.IsServer || IsShared(run))
            {
                foreach (var p in OtherPlayers(run))
                    if (Peers.Contains(p) || Greeted.Add(p)) SendRaw(run, p, data);
            }
            else if (Peers.Count > 0 || Greeted.Add(-1))
                SendRaw(run, -1, data); // client: to the host, which relays
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
                    int pref = player;
                    void** args = stackalloc void*[3];
                    args[0] = &pref;
                    args[1] = &key;
                    args[2] = &span;
                    IL2CPP.il2cpp_runtime_invoke(_sendToPlayer, run.Pointer, args, ref exc);
                }
                if (exc != IntPtr.Zero)
                    WombatGDMod.Log.Warning($"Profile packet to {(player < 0 ? "host" : "player " + player)} failed");
            }
        }

        // -------------------------------------------------------------------- state

        private static NetworkRunner Runner()
        {
            try
            {
                var onr = OverlandNetworkRunner.Instance;
                var run = onr != null ? onr.NetworkRunner : null;
                return run != null && run.IsRunning ? run : null;
            }
            catch { return null; }
        }

        private static bool IsShared(NetworkRunner run) => run.Topology.ToString() == "Shared";

        private static string Role(NetworkRunner run) => IsShared(run) ? "shared" : run.IsServer ? "host" : "client";

        private static List<int> OtherPlayers(NetworkRunner run)
        {
            var list = new List<int>();
            int me = run.LocalPlayer.Raw;
            var en = run.ActivePlayers.GetEnumerator();
            var it = en.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (it.MoveNext())
            {
                int raw = en.Current.Raw;
                if (raw != me && raw > 0) list.Add(raw);
            }
            return list;
        }

        private static void LogModeOnce(NetworkRunner run)
        {
            if (_loggedMode) return;
            _loggedMode = true;
            WombatGDMod.Log.Msg($"Multiplayer session: GameMode={run.GameMode} role={Role(run)} topology={run.Topology}");
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
