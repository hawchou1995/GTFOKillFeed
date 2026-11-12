using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;

static class Program
{
    static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "ildump") return IlDump(args[1], args[2]);
        if (args.Length >= 2) return Dump(args[0], args[1]);
        Console.Error.WriteLine("usage: Inspect <assembly> <out.jsonl> | Inspect ildump <assembly> <out.txt>");
        return 2;
    }

    static string Render(CustomAttributeArgument a)
    {
        object v = a.Value;
        if (v == null) return "null";
        if (v is TypeReference tr) return "typeof(" + tr.FullName + ")";
        if (v is string s) return "str:" + s;
        if (v is CustomAttributeArgument[] arr) return "[" + string.Join(";", arr.Select(Render)) + "]";
        if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
        return v.ToString();
    }

    static string Attr(CustomAttribute ca)
    {
        var sb = new StringBuilder(ca.AttributeType.FullName);
        var parts = new List<string>();
        foreach (var a in ca.ConstructorArguments) parts.Add(Render(a));
        foreach (var p in ca.Properties) parts.Add(p.Name + "=" + Render(p.Argument));
        foreach (var p in ca.Fields) parts.Add(p.Name + "=" + Render(p.Argument));
        if (parts.Count > 0) sb.Append("(" + string.Join(",", parts) + ")");
        return sb.ToString();
    }

    static void Emit(StreamWriter w, Dictionary<string, object> row)
    {
        w.WriteLine(JsonSerializer.Serialize(row));
    }

    static int Dump(string path, string outPath)
    {
        var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });
        var m = asm.MainModule;
        using var w = new StreamWriter(outPath, false, new UTF8Encoding(false));
        Emit(w, new Dictionary<string, object> {
            {"kind","assembly"},{"name",asm.Name.Name},{"version",asm.Name.Version.ToString()},
            {"culture",asm.Name.Culture ?? ""},
            {"publicKeyToken",asm.Name.HasPublicKey ? Convert.ToHexString(asm.Name.PublicKeyToken).ToLowerInvariant() : ""},
            {"module",m.Name},{"mvid",m.Mvid.ToString()},{"runtime",m.Runtime.ToString()}});
        foreach (var r in m.AssemblyReferences.OrderBy(x => x.Name, StringComparer.Ordinal))
            Emit(w, new Dictionary<string, object> {{"kind","ref"},{"name",r.Name},{"version",r.Version.ToString()},
                {"publicKeyToken",Convert.ToHexString(r.PublicKeyToken ?? Array.Empty<byte>()).ToLowerInvariant()}});
        foreach (var res in m.Resources.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            string h = "external"; long size = -1;
            if (res is EmbeddedResource er)
            {
                using var s = er.GetResourceStream();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                var bytes = ms.ToArray();
                h = Hash(bytes); size = bytes.Length;
            }
            Emit(w, new Dictionary<string, object> {{"kind","resource"},{"name",res.Name},{"hash",h},{"size",size}});
        }
        foreach (var att in asm.CustomAttributes)
            Emit(w, new Dictionary<string, object> {{"kind","asmattr"},{"attr",Attr(att)}});
        foreach (var t in m.Types.SelectMany(Flatten).OrderBy(x => x.FullName, StringComparer.Ordinal))
        {
            Emit(w, new Dictionary<string, object> {{"kind","type"},{"name",t.FullName},{"attrs",t.Attributes.ToString()},
                {"base",t.BaseType?.FullName ?? ""},{"interface",t.IsInterface}});
            foreach (var ca in t.CustomAttributes)
                Emit(w, new Dictionary<string, object> {{"kind","typeattr"},{"owner",t.FullName},{"attr",Attr(ca)}});
            foreach (var f in t.Fields.OrderBy(x => x.Name, StringComparer.Ordinal))
                Emit(w, new Dictionary<string, object> {{"kind","field"},{"owner",t.FullName},{"name",f.Name},
                    {"type",f.FieldType.FullName},{"attrs",f.Attributes.ToString()},
                    {"static",f.IsStatic},{"literal",f.HasConstant}});
            foreach (var p in t.Properties.OrderBy(x => x.Name, StringComparer.Ordinal))
                Emit(w, new Dictionary<string, object> {{"kind","prop"},{"owner",t.FullName},{"name",p.Name},
                    {"type",p.PropertyType.FullName},{"get",p.GetMethod != null},{"set",p.SetMethod != null}});
            foreach (var md in t.Methods.OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Parameters.Count))
            {
                var sig = md.ReturnType.FullName + " " + md.Name + "(" + string.Join(",", md.Parameters.Select(p => p.ParameterType.FullName)) + ")";
                string ilh = "-";
                if (md.HasBody)
                {
                    var ms = new MemoryStream();
                    foreach (var i in md.Body.Instructions)
                    {
                        ms.Write(BitConverter.GetBytes((int)i.OpCode.Value));
                        foreach (var o in i.Operand is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(i.Operand.ToString())) ms.WriteByte(o);
                    }
                    ms.Write(BitConverter.GetBytes(md.Body.Instructions.Count));
                    ilh = Hash(ms.ToArray());
                }
                Emit(w, new Dictionary<string, object> {{"kind","method"},{"owner",t.FullName},{"sig",sig},
                    {"il",ilh},{"instrs",md.HasBody ? md.Body.Instructions.Count : 0},
                    {"static",md.IsStatic},{"generic",md.HasGenericParameters}});
                foreach (var ca in md.CustomAttributes)
                    Emit(w, new Dictionary<string, object> {{"kind","methodattr"},{"owner",t.FullName},{"sig",sig},{"attr",Attr(ca)}});
            }
        }
        Console.WriteLine("dumped " + outPath);
        return 0;
    }

    static int IlDump(string path, string outPath)
    {
        var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });
        using var w = new StreamWriter(outPath, false, new UTF8Encoding(false));
        int n = 0;
        foreach (var t in asm.MainModule.Types.SelectMany(Flatten).OrderBy(x => x.FullName, StringComparer.Ordinal))
            foreach (var md in t.Methods.OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Parameters.Count))
            {
                w.WriteLine("### " + t.FullName + "|" + md.ReturnType.FullName + " " + md.Name + "(" + string.Join(",", md.Parameters.Select(p => p.ParameterType.FullName)) + ")");
                if (md.HasBody) foreach (var ins in md.Body.Instructions)
                    w.WriteLine("  " + ins.Offset.ToString("X4") + " " + ins.OpCode + " " + (ins.Operand ?? ""));
                n++;
            }
        Console.WriteLine("ildump methods: " + n);
        return 0;
    }

    static IEnumerable<TypeDefinition> Flatten(TypeDefinition t)
    {
        yield return t;
        foreach (var n in t.NestedTypes) foreach (var x in Flatten(n)) yield return x;
    }
}
