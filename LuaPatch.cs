using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace EnhancedErrors
{
    /// <summary>
    /// Keeps the original stack trace of .NET exceptions that pass through Lua.
    /// </summary>
    /// <remarks>
    /// When a C# function called from a script throws, LuaInterface carries the exception through
    /// Lua and Lua.ThrowExceptionFromError rethrows the same object with <c>throw e</c>, which on
    /// .NET Framework replaces its stack trace. Calling Exception.InternalPreserveStackTrace first
    /// (what remoting uses) makes the rethrow append to the original trace instead.
    /// </remarks>
    public static class LuaPatch
    {
        static readonly MethodInfo preserveStackTrace =
            typeof(Exception).GetMethod("InternalPreserveStackTrace", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Apply(Assembly asm)
        {
            var target = asm.GetType("LuaInterface.Lua")?.GetMethod("ThrowExceptionFromError", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (target == null || preserveStackTrace == null)
            {
                Log.Write("Lua patch skipped: " + (target == null ? "Lua.ThrowExceptionFromError" : "Exception.InternalPreserveStackTrace") + " not found");
                return;
            }
            new Harmony("enhancederrors.lua").Patch(target,
                transpiler: new HarmonyMethod(typeof(LuaPatch).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static)));
            Log.Write("Patched " + asm.GetName().Name + " to keep stack traces of exceptions thrown through Lua");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ins in instructions)
            {
                if (ins.opcode == OpCodes.Throw)
                {
                    // (exception) -> (exception), with its existing trace kept. Labels stay on the call.
                    var call = new CodeInstruction(OpCodes.Call, typeof(LuaPatch).GetMethod(nameof(Preserve)));
                    call.MoveLabelsFrom(ins);
                    call.MoveBlocksFrom(ins);
                    yield return call;
                }
                yield return ins;
            }
        }

        public static Exception Preserve(Exception e)
        {
            // Exceptions LuaInterface creates from Lua error strings have never been thrown; leave those alone.
            if (e != null && e.StackTrace != null)
                preserveStackTrace.Invoke(e, null);
            return e;
        }
    }
}
