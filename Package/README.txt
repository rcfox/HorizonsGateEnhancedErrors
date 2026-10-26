EnhancedErrors for Horizon's Gate
=================================
Version @VERSION@

When the game crashes because something wasn't found (usually a typo in a mod's
IDs), the error normally just says:

    The given key was not present in the dictionary.

With EnhancedErrors it names the missing key and suggests close matches:

    The given key "fireSowrd" was not present in the dictionary
    (Dictionary<string, ItemType>). Did you mean "fireSword"?

Errors from Lua scripts also keep their full stack trace, so you can see which
function failed.


INSTALL
-------
1. Open the game folder (in Steam: right-click Horizon's Gate > Manage >
   Browse local files).
2. Copy EnhancedErrors.dll and BoatTactics.exe.config into it, replacing the
   existing BoatTactics.exe.config.

The game takes a few extra seconds to start while the fix is applied. Each
launch writes EnhancedErrors.log to the game folder.


UNINSTALL
---------
Delete EnhancedErrors.dll, then either rename BoatTactics.exe.config.original to
BoatTactics.exe.config, or use Steam's "Verify integrity of game files".

Verifying files or a game update can also restore the original config and turn
the fix off; copy BoatTactics.exe.config in again to turn it back on.
