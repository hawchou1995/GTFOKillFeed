using System;
using System.IO;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.Metadata;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: Decompile <assembly> <outDir> [refDir...]");
            return 2;
        }
        string asm = args[0], outDir = args[1];
        var resolver = new UniversalAssemblyResolver(asm, false, null);
        for (int i = 2; i < args.Length; i++) resolver.AddSearchDirectory(args[i]);
        var settings = new DecompilerSettings(LanguageVersion.CSharp10_0);
        settings.ThrowOnAssemblyResolveErrors = false;
        var decompiler = new WholeProjectDecompiler(settings, resolver, null, null);
        using var module = new PEFile(asm);
        Directory.CreateDirectory(outDir);
        decompiler.DecompileProject(module, outDir);
        Console.WriteLine("decompiled to " + outDir);
        return 0;
    }
}
