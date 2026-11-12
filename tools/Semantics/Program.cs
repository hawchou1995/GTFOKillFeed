using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;

static class Program
{
    static List<string> RefDirs = new List<string>();

    static void Main(string[] a)
    {
        string asm = null, mode = null, only = null;
        int skip = 0, take = int.MaxValue;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == "--asm") asm = a[++i];
            else if (a[i] == "--ref") RefDirs.Add(a[++i]);
            else if (a[i] == "--list") mode = "list";
            else if (a[i] == "--run") { mode = "run"; only = a[++i]; }
            else if (a[i] == "--runall") mode = "runall";
            else if (a[i] == "--skip") skip = int.Parse(a[++i], CultureInfo.InvariantCulture);
            else if (a[i] == "--take") take = int.Parse(a[++i], CultureInfo.InvariantCulture);
        }
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            foreach (var d in RefDirs)
            {
                var p = Path.Combine(d, name.Name + ".dll");
                if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
            }
            return null;
        };
        var asmb = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(asm));
        if (mode == "list") { foreach (var m in Candidates(asmb)) Console.WriteLine(Id(m)); return; }
        if (mode == "runall")
        {
            int n = 0, done = 0;
            foreach (var m in Candidates(asmb))
            {
                if (n++ < skip) continue;
                if (done++ >= take) break;
                Console.WriteLine(Run(m));
            }
            return;
        }
        var target = Candidates(asmb).FirstOrDefault(m => Id(m) == only);
        if (target == null) { Console.Error.WriteLine("method not found: " + only); Environment.Exit(3); }
        Console.WriteLine(Run(target));
    }

    static string Id(MethodInfo m)
    {
        var ps = string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName));
        return m.DeclaringType.FullName + "|" + m.Name + "|" + ps;
    }

    static IEnumerable<MethodInfo> Candidates(Assembly asm)
    {
        foreach (var t in asm.GetTypes().OrderBy(x => x.FullName, StringComparer.Ordinal))
        {
            if (t.IsGenericTypeDefinition) continue;
            if (t.FullName.Contains("<") || t.FullName.Contains(">")) continue;
            if (t.GetCustomAttributesData().Any(c => c.AttributeType.Name == "CompilerGeneratedAttribute")) continue;
            if (t.FullName.Contains("Plugin")) continue;
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                               .OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                if (m.IsGenericMethodDefinition || m.IsAbstract) continue;
                if (BadName(m.Name)) continue;
                if (!SafeType(m.ReturnType, asm)) continue;
                if (!m.GetParameters().All(p => SafeType(p.ParameterType, asm))) continue;
                if (m.GetParameters().Length > 3) continue;
                yield return m;
            }
        }
    }

    static bool BadName(string n)
    {
        string[] bad = { "Patch", "Apply", "Bind", "Install", "Register", "Inject", "Save", "Load", "Write", "Read",
                         "Log", "Report", "Init", "Setup", "Dispose", "Update", "Tick", "Frame", "Draw", "Render", "Reload",
                         "Probe", "Scan", "Send", "Start", "Stop", "Create", "Destroy", "Main", "Hook" };
        foreach (var b in bad) if (n.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.StartsWith("get_") || n.StartsWith("set_") || n.StartsWith("add_") || n.StartsWith("remove_")) return true;
        return false;
    }

    static bool SafeType(Type t, Assembly asm, int depth = 0)
    {
        if (depth > 2) return false;
        if (t.IsByRef || t.IsPointer) return false;
        if (t == typeof(void)) return true;
        if (t.IsPrimitive || t == typeof(string)) return true;
        if (t.IsEnum) return t.Assembly == asm || t.Namespace == "System";
        if (t.IsArray) return t.GetArrayRank() == 1 && SafeType(t.GetElementType(), asm, depth + 1);
        var n = t.FullName ?? "";
        if (n.StartsWith("System.ValueTuple`")) return t.GetGenericArguments().All(g => SafeType(g, asm, depth + 1));
        if (n.StartsWith("System.Nullable`")) return SafeType(t.GetGenericArguments()[0], asm, depth + 1);
        return false;
    }

    static IEnumerable<object> Values(Type t)
    {
        if (t == typeof(bool)) { yield return true; yield return false; }
        else if (t == typeof(byte)) { yield return (byte)0; yield return (byte)3; yield return byte.MaxValue; }
        else if (t == typeof(sbyte)) { yield return (sbyte)-1; yield return (sbyte)5; }
        else if (t == typeof(short)) { yield return (short)0; yield return (short)-7; yield return short.MaxValue; }
        else if (t == typeof(ushort)) { yield return (ushort)0; yield return (ushort)9; }
        else if (t == typeof(int)) { yield return 0; yield return 1; yield return -1; yield return 7; yield return int.MinValue; }
        else if (t == typeof(uint)) { yield return 0u; yield return 3u; yield return uint.MaxValue; }
        else if (t == typeof(long)) { yield return 0L; yield return -5L; yield return long.MinValue; }
        else if (t == typeof(ulong)) { yield return 0UL; yield return 9UL; }
        else if (t == typeof(float)) { yield return 0f; yield return 0.5f; yield return -1.25f; yield return 2.5f; yield return float.NaN; yield return float.PositiveInfinity; }
        else if (t == typeof(double)) { yield return 0d; yield return 1.5d; yield return -2.25d; yield return double.NaN; }
        else if (t == typeof(char)) { yield return 'a'; yield return ' '; }
        else if (t == typeof(string)) { yield return null; yield return ""; yield return "a"; yield return "  x  "; yield return "结沫*2"; }
        else if (t.IsEnum) { foreach (var v in Enum.GetValues(t).Cast<object>().Take(3)) yield return v; }
        else if (t.IsArray)
        {
            yield return Array.CreateInstance(t.GetElementType(), 0);
            var one = Array.CreateInstance(t.GetElementType(), 1);
            one.SetValue(Values(t.GetElementType()).First(), 0);
            yield return one;
        }
        else if ((t.FullName ?? "").StartsWith("System.Nullable`")) { yield return null; yield return Values(t.GetGenericArguments()[0]).First(); }
        else if ((t.FullName ?? "").StartsWith("System.ValueTuple`"))
        {
            var gs = t.GetGenericArguments();
            var args = gs.Select(g => Values(g).First()).ToArray();
            yield return Activator.CreateInstance(t, args);
        }
        else yield return null;
    }

    static string Render(object v, Type t)
    {
        if (v == null) return "null";
        if (t == typeof(float)) return "f32:" + BitConverter.SingleToInt32Bits((float)v).ToString("X8", CultureInfo.InvariantCulture);
        if (t == typeof(double)) return "f64:" + BitConverter.DoubleToInt64Bits((double)v).ToString("X16", CultureInfo.InvariantCulture);
        if (t == typeof(string)) return "str:" + (string)v;
        if (t == typeof(bool)) return "b:" + ((bool)v ? "1" : "0");
        if (t.IsEnum) return "e:" + Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        if (t == typeof(char)) return "c:" + ((int)(char)v).ToString(CultureInfo.InvariantCulture);
        if (t.IsPrimitive) return "p:" + Convert.ToString(v, CultureInfo.InvariantCulture);
        if (t.IsArray) return "arr[" + string.Join(";", ((Array)v).Cast<object>().Select(x => Render(x, t.GetElementType()))) + "]";
        return "obj:" + v.ToString();
    }

    static object[][] Combos(MethodInfo m, out List<string> labels)
    {
        var ps = m.GetParameters();
        var vals = ps.Select(p => Values(p.ParameterType).Take(6).ToList()).ToList();
        labels = new List<string>();
        if (ps.Length == 0) { labels.Add(""); return new[] { new object[0] }; }
        var combos = new List<object[]>();
        var idx = new int[ps.Length];
        int guard = 0;
        while (guard++ < 24)
        {
            var args = new object[ps.Length];
            var lab = new StringBuilder();
            for (int i = 0; i < ps.Length; i++)
            {
                var list = vals[i];
                args[i] = list[idx[i] % list.Count];
                lab.Append(Render(args[i], ps[i].ParameterType)).Append("|");
            }
            combos.Add(args);
            labels.Add(lab.ToString());
            int k = ps.Length - 1;
            bool carry = true;
            while (k >= 0 && carry)
            {
                idx[k]++;
                if (idx[k] % vals[k].Count != 0) carry = false;
                else idx[k] = 0;
                k--;
            }
            if (carry) break;
        }
        return combos.ToArray();
    }

    static string Run(MethodInfo m)
    {
        var combos = Combos(m, out var labels);
        var results = new List<string>();
        foreach (var args in combos)
        {
            string r;
            try
            {
                var v = m.Invoke(null, args);
                r = m.ReturnType == typeof(void) ? "void" : Render(v, m.ReturnType);
            }
            catch (TargetInvocationException ex) { r = "throw:" + (ex.InnerException == null ? "unknown" : ex.InnerException.GetType().FullName); }
            catch (Exception ex) { r = "throw:" + ex.GetType().FullName; }
            results.Add(r);
        }
        var d = new Dictionary<string, object> { { "id", Id(m) }, { "inputs", labels }, { "results", results } };
        return JsonSerializer.Serialize(d);
    }
}
