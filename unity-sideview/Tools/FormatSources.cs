using System;
using System.IO;
using System.Linq;
using UnityPipeline.Microsoft.CodeAnalysis;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp;
public static class FormatSources {
 public static string Main(){
  var paths=Directory.GetFiles("Assets/Seabreeze/Scripts/World","*.cs").Concat(new[]{"Assets/Seabreeze/Scripts/Core/GameBootstrap.cs","Assets/Seabreeze/Editor/ProjectBuilder.cs","Assets/Seabreeze/Scripts/Development/RuntimeVerification.cs"});
  int count=0;
  foreach(var path in paths){string original=File.ReadAllText(path);var tree=CSharpSyntaxTree.ParseText(original, new CSharpParseOptions().WithPreprocessorSymbols("UNITY_EDITOR"));if(tree.GetDiagnostics().Any(x=>x.Severity==DiagnosticSeverity.Error))throw new Exception("Cannot format invalid source: "+path);string formatted=tree.GetRoot().NormalizeWhitespace("    ","\n").ToFullString()+"\n";File.WriteAllText(path,formatted);count++;}
  return "Formatted "+count+" C# sources with Roslyn, preserving syntax.";
 }
}
