using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;

namespace EnhancedErrors
{
    /// <summary>
    /// Rewrites every <c>Dictionary&lt;K,V&gt;[key]</c> read in the game's own code into a call to
    /// <see cref="KeyLookup.Get{TKey,TValue}"/>, which throws a KeyNotFoundException naming the key.
    /// </summary>
    /// <remarks>
    /// The call sites are patched rather than Dictionary itself: on .NET Framework the indexer
    /// lives in pre-compiled mscorlib, is shared between all reference-type instantiations and is
    /// often inlined into callers, so a detour on it would miss many lookups.
    /// </remarks>
    public static class Patcher
    {
        static readonly MethodInfo lookup = typeof(KeyLookup).GetMethod(nameof(KeyLookup.Get));

        const BindingFlags AllDeclared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        static int sites;

        public static void PatchAssembly(Assembly asm)
        {
            var timer = Stopwatch.StartNew();
            var harmony = new Harmony("enhancederrors");
            var transpiler = new HarmonyMethod(typeof(Patcher).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static));
            int methods = 0, failed = 0;
            sites = 0;

            var targets = CandidateMethods(asm).AsParallel().Where(ContainsIndexerCall).ToList();
            var scanned = timer.ElapsedMilliseconds;

            // Harmony JIT-compiles each original before detouring it, one at a time under a lock.
            // Doing that up front on all cores roughly halves the startup cost.
            Parallel.ForEach(targets, method =>
            {
                try { RuntimeHelpers.PrepareMethod(method.MethodHandle); }
                catch { }
            });
            var prepared = timer.ElapsedMilliseconds;

            foreach (var method in targets)
            {
                try
                {
                    harmony.Patch(method, transpiler: transpiler);
                    methods++;
                }
                catch (Exception e)
                {
                    failed++;
                    Log.Write("Could not patch " + method.DeclaringType + "." + method.Name + ": " + e.Message);
                }
            }

            Log.Write($"Patched {sites} dictionary lookups in {methods} methods of {asm.GetName().Name} ({failed} failed) in {timer.ElapsedMilliseconds} ms"
                + $" (scan {scanned} ms, pre-JIT {prepared - scanned} ms)");
        }

        static IEnumerable<MethodBase> CandidateMethods(Assembly asm)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }

            foreach (var type in types)
            {
                // Harmony can't patch open generic types; there are only a handful of lookups in them.
                if (type.ContainsGenericParameters)
                    continue;
                var members = type.GetMethods(AllDeclared).Cast<MethodBase>()
                    .Concat(type.GetConstructors(AllDeclared).Where(c => !c.IsStatic));
                foreach (var method in members)
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null)
                        continue;
                    yield return method;
                }
            }
        }

        static bool ContainsIndexerCall(MethodBase method)
        {
            try
            {
                return PatchProcessor.ReadMethodBody(method).Any(op => IsIndexerCall(op.Key, op.Value, out _));
            }
            catch (Exception e)
            {
                Log.Write("Could not read " + method.DeclaringType + "." + method.Name + ": " + e.Message);
                return false;
            }
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ins in instructions)
            {
                if (IsIndexerCall(ins.opcode, ins.operand, out var dictType))
                {
                    // Same stack shape: (dictionary, key) -> value. Labels and blocks stay on the instruction.
                    ins.opcode = OpCodes.Call;
                    ins.operand = lookup.MakeGenericMethod(dictType.GetGenericArguments());
                    sites++;
                }
                yield return ins;
            }
        }

        static bool IsIndexerCall(OpCode opcode, object operand, out Type dictType)
        {
            dictType = null;
            if (opcode != OpCodes.Call && opcode != OpCodes.Callvirt)
                return false;
            if (!(operand is MethodInfo method) || method.Name != "get_Item")
                return false;
            var type = method.DeclaringType;
            if (type == null || !type.IsGenericType || type.ContainsGenericParameters
                || type.GetGenericTypeDefinition() != typeof(Dictionary<,>))
                return false;
            dictType = type;
            return true;
        }
    }
}
