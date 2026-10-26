using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using VRage.FileSystem;
using VRage.Utils;

namespace ClientPlugin.Shaders
{
    /// <summary>
    /// Flattens and inlines HLSL include directives (#include <...>) into a single consolidated file.
    /// This prevents D3DCompiler callback failures and compatibility issues on Linux/Wine/Proton setups.
    /// </summary>
    public static class ShaderFlattener
    {
        private static readonly Regex IncludeRegex = new Regex(
            @"^\s*#include\s*[<""](?<path>[^>""]+)[>""]",
            RegexOptions.Compiled | RegexOptions.Multiline
        );

        /// <summary>
        /// Reads an HLSL shader file, resolves and inlines all recursively referenced headers,
        /// and outputs the flattened code string.
        /// </summary>
        /// <param name="mainShaderFilePath">Absolute or relative path to the entry HLSL file.</param>
        /// <param name="searchDirectories">Directories to search when resolving relative includes.</param>
        /// <returns>Flattened shader source ready for MyPixelShaders.Create().</returns>
        public static string Flatten(string mainShaderFilePath, IEnumerable<string> searchDirectories = null)
        {
            if (!File.Exists(mainShaderFilePath))
            {
                throw new FileNotFoundException($"HLSL entry file not found at path: {mainShaderFilePath}");
            }

            var resolvedDirectories = new List<string>
            {
                Path.GetDirectoryName(mainShaderFilePath)
            };

            // Include Space Engineers game Content/Shaders directory if accessible
            string seContentDir = MyFileSystem.ContentPath;
            if (!string.IsNullOrEmpty(seContentDir))
            {
                string seShadersDir = Path.Combine(seContentDir, "Shaders");
                if (Directory.Exists(seShadersDir))
                {
                    resolvedDirectories.Add(seShadersDir);
                }
            }

            if (searchDirectories != null)
            {
                resolvedDirectories.AddRange(searchDirectories);
            }

            var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return InlineShaderIncludes(mainShaderFilePath, resolvedDirectories, visitedFiles);
        }

        private static string InlineShaderIncludes(string currentFilePath, List<string> searchDirs, HashSet<string> visitedFiles)
        {
            string fullPath = Path.GetFullPath(currentFilePath);
            if (visitedFiles.Contains(fullPath))
            {
                // Prevent cyclic inclusions
                return string.Empty;
            }

            visitedFiles.Add(fullPath);
            string content = File.ReadAllText(fullPath);

            return IncludeRegex.Replace(content, match =>
            {
                string relativePath = match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar);
                string resolvedIncludePath = null;

                foreach (string dir in searchDirs)
                {
                    string candidate = Path.Combine(dir, relativePath);
                    if (File.Exists(candidate))
                    {
                        resolvedIncludePath = candidate;
                        break;
                    }
                }

                if (resolvedIncludePath == null)
                {
                    MyLog.Default.Warning($"[VolumetricClouds] ShaderFlattener could not find include file '{relativePath}'. Retaining original #include directive.");
                    return match.Value;
                }

                return $"// --- BEGIN INLINE: {relativePath} ---\n" +
                       InlineShaderIncludes(resolvedIncludePath, searchDirs, visitedFiles) +
                       $"\n// --- END INLINE: {relativePath} ---\n";
            });
        }
    }
}
