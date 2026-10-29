using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

if (args.Length < 1) throw new ArgumentException("Pass the Assets directory.");
var files = Directory.GetFiles(args[0], "*.cs", SearchOption.AllDirectories);
var errors = new List<Diagnostic>();
foreach (var file in files)
{
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file),
        new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: new[] { "UNITY_EDITOR", "UNITY_6000_3_OR_NEWER" }), file);
    errors.AddRange(tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
}
foreach (var error in errors) Console.WriteLine(error);
Console.WriteLine($"C# syntax: {files.Length} files, {errors.Count} errors. Unity API/type checking still requires the editor.");
return errors.Count == 0 ? 0 : 1;
