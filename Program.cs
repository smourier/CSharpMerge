using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CSharpMerge.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpMerge
{
    class Program
    {
        static void Main(string[] args)
        {
            if (Debugger.IsAttached)
            {
                SafeMain(args);
                return;
            }

            try
            {
                SafeMain(args);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        static void SafeMain(string[] args)
        {
            Console.WriteLine("CSharpMerge - Copyright (C) 2017-" + DateTime.Now.Year + " Simon Mourier. All rights reserved.");
            Console.WriteLine();
            if (CommandLine.HelpRequested || args.Length < 2)
            {
                Help();
                return;
            }

            var inputDirectoryPath = CommandLine.GetArgument<string>(0);
            var outputFilePath = CommandLine.GetArgument<string>(1);
            if (inputDirectoryPath == null || outputFilePath == null)
            {
                Help();
                return;
            }

            inputDirectoryPath = Path.GetFullPath(inputDirectoryPath);
            if (!Directory.Exists(inputDirectoryPath))
            {
                Console.WriteLine(inputDirectoryPath + " directory does not exists.");
                return;
            }

            outputFilePath = Path.GetFullPath(outputFilePath);

            var internalize = CommandLine.GetArgument("internalize", false);
            var excludedFiles = Conversions.SplitToList<string>(CommandLine.GetNullifiedArgument("exclude"), ';');
            var commentsFiles = Conversions.SplitToList<string>(CommandLine.GetNullifiedArgument("comments", @"..\LICENSE"), ';');
            var nullable = CommandLine.GetNullifiedArgument("nullable");
            var encoding = Encoding.UTF8;
            var enc = CommandLine.GetNullifiedArgument("encoding");
            if (enc != null)
            {
                if (int.TryParse(enc, NumberStyles.Integer, CultureInfo.CurrentCulture, out int cp))
                {
                    encoding = Encoding.GetEncoding(cp);
                }
                else
                {
                    encoding = Encoding.GetEncoding(enc);
                }
            }

            Console.WriteLine("Input       : " + inputDirectoryPath);
            Console.WriteLine("Output      : " + outputFilePath);
            Console.WriteLine("Internalize : " + internalize);
            Console.WriteLine("Encoding    : " + encoding.WebName);
            Console.WriteLine("Excluded    : " + string.Join(", ", excludedFiles));
            Console.WriteLine("Comments    : " + string.Join(", ", commentsFiles));
            Console.WriteLine();
            Merge(inputDirectoryPath, outputFilePath, encoding, excludedFiles, commentsFiles, internalize, nullable);
        }

        static void Help()
        {
            Console.WriteLine(Assembly.GetEntryAssembly().GetName().Name.ToUpperInvariant() + " <input directory path> <output file path>");
            Console.WriteLine();
            Console.WriteLine("Description:");
            Console.WriteLine("    This tool is used to merge .CS files from a directory into a single .CS file.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("    /incai               Includes files whose name ends with 'AssemblyInfo.cs' or 'AssemblyAttributes.cs'. Default is false.");
            Console.WriteLine("    /incgs               Includes files whose name ends with 'GlobalSuppressions.cs'. Default is false.");
            Console.WriteLine("    /toponly             Includes only the current directory in a search operation. Default is false (recursive).");
            Console.WriteLine("    /encoding:<enc>      Defines the encoding to use for the output file path. Default is '" + Encoding.UTF8.WebName + "'.");
            Console.WriteLine("    /exclude:<files>     Defines a list of file paths or names separated by ';'. Default is none.");
            Console.WriteLine("    /comments:<files>    Defines a list of file paths or names separated by ';' used as comments. Default is ..\\LICENSE.");
            Console.WriteLine("    /nullable:<text>     Adds nullable directive. Default is no directive.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine();
            Console.WriteLine("    " + Assembly.GetEntryAssembly().GetName().Name.ToUpperInvariant() + " c:\\mypath\\myproject myproject_merged.cs /encoding:" + Encoding.Default.WebName);
            Console.WriteLine();
            Console.WriteLine("    Merges recursively all .cs files, except AssemblyInfo.cs, from the myproject path");
            Console.WriteLine("    into a single myproject_merged.cs file using '" + Encoding.Default.WebName + "' encoding.");
            Console.WriteLine();
        }

        static Encoding DetectEncoding(string filePath)
        {
            using (var reader = new StreamReader(filePath, Encoding.Default, true))
            {
                reader.Peek();
                return reader.CurrentEncoding;
            }
        }

        static string NormalizeLineEndings(string text) => NormalizeLineEndings(text, Environment.NewLine);
        static string NormalizeLineEndings(string text, string newLine)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder(text);
            sb.Replace("\r", "").Replace("\n", newLine);
            return sb.ToString();
        }

        static void Merge(string inputDirectoryPath, string outputFilePath, Encoding encoding, IList<string> excludedFiles, IList<string> commentsFiles, bool internalize, string nullable)
        {
            var incai = CommandLine.GetArgument("incai", false);
            var incgs = CommandLine.GetArgument("incgs", false);
            var topdir = CommandLine.GetArgument("toponly", false);
            var option = SearchOption.TopDirectoryOnly;
            if (!topdir)
            {
                option |= SearchOption.AllDirectories;
            }

            var usings = new HashSet<string>(StringComparer.Ordinal);
            var comments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var codes = new List<string>();
            foreach (var file in Directory.GetFiles(inputDirectoryPath, "*.*", option))
            {
                var name = Path.GetFileName(file);

                if (commentsFiles.Contains(name, StringComparer.OrdinalIgnoreCase) || commentsFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Comment " + file);
                    comments.Add(file);
                    continue;
                }

                if (string.Compare(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase) != 0)
                    continue;

                if (name.IndexOf("TemporaryGeneratedFile", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("Skip " + file);
                    continue;
                }

                if (!incai && (name.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith("AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine("Skip " + file);
                    continue;
                }

                if (!incgs && name.EndsWith("GlobalSuppressions.cs", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Skip " + file);
                    continue;
                }

                if (excludedFiles.Contains(name, StringComparer.OrdinalIgnoreCase) || excludedFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Skip " + file);
                    continue;
                }

                var enc = DetectEncoding(file);
                var text = File.ReadAllText(file, enc);
                Console.WriteLine(file + ", encoding: " + enc.WebName);

                var tree = CSharpSyntaxTree.ParseText(text);
                var root = (CompilationUnitSyntax)tree.GetRoot();

                if (internalize)
                {
                    var publicTypes = new List<BaseTypeDeclarationSyntax>();
                    foreach (var rootMember in root.Members)
                    {
                        if (rootMember is NamespaceDeclarationSyntax ns)
                        {
                            foreach (var member in ns.Members)
                            {
                                if (member is BaseTypeDeclarationSyntax type)
                                {
                                    if (member.Modifiers.Any(m => m.ValueText == "public"))
                                    {
                                        publicTypes.Add(type);
                                    }
                                }
                            }
                        }
                    }

                    if (publicTypes.Count > 0)
                    {
                        var dic = new Dictionary<BaseTypeDeclarationSyntax, BaseTypeDeclarationSyntax>();

                        foreach (var publicType in publicTypes)
                        {
                            var publicToken = publicType.Modifiers.First(m => m.ValueText == "public");
                            var modifiers = publicType.Modifiers.Remove(publicToken).Add(SyntaxFactory.Identifier("internal "));

                            var internalType = publicType.WithModifiers(modifiers);
                            dic[publicType] = internalType;
                        }

                        root = root.ReplaceNodes(publicTypes, (publ, inte) => dic[publ]);
                    }
                }

                foreach (var us in root.Usings)
                {
                    usings.Add(us.ToString());
                }

                var withoutUsings = root.WithUsings(new SyntaxList<UsingDirectiveSyntax>());
                codes.Add(withoutUsings.ToString());
            }

            // scan for comments in all places
            foreach (var commentFile in commentsFiles)
            {
                var path = Path.GetFullPath(Path.Combine(inputDirectoryPath, commentFile));
                if (File.Exists(path))
                {
                    Console.WriteLine("Comment " + path);
                    comments.Add(path);
                }
            }

            // sort out global vs non global ns...
            foreach (var us in usings.ToArray())
            {
                if (!us.StartsWith("global "))
                {
                    var ns = us.Split(new[] { "using " }, StringSplitOptions.None)[1];
                    var globalUsing = "global using global::" + ns;
                    if (usings.Contains(globalUsing))
                    {
                        usings.Remove(us);
                    }
                }
            }

            var uss = usings.ToList();
            uss.Sort();

            // bit of a hack... seems to work for me so far :-)
            using (var writer = new StreamWriter(outputFilePath, false, encoding))
            {
                if (nullable != null)
                {
                    writer.Write("#nullable ");
                    writer.WriteLine(nullable);
                }

                foreach (var file in comments)
                {
                    var enc = DetectEncoding(file);
                    var comment = File.ReadAllText(file, enc);
                    writer.WriteLine("/*");
                    writer.Write(NormalizeLineEndings(comment));
                    writer.WriteLine("*/");
                }

                foreach (var us in uss)
                {
                    writer.WriteLine(NormalizeLineEndings(us));
                }

                writer.WriteLine();
                foreach (var code in codes)
                {
                    writer.WriteLine(NormalizeLineEndings(code));
                }
            }
        }
    }
}
