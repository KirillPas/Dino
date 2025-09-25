// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    static class ShaderPatcher
    {
        public const string InstancingHeaderGUID = "4223a6d499be4d6f88dbcaca94e8948d";
        public const string PatchedShaderSuffix = " (Flora)";
        public const string ShaderDirectory = "Assets/Flora/Shaders";

        internal static Shader GetOrCreatePatchedShader(Shader shader)
        {
            if (FloraShaderCache.instance.TryGetImporter(shader, out FloraShaderImporter importer))
            {
                if (importer)
                    return importer.PatchedShader;
            }

            Shader patchedShader = Shader.Find(shader.name + PatchedShaderSuffix);
            if (patchedShader)
                return patchedShader;

            FloraShaderData metadata = new FloraShaderData
            {
                ImporterVersion = FloraShaderImporter.Version,
                SourceGUID = "",
                SourceType = FloraSourceShaderType.Invalid
            };

            string inputShaderPath = AssetDatabase.GetAssetPath(shader);

            string guid = AssetDatabase.AssetPathToGUID(inputShaderPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.Log("ShaderPatcher: Error getting GUID of input shader.");
                return null;
            }

            metadata.SourceType = FloraSourceShaderType.Shader;
            metadata.SourceGUID = guid;

            string outputShaderPath = inputShaderPath;

            if (outputShaderPath.IndexOf("Assets/", StringComparison.InvariantCultureIgnoreCase) == -1)
            {
                // if the shader is not in the project folder, save it in the shaders folder
                outputShaderPath = $"{ShaderDirectory}/{shader.name}";
                // split the path and remove the file name
                outputShaderPath = outputShaderPath[..outputShaderPath.LastIndexOf('/')];
                if (outputShaderPath.Length > 0 && outputShaderPath[^1] != '/')
                    outputShaderPath += "/";

                // create the folder hierarchy if it doesn't exist
                string shaderDirectory = Path.GetDirectoryName(outputShaderPath);
                if (!string.IsNullOrEmpty(shaderDirectory) && !AssetDatabase.IsValidFolder(shaderDirectory))
                {
                    string[] directories = shaderDirectory.Split(Path.DirectorySeparatorChar);
                    string rootPath = "";
                    foreach (var directory in directories)
                    {
                        var newPath = rootPath + directory;
                        if (!AssetDatabase.IsValidFolder(newPath))
                            AssetDatabase.CreateFolder(rootPath.TrimEnd(Path.DirectorySeparatorChar), directory);
                        rootPath = newPath + Path.DirectorySeparatorChar;
                    }
                }
            }

            if (!string.IsNullOrEmpty(outputShaderPath) && File.Exists(outputShaderPath))
            {
                // if the file already exists, save it in the same folder
                outputShaderPath = Path.GetDirectoryName(outputShaderPath)!;

                // https://forum.unity.com/threads/how-to-implement-create-new-asset.759662/
                outputShaderPath = outputShaderPath.Replace("\\", "/");
            }

            if (string.IsNullOrEmpty(outputShaderPath))
                outputShaderPath = $"{ShaderDirectory}/";
            else if (outputShaderPath.Length > 0 && outputShaderPath[^1] != '/')
                outputShaderPath += "/";

            string newFilename = $"{shader}{PatchedShaderSuffix}.{FloraShaderImporter.Extension}";
            if (!string.IsNullOrEmpty(inputShaderPath))
                newFilename = $"{Path.GetFileNameWithoutExtension(inputShaderPath)}{PatchedShaderSuffix}.{FloraShaderImporter.Extension}";

            string outputPath = $"{outputShaderPath}{newFilename}";
            patchedShader = AssetDatabase.LoadMainAssetAtPath(outputPath) as Shader;
            if (patchedShader)
                return patchedShader;

            outputPath = AssetDatabase.GenerateUniqueAssetPath(outputPath);

            if (string.IsNullOrEmpty(outputPath))
            {
                Debug.Log("ShaderPatcher: Error creating output path for new shader.");
                return null;
            }

            try
            {
                string content = JsonUtility.ToJson(metadata);
                File.WriteAllText(outputPath, content);
                AssetDatabase.ImportAsset(outputPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"ShaderPatcher: Error creating new shader: {e.Message}");
                return null;
            }

            FloraShaderImporter newImporter = AssetDatabase.LoadAssetAtPath<FloraShaderImporter>(outputPath);
            if (newImporter)
            {
                FloraShaderCache.instance.Register(shader, newImporter);
                return newImporter.PatchedShader;
            }

            return null;
        }

        [Flags]
        internal enum PatchFlags
        {
            None = 0,
            DebugSymbols = 1 << 0,
        }

        internal static string PatchShaderCode(string sourceCode, PatchFlags flags = PatchFlags.None)
        {
            string floraHeaderPath = AssetDatabase.GUIDToAssetPath(InstancingHeaderGUID);
            if (string.IsNullOrEmpty(floraHeaderPath))
            {
                Debug.LogError($"ShaderPatcher: Flora header not found!");
                return sourceCode;
            }

            return Patch(sourceCode, floraHeaderPath, flags);
        }

        enum Section
        {
            None,
            Shader,
            Properties,
            SubShader,
            SubShaderTags,
            Pass,
            PassTags,
            Include,
            Program,
            Unknown
        }

        static class TokenRegex
        {
            const RegexOptions k_DefaultRegexOptions = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled;

            // Symbols
            public static readonly Regex Braces = new Regex(@"[{]|[}]", k_DefaultRegexOptions);

            // Misc
            public static readonly Regex ShaderName = new Regex(@"^[ \t]*Shader\s+""([^""]*)""", k_DefaultRegexOptions);
            public static readonly Regex PassName = new Regex(@"^[ \t]*Pass\s+""([^""]*)""", k_DefaultRegexOptions);
            public static readonly Regex CGInclude = new Regex(@"^[ \t]*CGINCLUDE", k_DefaultRegexOptions);
            public static readonly Regex HLSLInclude = new Regex(@"^[ \t]*HLSLINCLUDE", k_DefaultRegexOptions);
            public static readonly Regex CGProgram = new Regex(@"^[ \t]*CGPROGRAM", k_DefaultRegexOptions);
            public static readonly Regex HLSLProgram = new Regex(@"^[ \t]*HLSLPROGRAM", k_DefaultRegexOptions);
            public static readonly Regex EndCG = new Regex(@"^[ \t]*ENDCG", k_DefaultRegexOptions);
            public static readonly Regex EndHLSL = new Regex(@"^[ \t]*ENDHLSL", k_DefaultRegexOptions);

            // Sections
            public static readonly Regex Shader = new Regex(@"^[ \t]*Shader", k_DefaultRegexOptions);
            public static readonly Regex Properties = new Regex(@"^[ \t]*Properties", k_DefaultRegexOptions);
            public static readonly Regex SubShader = new Regex(@"^[ \t]*SubShader", k_DefaultRegexOptions);
            public static readonly Regex Tags = new Regex(@"^[ \t]*Tags", k_DefaultRegexOptions);
            public static readonly Regex Pass = new Regex(@"^[ \t]*Pass", k_DefaultRegexOptions);

            // Tags
            public static readonly Regex InlineTags = new Regex(@"^[ \t]*Tags\s*{.*?}", k_DefaultRegexOptions);
            public static readonly Regex RenderPipelineTag = new Regex(@"""RenderPipeline""\s*=\s*""(.*?)""", k_DefaultRegexOptions);
            public static readonly Regex ShaderGraphShaderTag = new Regex(@"""ShaderGraphShader""\s*=\s*""true""", k_DefaultRegexOptions);

            // Comments
            public static readonly Regex Include = new Regex(@"^[ \t]*#include(_with_pragmas)?\s*""[^""]*""", k_DefaultRegexOptions);
            public static readonly Regex Comment = new Regex(@"^[ \t]*//.*?$", k_DefaultRegexOptions);
            public static readonly Regex CommentBlockBegin = new Regex(@"^[ \t]*/\*", k_DefaultRegexOptions);
            public static readonly Regex CommentBlockEnd = new Regex(@"^[ \t]*\*/", k_DefaultRegexOptions);

            // Pragmas
            public static readonly Regex Pragma = new Regex(@"^[ \t]*#pragma\s+.*", k_DefaultRegexOptions);
            public static readonly Regex PragmaMultiCompileInstancing = new Regex(@"^[ \t]*#pragma\s+multi_compile_instancing.*$", k_DefaultRegexOptions);
            public static readonly Regex PragmaInstancingOptions = new Regex(@"^[ \t]*#pragma\s+instancing_options\s+(.*?\s+)?procedural:\w+(\s+.*)?$", k_DefaultRegexOptions);
            public static readonly Regex SurfacePragma = new Regex(@"^[ \t]*#pragma\s+surface\s+.*", k_DefaultRegexOptions);
            public static readonly Regex ShaderTarget = new Regex(@"^[ \t]*#pragma\s+target\s+.*", k_DefaultRegexOptions);

            // Defines
            public static readonly Regex DefineIf = new Regex(@"^[ \t]*#if", k_DefaultRegexOptions);
            public static readonly Regex DefineEndIf = new Regex(@"^[ \t]*#endif", k_DefaultRegexOptions);

            // Includes
            public static readonly Regex AnyInclude = new Regex(@"^[ \t]*#include(_with_pragmas)?\s*"".*""", k_DefaultRegexOptions);
            public static readonly Regex FloraInclude = new Regex(@"^[ \t]*#include(_with_pragmas)?\s*""Packages/com\.ma\.flora/.*\.hlsl""", k_DefaultRegexOptions);
            public static readonly Regex UnityCGVariablesInclude = new Regex(@"^[ \t]*#include(_with_pragmas)?\s*""UnityShaderVariables\.cginc""", k_DefaultRegexOptions);
            public static readonly Regex UnityCGInclude = new Regex(@"^[ \t]*#include(_with_pragmas)?\s*""Unity[a-zA-Z]*\.cginc""", k_DefaultRegexOptions);
            public static readonly Regex UniversalCoreInclude = new Regex(@"^[ \t]*#include(_with_pragmas)?\s*""Packages/com\.unity\.render-pipelines\.universal/ShaderLibrary/Core\.hlsl""", k_DefaultRegexOptions);
            public static readonly Regex HDRPShaderVariablesInclude = new Regex(@"^[ \t]*#include(_with_pragmas)?\s*""Packages/com\.unity\.render-pipelines\.high-definition/Runtime/ShaderLibrary/ShaderVariables\.hlsl""", k_DefaultRegexOptions);

            // Common names for includes as a last resort
            public static readonly Regex PassInclude = new Regex(@"^[ \t]*#include(_with_pragmas)?\s*"".*Pass.*\.hlsl""", k_DefaultRegexOptions);
            public static readonly Regex InputInclude = new Regex(@"^[ \t]*#include(_with_pragmas)?\s*"".*Input.*\.hlsl""", k_DefaultRegexOptions);
            public static readonly Regex LightingInclude = new Regex(@"^[ \t]*#include(_with_pragmas)?\s*"".*Lighting.*\.hlsl""", k_DefaultRegexOptions);
        }

        class ShaderPatcherState
        {
            public Section CurrentSection
            {
                get => SectionStack.Count > 0 ? SectionStack.Peek() : Section.None;
                set
                {
                    Section previousSection = CurrentSection;
                    if (value != previousSection)
                    {
                        if (previousSection == Section.Pass)
                        {
                            IsExcludedSection = false;
                            CurrentPassName = "";
                        }

                        WaitingForSectionBrace = true;
                        IsExcludedSection = false;

                        switch (value)
                        {
                            case Section.Include:
                            case Section.Program:
                                IncludeIndices.Clear();
                                ResetFlags(value);
                                break;

                        }

                        SectionStack.Push(value);
                    }
                }
            }

            public bool CanModifyLine => IsExcludedSection || CurrentSection != Section.None && CurrentSection != Section.Unknown;
            public bool IsExcludedSection;
            public string CurrentPassName;

            public bool WantsDebugSymbols;
            public bool IsLegacyShader;

            public bool IncludeHasFloraIncluded;
            public bool IncludeHasProceduralOptions;
            public bool IncludeHashMultiCompileInstancingPragma;
            public bool IncludeAddedDebugSymbols;

            public bool ProgramHasFloraIncluded;
            public bool ProgramHasProceduralOptions;
            public bool ProgramHasMultiCompileInstancingPragma;
            public bool ProgramAddedDebugSymbols;

            public bool HasFloraBeenIncluded => IncludeHasFloraIncluded || ProgramHasFloraIncluded;
            public bool HasProceduralOptions => IncludeHasProceduralOptions || ProgramHasProceduralOptions;
            public bool HasMultiCompileInstancingPragma => IncludeHashMultiCompileInstancingPragma || ProgramHasMultiCompileInstancingPragma;
            public bool HasDebugSymbols => IncludeAddedDebugSymbols || ProgramAddedDebugSymbols;

            public HashSet<string> ModifiedPassNames = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            public List<string> Lines = new List<string>();
            public List<int> IncludeIndices = new List<int>();

            public bool WaitingForSectionBrace;
            public Stack<Section> SectionStack = new Stack<Section>();
            public bool IsInsideIncludeArea => CurrentSection is Section.Include or Section.Program;
            public bool ShouldBacktrack = false;

            public void AppendLine(string line)
            {
                Lines.Add(line);
            }

            public void InsertLine(int index, string line)
            {
                Lines.Insert(index, line);
            }

            public void AppendIncludeLine(string line)
            {
                IncludeIndices.Add(Lines.Count);
                Lines.Add(line);
            }

            void ResetFlags(Section section)
            {
                if (section == Section.Include)
                {
                    IncludeHasFloraIncluded = false;
                    IncludeHasProceduralOptions = false;
                    IncludeHashMultiCompileInstancingPragma = false;
                    IncludeAddedDebugSymbols = false;
                }
                else if (section == Section.Program)
                {
                    ProgramHasFloraIncluded = false;
                    ProgramHasProceduralOptions = false;
                    ProgramHasMultiCompileInstancingPragma = false;
                    ProgramAddedDebugSymbols = false;
                }
            }
        }

        static readonly HashSet<string> s_ExcludedPassNames = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "Meta",
        };

        static string Patch(string shaderSource, string includePath, PatchFlags flags)
        {
            shaderSource = FixLineEndings(shaderSource);

            ShaderPatcherState state = new ShaderPatcherState();
            state.AppendLine($"// FLORA SHADER ({FloraShaderImporter.Version:0000})");
            if (flags.HasFlag(PatchFlags.DebugSymbols))
                state.WantsDebugSymbols = true;

            try
            {
                using StringReader reader = new StringReader(shaderSource);
                while (reader.ReadLine() is { } line)
                {
                    ProcessLine(line, state, includePath);
                }

                return string.Join(Environment.NewLine, state.Lines);
            }
            catch (Exception e)
            {
                Debug.LogError($"ShaderPatcher: Error patching shader: {e.Message}");
                return shaderSource;
            }
        }

        static void ProcessLine(string line, ShaderPatcherState state, string includePath)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                state.AppendLine(line);
                return;
            }

            state.CurrentSection = DetermineCurrentSection(line, state, out bool isEndOfProgramSection);
            if (state.CurrentSection == Section.Shader)
                line = ModifyShaderName(line);

            TrackBraces(line, state);

            if (isEndOfProgramSection && !state.HasFloraBeenIncluded)
            {
                // Process headers at the end of a program
                InsertProgramHeaders(state, includePath);
            }

            if (state.CurrentSection == Section.Program && TokenRegex.AnyInclude.IsMatch(line))
            {
                state.AppendIncludeLine(line);
            }
            else if (!state.CanModifyLine || !TryModifyLine(ref line, ref state, includePath))
            {
                state.AppendLine(line);
            }
        }

        static string ModifyShaderName(string line)
        {
            Match shaderNameMatch = TokenRegex.ShaderName.Match(line);
            return shaderNameMatch.Success ? TokenRegex.ShaderName.Replace(line, AddFloraToName) : line;
        }

        static bool TryModifyLine(ref string line, ref ShaderPatcherState state, string instancingIncludePath)
        {
            if (state.IsExcludedSection)
                return false;

            switch (state.CurrentSection)
            {
                case Section.Pass:
                    return CheckAndExcludePass(ref line, state);
                case Section.Include:
                    return CheckAndModifyLine(ref line, state, instancingIncludePath, Section.Include);
                case Section.Program:
                    return CheckAndModifyLine(ref line, state, instancingIncludePath, Section.Program);
                default:
                    return false;
            }
        }

        static bool CheckAndExcludePass(ref string line, ShaderPatcherState state)
        {
            Match passNameMatch = TokenRegex.PassName.Match(line);
            state.CurrentPassName = passNameMatch.Success ? passNameMatch.Groups[1].Value : state.CurrentPassName;
            if (passNameMatch.Success && s_ExcludedPassNames.Contains(passNameMatch.Groups[1].Value))
                state.IsExcludedSection = true;

            return false;
        }

        static bool CheckAndModifyLine(ref string line, ShaderPatcherState state, string instancingIncludePath, Section section)
        {
            // Handle pragma multi_compile_instancing
            if (TokenRegex.PragmaMultiCompileInstancing.IsMatch(line))
            {
                if (state.HasMultiCompileInstancingPragma)
                {
                    state.AppendLine(CommentOutLine(line));
                    return true;
                }
                else
                {
                    if (section == Section.Include)
                        state.IncludeHashMultiCompileInstancingPragma = true;
                    else
                        state.ProgramHasMultiCompileInstancingPragma = true;
                    return false;
                }
            }

            // Update shader target
            if (TokenRegex.ShaderTarget.IsMatch(line))
            {
                if (!state.ModifiedPassNames.Contains(state.CurrentPassName))
                {
                    string indent = GetIndentation(line);
                    state.AppendLine($"{indent}#pragma target 4.5");

                    if (!string.IsNullOrEmpty(state.CurrentPassName))
                        state.ModifiedPassNames.Add(state.CurrentPassName);

                    return true;
                }
            }

            // Remove existing procedural options
            if (TokenRegex.PragmaInstancingOptions.IsMatch(line))
            {
                state.AppendLine(CommentOutProceduralOption(line));
                return true;
            }

            // Remove existing flora include
            if (TokenRegex.FloraInclude.IsMatch(line))
            {
                state.AppendLine(CommentOutLine(line));
                return true;
            }

            // Add debug symbols
            if (state.WantsDebugSymbols && !state.HasDebugSymbols && TokenRegex.Pragma.IsMatch(line))
            {
                string indent = GetIndentation(line);
                state.AppendLine($"{indent}#pragma enable_d3d11_debug_symbols");
                if (section == Section.Include)
                    state.IncludeAddedDebugSymbols = true;
                else
                    state.ProgramAddedDebugSymbols = true;
            }

            // Add procedural options
            if (!state.ProgramHasProceduralOptions && IsValidProceduralOptionsLocation(section, line))
            {
                state.AppendLine(line);

                string indent = GetIndentation(line);
                state.AppendLine($"{indent}// --- FLORA_SHADER_BEGIN ---");
                if (!state.HasMultiCompileInstancingPragma)
                {
                    state.ProgramHasMultiCompileInstancingPragma = true;
                    state.AppendLine($"{indent}#pragma multi_compile_instancing");
                }

                state.AppendLine($"{indent}#pragma instancing_options procedural:SetupFloraInstancingData forwardadd");
                state.AppendLine($"{indent}// --- FLORA_SHADER_END ---");
                state.ProgramHasProceduralOptions = true;
                return true;
            }

            // Add flora instancing include (Include section only)
            if (section == Section.Include && !state.HasFloraBeenIncluded && IsValidFloraIncludeLocation(section, line))
            {
                state.AppendLine(line);

                string indent = GetIndentation(line);
                state.AppendLine($"{indent}// --- FLORA_SHADER_BEGIN ---");
                state.AppendLine($"{indent}#include_with_pragmas \"{instancingIncludePath}\"");
                state.AppendLine($"{indent}// --- FLORA_SHADER_END ---");
                state.IncludeHasFloraIncluded = true;
                return true;
            }

            return false;
        }

        static void InsertProgramHeaders(ShaderPatcherState state, string includePath)
        {
            if (state.HasFloraBeenIncluded) return;

            foreach (int includeIndex in state.IncludeIndices)
            {
                if (IsValidFloraIncludeLocation(Section.Program, state.Lines[includeIndex]) ||
                    TokenRegex.PassInclude.IsMatch(state.Lines[includeIndex]) ||
                    TokenRegex.InputInclude.IsMatch(state.Lines[includeIndex]) ||
                    TokenRegex.LightingInclude.IsMatch(state.Lines[includeIndex]))
                {
                    bool insertBefore = TokenRegex.PassInclude.IsMatch(state.Lines[includeIndex]);
                    int includeIndexOffset = insertBefore ? 0 : 1;
                    string include = state.Lines[includeIndex];
                    string indent = GetIndentation(include);
                    int insertIndex = includeIndex + includeIndexOffset;
                    state.InsertLine(insertIndex + 0, $"{indent}// --- FLORA_SHADER_BEGIN ---");
                    state.InsertLine(insertIndex + 1, $"{indent}#include_with_pragmas \"{includePath}\"");
                    state.InsertLine(insertIndex + 2, $"{indent}// --- FLORA_SHADER_END ---");
                    state.ProgramHasFloraIncluded = true;
                    break;
                }
            }

            if (!state.ProgramHasFloraIncluded && state.IncludeIndices.Count > 0)
            {
                int lastIncludeIndex = state.IncludeIndices.LastOrDefault();
                string lastInclude = state.Lines[lastIncludeIndex];
                string indent = GetIndentation(lastInclude);
                state.InsertLine(lastIncludeIndex + 0, $"{indent}// --- FLORA_SHADER_BEGIN ---");
                state.InsertLine(lastIncludeIndex + 1, $"{indent}#include_with_pragmas \"{includePath}\"");
                state.InsertLine(lastIncludeIndex + 2, $"{indent}// --- FLORA_SHADER_END ---");
            }
        }

        static string GetIndentation(string line)
        {
            return new string(line.TakeWhile(char.IsWhiteSpace).ToArray());
        }

        static bool IsValidProceduralOptionsLocation(Section section, string line)
        {
            if (section is not Section.Program)
                return false;

            return TokenRegex.SurfacePragma.IsMatch(line) || TokenRegex.Pragma.IsMatch(line);
        }

        static bool IsValidFloraIncludeLocation(Section section, string line)
        {
            if (section is not Section.Include and not Section.Program)
                return false;

            return TokenRegex.UnityCGVariablesInclude.IsMatch(line)
                   || TokenRegex.UnityCGInclude.IsMatch(line)
                   || TokenRegex.UniversalCoreInclude.IsMatch(line)
                   || TokenRegex.HDRPShaderVariablesInclude.IsMatch(line);
        }

        // --- Utility ---

        static Section DetermineCurrentSection(string line, ShaderPatcherState state, out bool isEndOfProgramSection)
        {
            isEndOfProgramSection = false;
            switch (state.CurrentSection)
            {
                case Section.None:
                    if (TokenRegex.Shader.IsMatch(line))
                        return Section.Shader;
                    break;
                case Section.Shader:
                    if (TokenRegex.Properties.IsMatch(line))
                        return Section.Properties;
                    if (TokenRegex.HLSLInclude.IsMatch(line) || TokenRegex.CGInclude.IsMatch(line))
                    {
                        state.IsLegacyShader = TokenRegex.CGInclude.IsMatch(line);
                        state.CurrentSection = Section.Include;
                        return Section.Include;
                    }
                    if (TokenRegex.SubShader.IsMatch(line))
                        return Section.SubShader;
                    break;
                case Section.SubShader:
                    if (TokenRegex.Tags.IsMatch(line)) return
                        Section.SubShaderTags;
                    if (TokenRegex.HLSLInclude.IsMatch(line) || TokenRegex.CGInclude.IsMatch(line))
                    {
                        state.IsLegacyShader = TokenRegex.CGInclude.IsMatch(line);
                        state.CurrentSection = Section.Include;
                        return Section.Include;
                    }
                    if (TokenRegex.HLSLProgram.IsMatch(line) || TokenRegex.CGProgram.IsMatch(line))
                    {
                        state.CurrentSection = Section.Program;
                        return Section.Program;
                    }
                    if (TokenRegex.Pass.IsMatch(line)) return
                        Section.Pass;
                    break;
                case Section.Pass:
                    if (TokenRegex.Tags.IsMatch(line))
                        return Section.PassTags;
                    if (TokenRegex.HLSLProgram.IsMatch(line) || TokenRegex.CGProgram.IsMatch(line))
                    {
                        state.IsLegacyShader = TokenRegex.CGProgram.IsMatch(line);
                        state.CurrentSection = Section.Program;
                        return Section.Program;
                    }
                    break;
                case Section.Include:
                case Section.Program:
                    if (TokenRegex.EndHLSL.IsMatch(line) || TokenRegex.EndCG.IsMatch(line))
                    {
                        if (state.SectionStack.Count == 0)
                            throw new InvalidOperationException("Unexpected end of HLSL/CG program");

                        isEndOfProgramSection = Section.Program == state.CurrentSection;
                        state.SectionStack.Pop();
                    }
                    break;
            }
            return state.CurrentSection;
        }

        static bool IsBracedSection(Section section) => section is not Section.Include and not Section.Program;

        static void TrackBraces(string line, ShaderPatcherState state)
        {
            if (!IsBracedSection(state.CurrentSection))
                return;

            MatchCollection braceMatches = TokenRegex.Braces.Matches(line);
            foreach (Match brace in braceMatches)
            {
                switch (brace.Value)
                {
                    case "{" when state.WaitingForSectionBrace:
                        // Reset the flag as the section is now associated with a brace
                        state.WaitingForSectionBrace = false;
                        break;
                    case "{":
                        // Found an unknown brace, push an unknown section onto the stack
                        state.SectionStack.Push(Section.Unknown);
                        break;
                    case "}" when state.SectionStack.Count > 0:
                        // End brace, pop the current section
                        state.SectionStack.Pop();
                        break;
                }
            }
        }

        static string CommentOutProceduralOption(string line)
        {
            string indent = new string(line.TakeWhile(char.IsWhiteSpace).ToArray());
            Match match = TokenRegex.PragmaInstancingOptions.Match(line);
            if (match.Success)
            {
                string before = match.Groups[1].Value.Trim();
                string after = match.Groups[2].Value.Trim();
                if (string.IsNullOrEmpty(before) && string.IsNullOrEmpty(after))
                {
                    return $"{indent}// {line.Trim()} /*(FLORA_SHADER_REPLACE_LINE)*/";
                }
                else
                {
                    return $"{indent}#pragma instancing_options {before} {after} /*(FLORA_SHADER_REMOVE_PROCEDURAL_FUNC)*/".Trim();
                }
            }
            return line;
        }

        static string CommentOutLine(string currentLine)
        {
            string indent = new string(currentLine.TakeWhile(char.IsWhiteSpace).ToArray());
            string trimmed = currentLine.TrimStart();
            return $"{indent}// {trimmed} /*(FLORA AUTO_SHADER REPLACED_LINE)*/";
        }

        static string AddFloraToName(Match match)
        {
            string str = match.Value;
            if (!str.Contains(PatchedShaderSuffix))
                str = str.Insert(str.LastIndexOf('"'), PatchedShaderSuffix);
            return str;
        }

        static string FixLineEndings(string sourceCode)
        {
            return sourceCode
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", Environment.NewLine);
        }
    }
}
