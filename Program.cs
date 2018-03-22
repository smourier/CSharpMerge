using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            Console.WriteLine("CSharpMerge - Copyright (C) 2016-" + DateTime.Now.Year + " Simon Mourier. All rights reserved.");
            Console.WriteLine();
            if (CommandLine.HelpRequested || args.Length < 2)
            {
                Help();
                return;
            }

            string inputDirectoryPath = CommandLine.GetArgument<string>(0);
            string outputFilePath = CommandLine.GetArgument<string>(1);
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

            var excludedFiles = Conversions.SplitToList<string>(CommandLine.GetNullifiedArgument("exclude"), ';');
            var commentsFiles = Conversions.SplitToList<string>(CommandLine.GetNullifiedArgument("comments", @"..\LICENSE"), ';');
            var encoding = Encoding.UTF8;
            var enc = CommandLine.GetNullifiedArgument("encoding");
            if (enc != null)
            {
                if (int.TryParse(enc, out int cp))
                {
                    encoding = Encoding.GetEncoding(cp);
                }
                else
                {
                    encoding = Encoding.GetEncoding(enc);
                }
            }

            Console.WriteLine("Input    : " + inputDirectoryPath);
            Console.WriteLine("Output   : " + outputFilePath);
            Console.WriteLine("Encoding : " + encoding.WebName);
            Console.WriteLine("Excluded : " + string.Join(", ", excludedFiles));
            Console.WriteLine("Comments : " + string.Join(", ", commentsFiles));
            Console.WriteLine();
            Merge(inputDirectoryPath, outputFilePath, encoding, excludedFiles, commentsFiles);
        }

        static void Help()
        {
            Console.WriteLine(Assembly.GetEntryAssembly().GetName().Name.ToUpperInvariant() + " <input directory path> <output file path>");
            Console.WriteLine();
            Console.WriteLine("Description:");
            Console.WriteLine("    This tool is used to merge .CS files from a directory into a single .CS file.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("    /incai               Includes files whose name ends with 'AssemblyInfo.cs'. Default is false.");
            Console.WriteLine("    /toponly             Includes only the current directory in a search operation. Default is false (recursive).");
            Console.WriteLine("    /encoding:<enc>      Defines the encoding to use for the output file path. Default is '" + Encoding.UTF8.WebName + "'.");
            Console.WriteLine("    /exclude:<files>     Defines a list of file paths or names separated by ';'. Default is none.");
            Console.WriteLine("    /comments:<files>    Defines a list of file paths or names separated by ';' used as comments. Default is ..\\LICENSE.");
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

        static void Merge(string inputDirectoryPath, string outputFilePath, Encoding encoding, IList<string> excludedFiles, IList<string> commentsFiles)
        {
            bool incai = CommandLine.GetArgument("incai", false);
            bool topdir = CommandLine.GetArgument("toponly", false);
            var option = SearchOption.TopDirectoryOnly;
            if (!topdir)
            {
                option |= SearchOption.AllDirectories;
            }

            var usings = new HashSet<string>();
            var comments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var codes = new List<string>();
            foreach (var file in Directory.GetFiles(inputDirectoryPath, "*.*", option))
            {
                string name = Path.GetFileName(file);

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

                if (!incai && name.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
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
                string text = File.ReadAllText(file, enc);
                Console.WriteLine(file + ", encoding: " + enc.WebName);

                var tree = CSharpSyntaxTree.ParseText(text);
                var root = (CompilationUnitSyntax)tree.GetRoot();
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
                string path = Path.GetFullPath(Path.Combine(inputDirectoryPath, commentFile));
                if (File.Exists(path))
                {
                    Console.WriteLine("Comment " + path);
                    comments.Add(path);
                }
            }

            var uss = usings.ToList();
            uss.Sort();

            // bit of a hack... seems to work for me so far :-)
            using (var writer = new StreamWriter(outputFilePath, false, encoding))
            {
                foreach (var file in comments)
                {
                    var enc = DetectEncoding(file);
                    string comment = File.ReadAllText(file, enc);
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
