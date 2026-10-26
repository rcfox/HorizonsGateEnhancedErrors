using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EnhancedErrors
{
    /// <summary>
    /// Entry point. The CLR instantiates this before the game's Main runs because
    /// BoatTactics.exe.config names it as the appDomainManagerType.
    /// </summary>
    public sealed class Manager : AppDomainManager
    {
        public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
        {
            base.InitializeNewDomain(appDomainInfo);
            try
            {
                if (AppDomain.CurrentDomain.IsDefaultAppDomain())
                    Bootstrap.Init();
            }
            catch (Exception e)
            {
                // Never stop the game from launching.
                Log.Write("Initialisation failed: " + e);
            }
        }
    }

    static class Bootstrap
    {
        static bool gamePatched, luaPatched;

        public static void Init()
        {
            Log.Write("EnhancedErrors " + typeof(Bootstrap).Assembly.GetName().Version + " loaded");
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
            AppDomain.CurrentDomain.AssemblyLoad += (s, e) => TryPatch(e.LoadedAssembly);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(asm);
        }

        static void TryPatch(Assembly asm)
        {
            var name = asm.GetName().Name;
            try
            {
                if (name == "BoatTactics" && !gamePatched)
                {
                    gamePatched = true;
                    PatchGame(asm);
                }
                else if (name == "KopiLuaInterface" && !luaPatched)
                {
                    luaPatched = true;
                    PatchLua(asm);
                }
            }
            catch (Exception e)
            {
                Log.Write("Patching " + name + " failed: " + e);
            }
        }

        // Kept separate so Harmony isn't needed until AssemblyResolve is hooked up.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PatchGame(Assembly asm) => Patcher.PatchAssembly(asm);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PatchLua(Assembly asm) => LuaPatch.Apply(asm);

        static Assembly ResolveEmbedded(object sender, ResolveEventArgs args)
        {
            if (new AssemblyName(args.Name).Name != "0Harmony")
                return null;
            using (var stream = typeof(Bootstrap).Assembly.GetManifestResourceStream("0Harmony.dll"))
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return Assembly.Load(ms.ToArray());
            }
        }
    }

    static class Log
    {
        static readonly string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EnhancedErrors.log");
        static bool started;

        public static void Write(string message)
        {
            try
            {
                var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine;
                if (started)
                    File.AppendAllText(path, line);
                else
                    File.WriteAllText(path, line);
                started = true;
            }
            catch
            {
                // Logging is best-effort (the folder may not be writable).
            }
        }
    }
}
