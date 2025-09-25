// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using MA.Core;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MA.Flora.Rendering
{
    static class RenderSelectionUtility
    {
        [Conditional("UNITY_EDITOR"),MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void RenderSceneSelection(InstancingContext ctx, Camera camera)
        {
#if UNITY_EDITOR
            if (camera.cameraType is CameraType.SceneView)
            {
                if (!ctx.CameraManager.TryGetInstancedCamera(camera, out InstancedCameraID cameraID))
                    return;

                bool hasSelection = ctx.RendererManager.SelectedContainers.Count > 0;
                if (hasSelection)
                {
                    CommandBuffer cmd = CommandBufferPool.Get();
                    {
                        DrawSelectionOutline(ctx, cmd, camera, cameraID);
                        Graphics.ExecuteCommandBuffer(cmd);
                    }
                    CommandBufferPool.Release(cmd);
                }
            }
#endif
        }

#if UNITY_EDITOR
        enum SelectionPasses
        {
            Picking,       // Picking
            SelectedAll,   // Render selected, always pass z-test
            SelectedFront, // Render selected, front pass z-test
            PostProcess,   // Post
            Blur,          // Blur
            CompareID,     // Compare object ids
        }

        // Built-in passes
        const string k_ScenePickingPassName   = "ScenePickingPass";
        const string k_SceneSelectionPassName = "SceneSelectionPass";

        // Profiler markers
        static readonly ProfilerMarker k_SelectionOutline     = new ProfilerMarker("Flora.SelectionOutline");
        static readonly ProfilerMarker k_SelectionFront       = new ProfilerMarker("Flora.SelectionOutline.RenderFront");
        static readonly ProfilerMarker k_SelectionBack        = new ProfilerMarker("Flora.SelectionOutline.RenderBack");
        static readonly ProfilerMarker k_SelectionPostProcess = new ProfilerMarker("Flora.SelectionOutline.PostProcess");

        // Unity shader variables
        static readonly int _ScreenSize               = Shader.PropertyToID("_ScreenSize");
        static readonly int _Cull                     = Shader.PropertyToID("_Cull");
        static readonly int _ZTest                    = Shader.PropertyToID("_ZTest");
        static readonly int _ZTestDepthEqualForOpaque = Shader.PropertyToID("_ZTestDepthEqualForOpaque");
        static readonly int _ZWrite                   = Shader.PropertyToID("_ZWrite");
        static readonly int _AlphaCutoffEnable        = Shader.PropertyToID("_AlphaCutoffEnable");
        static readonly int _ObjectId                 = Shader.PropertyToID("_ObjectId");
        static readonly int _PassValue                = Shader.PropertyToID("_PassValue");
        static readonly int unity_FogColor            = Shader.PropertyToID("unity_FogColor");

        // Flora shader variables
        static readonly int flora_DebugViewMode      = Shader.PropertyToID("flora_DebugViewMode");

        static Color s_OutlineColor = new Color(251 / 255f, 202 / 255f, 76 / 255f, 1.0f);
        static MaterialPropertyBlock[] s_SelectionPropertyBlocks = Array.Empty<MaterialPropertyBlock>();

        static Material s_SceneViewSelectedMaterial;
        static Material SelectionMaterial
        {
            get
            {
                if (s_SceneViewSelectedMaterial == null)
                    s_SceneViewSelectedMaterial = new Material(Shader.Find("Hidden/Flora/Selection"));

                return s_SceneViewSelectedMaterial;
            }
        }

        static InstanceRenderPipelineType ActiveRenderPipelineType
            => InstancingSystem.Instance.ActiveRenderPipelineType;

        static InstancingContext InstancingContext
            => InstancingSystem.Instance.Context;

        internal static void AddPickingPass(CommandBuffer cmd, InstancedCameraID cameraID, RenderTexture target, int targetWidth, int targetHeight)
        {
            InstancingContext ctx = InstancingContext;
            Camera camera = ctx.CameraManager.Cameras[cameraID];

            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.nonJitteredProjectionMatrix);
            cmd.SetGlobalVector(_ScreenSize, new Vector4(targetWidth, targetHeight, 1.0f / targetWidth, 1.0f / targetHeight));
            cmd.SetGlobalFloat(unity_FogColor, 0);
            cmd.SetGlobalInteger(flora_DebugViewMode, (int)DebugShaderOverrideMode.GlobalID);

            InstanceCuller pickingCuller = ctx.CameraManager.CullingContexts[cameraID];
            pickingCuller.BuildInstanceDrawsWithoutOcclusion(cmd);
            ctx.SceneData.SetGlobalBuffers(cmd);

            cmd.SetRenderTarget(target, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(true, true, Color.clear);

            DrawIndirectWithPass(ctx, cmd, pickingCuller.IndirectDrawCommands, pickingCuller.IndirectArgsBuffer, GetPickingPass);
            return;

            (Material material, int passIndex) GetPickingPass(Material m, MaterialPropertyBlock mpb)
            {
                int pickingPass = FindPickingPass(m);
                if (pickingPass >= 0)
                {
                    m.EnableKeyword("PROCEDURAL_INSTANCING_ON");
                    if (m.shaderKeywords.Contains("PROCEDURAL_INSTANCING_ON"))
                        return (m, pickingPass);
                }

                return (SelectionMaterial, 0);
            }
        }

        static readonly int k_Selection = Shader.PropertyToID("k_Selection");
        static readonly int k_SelectionPostA = Shader.PropertyToID("_SelectionColorA");
        static readonly int k_SelectionPostB = Shader.PropertyToID("_SelectionColorB");

        static readonly int k_OutlineColor = Shader.PropertyToID("_OutlineColor");
        static readonly int k_OutlineFade = Shader.PropertyToID("_OutlineFade");
        static readonly int k_BlurDirection = Shader.PropertyToID("_BlurDirection");

        static void DrawSelectionOutline(InstancingContext ctx, CommandBuffer cmd, Camera camera, InstancedCameraID cameraID)
        {
            RenderTextureDescriptor descriptor = camera.targetTexture.descriptor;
            int targetWidth = descriptor.width;
            int targetHeight = descriptor.height;
            RenderTexture target = camera.targetTexture;
            RenderBuffer depth = target.depthBuffer;

            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.nonJitteredProjectionMatrix);
            cmd.SetGlobalVector(_ScreenSize, new Vector4(targetWidth, targetHeight, 1.0f / targetWidth, 1.0f / targetHeight));

            cmd.BeginSample(k_SelectionOutline);
            InstanceCuller selectionCuller = ctx.CameraManager.CullingContexts[cameraID];
            selectionCuller.BuildInstanceDrawsWithoutOcclusion(cmd, InstanceCullingFlags.SelectionOnly);
            ctx.SceneData.SetGlobalBuffers(cmd);

            // RTs

            RenderTextureDescriptor selectionDescriptor = target.descriptor;
            selectionDescriptor.depthStencilFormat = GraphicsFormat.None;

            cmd.GetTemporaryRT(k_Selection, selectionDescriptor);
            cmd.GetTemporaryRT(k_SelectionPostA, selectionDescriptor);
            cmd.GetTemporaryRT(k_SelectionPostB, selectionDescriptor);

            // Draw always

            cmd.BeginSample(k_SelectionBack);
            cmd.SetRenderTarget(k_Selection, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(false, true, Color.clear);
            SetSelectionPassValues(cmd, 0, 0);
            DrawIndirectWithPass(ctx, cmd, selectionCuller.IndirectDrawCommands, selectionCuller.IndirectArgsBuffer,
                (m, mpb) => GetSelectionPass(m, mpb, SelectionPasses.SelectedAll));
            cmd.EndSample(k_SelectionBack);

            // Draw front

            cmd.BeginSample(k_SelectionFront);
            cmd.Blit(k_Selection, k_SelectionPostA);
            cmd.SetRenderTarget(k_Selection, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare);
            SetSelectionPassValues(cmd, 1, 1);
            DrawIndirectWithPass(ctx, cmd, selectionCuller.IndirectDrawCommands, selectionCuller.IndirectArgsBuffer,
                (m, mpb) => GetSelectionPass(m, mpb, SelectionPasses.SelectedFront));
            cmd.EndSample(k_SelectionFront);

            // Post process

            cmd.BeginSample(k_SelectionPostProcess);
            cmd.Blit(k_Selection, k_SelectionPostA, SelectionMaterial, (int)SelectionPasses.CompareID);

            cmd.SetGlobalVector(k_BlurDirection, new Vector2(1.0f, 0.0f));
            cmd.Blit(k_SelectionPostA, k_SelectionPostB, SelectionMaterial, (int)SelectionPasses.Blur);

            cmd.SetGlobalVector(k_BlurDirection, new Vector2(0.0f, 1.0f));
            cmd.Blit(k_SelectionPostB, k_SelectionPostA, SelectionMaterial, (int)SelectionPasses.Blur);

            cmd.SetGlobalColor(k_OutlineColor, s_OutlineColor.WithAlpha(0).linear);
            cmd.SetGlobalFloat(k_OutlineFade, 1.0f);
            cmd.Blit(k_SelectionPostA, target, SelectionMaterial, (int)SelectionPasses.PostProcess);
            cmd.EndSample(k_SelectionPostProcess);

            // Cleanup

            cmd.ReleaseTemporaryRT(k_Selection);
            cmd.ReleaseTemporaryRT(k_SelectionPostA);
            cmd.ReleaseTemporaryRT(k_SelectionPostA);
            cmd.EndSample(k_SelectionOutline);
            return;

            (Material material, int passIndex) GetSelectionPass(Material m, MaterialPropertyBlock mpb, SelectionPasses pass)
            {
                int selectionPassIndex = FindSelectionPass(m);
                if (selectionPassIndex >= 0)
                {
                    m.SetFloat(_ZWrite, 0);
                    m.SetFloat(_Cull, (float)UnityEngine.Rendering.CullMode.Off);

                    return (m, selectionPassIndex);
                }

                return (SelectionMaterial, (int)pass);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SetSelectionPassValues(CommandBuffer cmd, int objectId, int passValue)
        {
#if UNITY_2022_3_OR_NEWER
            cmd.SetGlobalInteger(_ObjectId, objectId);
            cmd.SetGlobalInteger(_PassValue, passValue);
#else
            cmd.SetGlobalFloat(_ObjectId, objectId);
            cmd.SetGlobalFloat(_PassValue, passValue);
#endif
        }

        delegate (Material material, int passIndex) GetReplacementPass(Material material, MaterialPropertyBlock mpb);

        static readonly int unity_IndirectDrawArgs = Shader.PropertyToID("unity_IndirectDrawArgs");
        static readonly int unity_BaseCommandID = Shader.PropertyToID("unity_BaseCommandID");

        static unsafe void DrawIndirectWithPass(InstancingContext ctx, CommandBuffer cmd, UnsafeList<IndirectDrawCommand> renderCommands, GraphicsBuffer indirectArgs, GetReplacementPass getReplacementPass)
        {
            if (indirectArgs == null)
                return;

            if (s_SelectionPropertyBlocks.Length < renderCommands.Length)
                s_SelectionPropertyBlocks = new MaterialPropertyBlock[renderCommands.Length];

            for (int drawCommandIndex = 0; drawCommandIndex < renderCommands.Length; drawCommandIndex++)
            {
                ref readonly IndirectDrawCommand indirectDrawCommand = ref renderCommands.Ptr[drawCommandIndex];
                if (indirectDrawCommand.CommandCount == 0 || indirectDrawCommand.FilterSettings.ShadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    continue;

                if (!ctx.MeshManager.TryGetMesh(indirectDrawCommand.MeshID, out Mesh mesh))
                {
                    Debug.LogError($"Failed to get mesh for MeshID: {indirectDrawCommand.MeshID}");
                    continue;
                }

                if (!ctx.MaterialManager.TryGetEditorMaterialVariant(indirectDrawCommand.MaterialID, indirectDrawCommand.MaterialVariant, false, out Material material))
                {
                    Debug.LogError($"Failed to get material for MaterialID: {indirectDrawCommand.MaterialID}");
                    continue;
                }

                MaterialPropertyBlock mpb = s_SelectionPropertyBlocks[drawCommandIndex] ?? (s_SelectionPropertyBlocks[drawCommandIndex] = new MaterialPropertyBlock());
                (Material replacementMaterial, int replacementPassIndex) = getReplacementPass(material, mpb);
                if (!replacementMaterial || replacementPassIndex >= replacementMaterial.passCount)
                    continue;

                ctx.SceneData.SetBuiltinPropertyMetadata(indirectDrawCommand.BatchID, mpb);

                cmd.SetGlobalBuffer(unity_IndirectDrawArgs, indirectArgs);

                for (int subCommandIndex = 0; subCommandIndex < indirectDrawCommand.CommandCount; ++subCommandIndex)
                {
                    int commandIndex = indirectDrawCommand.CommandIndex + subCommandIndex;
                    int argsOffset = commandIndex * GraphicsBuffer.IndirectDrawIndexedArgs.size;
                    cmd.SetGlobalInt(unity_BaseCommandID, commandIndex);
                    cmd.DrawMeshInstancedIndirect(mesh, indirectDrawCommand.SubMeshIndex, replacementMaterial, replacementPassIndex, indirectArgs, argsOffset, mpb);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int FindPickingPass(Material material)
        {
            int passCount = material.passCount;
            for (int i = 0; i < passCount; i++)
            {
                if (material.GetPassName(i).Contains("ScenePicking", StringComparison.InvariantCultureIgnoreCase))
                    return i;
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int FindSelectionPass(Material material)
        {
            int passCount = material.passCount;
            for (int i = 0; i < passCount; i++)
            {
                if (material.GetPassName(i).Contains("SceneSelection", StringComparison.InvariantCultureIgnoreCase))
                    return i;
            }

            return -1;
        }
#endif
    }
}
