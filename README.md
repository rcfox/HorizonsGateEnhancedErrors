# EnhancedErrors

A drop-in mod for **Horizon's Gate** that makes the game's crash messages useful when debugging mods.

Most mod crashes are a typo in an ID, which the game reports as:

```
The given key was not present in the dictionary.
```

With EnhancedErrors installed, the same crash says what was missing, what kind of thing it was, and what you probably meant:

```
The given key "fireSowrd" was not present in the dictionary (Dictionary<string, ItemType>). Did you mean "fireSword"?
```

It also keeps the original stack trace for errors raised in C# code called from Lua scripts, which the game's Lua library otherwise throws away.

Players install it by copying two files into the game folder. No tools, launchers or mod loaders are needed. See [Package/README.txt](Package/README.txt) for the player-facing instructions that ship in the release zip.

## How it works

**Loading.** The game is a .NET Framework 4.x app (FNA, not Unity). Its `BoatTactics.exe.config` names an `appDomainManagerAssembly`/`appDomainManagerType`, so the CLR loads `EnhancedErrors.dll` and constructs `EnhancedErrors.Manager` before the game's `Main` runs ([Manager.cs](Manager.cs)). Harmony is embedded in the DLL as a resource and loaded through `AssemblyResolve`, so the mod is a single file.

**Missing keys.** [Patcher.cs](Patcher.cs) uses Harmony transpilers to rewrite every `Dictionary<K,V>[key]` read in `BoatTactics.exe` (about 27,000 of them in 859 methods) into a call to `KeyLookup.Get`, which does a `TryGetValue` and throws a more detailed error on failure ([KeyLookup.cs](KeyLookup.cs)). The call sites are rewritten instead of `Dictionary` itself because Harmony can't patch mscorlib's `Dictionary.get_Item` on .NET Framework ("The given generic instantiation was invalid"). The JIT would also inline it into many callers.

The exception is `MissingKeyException`, a subclass of `KeyNotFoundException`, so existing `catch` blocks still work. Its "did you mean" suggestions (Levenshtein distance ≤ 3, up to 3 matches, string keys only) are computed only when `Message` is read, so code that catches the exception and carries on doesn't pay for them.

**Lua stack traces.** When a C# function called from Lua throws, KopiLuaInterface carries the exception through Lua, and `Lua.ThrowExceptionFromError` rethrows it with `throw e`, which replaces the stack trace. [LuaPatch.cs](LuaPatch.cs) inserts a call to `Exception.InternalPreserveStackTrace` before that `throw`, so the popup shows the original frames down to the failing lookup.

Each launch writes `EnhancedErrors.log` to the game folder with what was patched and how long it took.

## Limitations

- **Startup cost.** Patching adds about 6.5 seconds to startup, almost all of it JIT-compiling the patched copies of the game's very large data-loading methods.
- **Generic methods aren't patched.** Harmony can't patch generic method instantiations on .NET Framework. This skips 85 lookups in `DataManager` and `ProceduralEditorUI.setupEditor`. The `DataManager` ones can't throw (they loop over the dictionary's own keys or check `ContainsKey` first). The `setupEditor` ones use fixed UI element names in the map editor.
- **Only the game's own code is patched**, not other DLLs such as KopiLuaInterface. When a Lua script indexes a .NET dictionary directly, KopiLuaInterface already reports the key (`key '…' not found`).
- **Some stack frames can be missing.** Patched methods show up in stack traces with a `_Patch1` suffix, and every trace starts with an `EnhancedErrors.KeyLookup.Get` frame. A method that ends by returning another call's result can be optimised into a tail call and disappear from the trace.
- **Steam can turn it off.** "Verify integrity of game files", or a game update that touches the config, restores the original `BoatTactics.exe.config` and disables the mod.

## Building

Requires the .NET SDK (any recent version; the project targets `net48` through the reference-assembly package).

```bash
dotnet build -c Release
```

This produces:

- `bin/Release/net48/EnhancedErrors.dll`
- `dist/EnhancedErrors-<version>.zip`, the release package, containing:
  - `EnhancedErrors.dll`
  - a `BoatTactics.exe.config` generated from [the template](Package/BoatTactics.exe.config.template) with the current assembly version
  - `BoatTactics.exe.config.original`, the game's unmodified config, for uninstalling
  - the player README

## Releasing

To release a new version, bump `Version` and `AssemblyVersion` in [EnhancedErrors.csproj](EnhancedErrors.csproj) and push to `main`. The packaged config always references the matching version.

The [release workflow](.github/workflows/release.yml) builds every push to `main` and uploads the zip as a workflow artifact. If no `v<Version>` tag exists yet, it also creates that tag and a GitHub release with the zip attached and auto-generated notes. Pushes that don't change the version are built but not released. The workflow can also be run manually from the Actions tab.

To install a build into a local copy of the game, extract `EnhancedErrors.dll` and `BoatTactics.exe.config` from the zip into the game folder.
