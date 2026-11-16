# Modding Unity IL2CPP games with MelonLoader

Layer: **generic**. Lessons here were learned on specific games (see the game layer for the
original incidents) but apply to any Unity IL2CPP game modded with MelonLoader 0.7.x,
Il2CppInterop and Harmony. Add to this file when a lesson isn't specific to one game.

---

## 1. How to approach a new mod

### Step 1: Find the game code you need

IL2CPP games ship native code, so there is no C# to read directly. MelonLoader generates
interop assemblies (`<game>/MelonLoader/Il2CppAssemblies/`) with **signatures only**.

```bash
# list types/members matching a pattern
.venv/bin/python tools/dump_types.py "<game>/MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll" 'Car|Audio'
# full signatures of one type
~/.dotnet/tools/ilspycmd -t Il2Cpp.SomeType "<game>/MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll"
```

Each game layer has a `regen-decomp.sh` that writes these into `.game/` (git-ignored, because the
game's code is the developer's copyright and must never be committed).

### Step 2: Check that the Unity API exists in *this* build

Unity strips modules and members the game doesn't use. A stripped API compiles fine against
the interop DLLs **and fails at runtime**. Before designing around an API, find it in
`Il2CppAssemblies/UnityEngine.*Module.dll`. Typical casualties: ParticleSystem module
setters (noise, size/colour over lifetime, texture sheet), image decoding.

### Step 3: Start from the skeleton

`tools/new-mod.sh <Name>` creates `<Name>/` (Mod, Settings, CrashGuard, Core/) and
`tests/<Name>.Tests/`. Add only the `Il2CppAssemblies` references you use. Mods are
**independent**: copy shared code (and keep it in sync via the template), never reference
another mod's DLL.

### Step 4: Hook the game, safest first

1. **Poll in `OnUpdate`**: find objects (cache `FindObjectsOfType` results), read state, change
   transforms. Nothing can corrupt the game.
2. **Harmony prefix/postfix on simple methods** (parameters are reference types or
   primitives): lifecycle methods like `Start`/`OnDestroy` tell you when objects appear and go.
3. **Native vtable-slot swap** (§3): only for methods taking structs by value, or that
   Harmony can't patch safely, **and that are called virtually / through an interface**.
4. **Never** code-patch trampolines (`NativeHook`, Harmony) on methods with by-value struct
   parameters, or on anything in a network handshake.

### Step 5: Visuals and audio

- **Clone stock objects** (`Object.Instantiate(stock.gameObject, parent, false)`) and change
  only what you need. Building complex components from scratch (`AddComponent<ParticleSystem>`,
  then `GetParticles`/`SetParticles`) has crashed games natively. Emitting from a cloned system
  with `Emit(EmitParams, n)` is reliable.
- Textures: write raw RGBA32 (bottom row first) and use `Texture2D.LoadRawTextureData`. This
  avoids image decoders that may be stripped.
- Audio in FMOD games: play your own sounds through the FMOD core API (`Il2CppFMOD`), and
  steer stock events through their parameters/labels.
- Find attachment points by raycasting the vehicle's own colliders (`Collider.Raycast`), with a
  hard-coded fallback.
- Rigidbodies on joints must be re-seated after you move them (toggle the GameObject).

### Step 6: Multiplayer

Ride the game's own networking with a custom message key rather than opening sockets. Sync
small, infrequent state. Vanilla peers must never receive anything they can't handle. Every
feature must still work locally when sync is unavailable. The game layer documents the
mechanism for each game.

### Step 7: Test cycle

See [testing.md](testing.md): `tools/check.sh`, then the in-game checklist, then both logs.

---

## 2. Debugging

| Symptom | Likely cause | What to do |
| --- | --- | --- |
| Process dies, no crash report | Exception crossed a native callback (`[UnmanagedCallersOnly]` detour), or native crash in engine code | Catch everything in detours; check CrashGuard markers to find the stage |
| Hang while loading / joining MP | Mod code ran inside the game's handshake, or a trampoline corrupted arguments | Delay hooks until the local player is spawned (+ a few seconds); use vtable swap |
| Mod's own log is clean but game misbehaves | Error logged only by Unity | Read Unity's `Player.log` (location under Proton: see game layer) |
| Compiles, then `MissingMethodException`/native crash on one call | API stripped from the build | Check `Il2CppAssemblies`; use another route |
| Duplicate `UnmanagedCallersOnlyAttribute` build error | `UnityEngine.CoreModule` ships its own copy | `extern alias sysinterop;` + the `AliasInteropServices` target (in the skeleton csproj) |
| Crash blamed on subsystem A | Was actually subsystem B | Wrap each suspect in its own CrashGuard stage before changing code |

**Method:** one hypothesis per build. Instrument (log lines, CrashGuard stages) before you guess.

### CrashGuard

`CrashGuard` (in the skeleton) writes `UserData/<Mod>/in_progress_<stage>.txt` before a
risky stage runs, for its first 30 runs, and deletes it when the stage returns. A hard crash
leaves the marker. The next launch logs the stage name, disables it (`disabled.txt`) and the
menu can re-enable it. Use it around particle/physics creation, native hooks, and audio
system setup.

---

## 3. Native vtable-slot swap (intercepting a virtual method without patching code)

When the game calls a method through an interface or a virtual call, the call goes through the
class's vtable in `Il2CppClass`. Replacing one slot redirects the call without touching
machine code.

1. Get the method's `MethodInfo*`: `Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method)`.
2. Get the class pointer: `Il2CppClassPointerStore<T>.NativeClassPtr`.
3. Scan the class's `VirtualInvokeData vtable[] = { methodPtr, MethodInfo* }` entries
   (offsets 0x40–0x4000, 8-byte steps). **Match on the `MethodInfo*` only**, because another mod may
   have replaced `methodPtr` already. Require exactly one match.
4. Save `slot[0]` as `_next`, write a `delegate* unmanaged` pointer to your
   `[UnmanagedCallersOnly]` static detour. `_chained = _next != original methodPointer`.
5. Detour: struct parameters arrive as pointers (`Key*`, `{IntPtr Ptr; int Length}*` for spans).
   Copy what you need, **queue it for the main thread**, and return. **No exception may escape.**
6. Unhook on quit only if the slot still holds *your* detour.
7. Install once, late (after the game has finished loading/joining), inside a CrashGuard stage.

Calling the game's methods with struct arguments: `IL2CPP.il2cpp_runtime_invoke(methodInfo,
obj.Pointer, args, ref exc)`, with `args` as pointers to raw structs.

**Chaining convention** (independent mods sharing one slot, any load order): handle your own
messages. If `_chained`, forward everything else to `_next`. If you sit directly on the game's
handler, forward only what the game itself handles (the game layer lists it) and drop the rest.

---

## 4. Proton / Linux specifics

- MelonLoader works under Proton. Logs: `<game>/MelonLoader/Latest.log` (and `Logs/`), plus
  Unity's `Player.log` inside the Proton prefix
  (`compatdata/<appid>/pfx/drive_c/users/steamuser/AppData/LocalLow/<Company>/<Product>/`).
- Build on Linux with the .NET SDK (any current SDK builds `net6.0`).
- Flatpak Steam installs under `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/`.
- Code-patch trampolines were measurably less reliable under Proton than vtable swaps.

---

## 5. Assets

- Prefer generated assets (scripts in `tools/`, reproducible with a seed) to recordings.
- Recordings need a licence you've read on the source page: CC0 or CC BY (credit file shipped
  next to the asset). Freesound downloads without login are previews; record that.
- Deploy user-swappable assets to `UserData/<Mod>/` with `SkipUnchangedFiles`, never overwriting
  user files.
