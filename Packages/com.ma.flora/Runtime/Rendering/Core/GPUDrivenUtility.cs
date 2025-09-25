// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

#if UNITY_2023_3_OR_NEWER && FLORA_ENABLE_EXPERIMENTAL_GPU_DRIVEN_OCCLUSION_INTEGRATION
using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using MA.Flora.Rendering.Occlusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace MA.Flora.Rendering
{
    static class GPUDrivenUtility
    {
        // GPUResidentDrawer
        static GPUResidentDrawer s_Instance;
        static PropertyInfo s_GPUResidentDrawer_instanceProperty;
        delegate GPUResidentDrawer GetInstanceDelegate();
        static GetInstanceDelegate s_GPUResidentDrawer_GetInstance;
        
        // GPUResidentBatcher
        static PropertyInfo s_GPUResidentDrawer_batcherProperty;
        delegate object GetBatcherDelegate(GPUResidentDrawer drawer);
        static GetBatcherDelegate s_GPUResidentDrawer_GetBatcher;
        
        static PropertyInfo s_GPUResidentBatcher_occlusionCullingCommonProperty;
        delegate object GetOcclusionCullingCommonDelegate(object batcher);
        
        // OcclusionCullingCommon
        delegate bool TryGetOccluderHandlesDelegate(object occlusionCullingCommon, RenderGraph renderGraph, int viewInstanceID, ref OccluderHandlesRenderGraph occluderHandles);
        static TryGetOccluderHandlesDelegate s_TryGetOccluderHandles;
        
        static bool s_IsInitialized;
        const BindingFlags k_AllFlags = BindingFlags.Public 
                                        | BindingFlags.NonPublic
                                        | BindingFlags.Instance
                                        | BindingFlags.Static
                                        | BindingFlags.GetField
                                        | BindingFlags.SetField
                                        | BindingFlags.GetProperty
                                        | BindingFlags.SetProperty;

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeGPUDrivenReflection()
        {
            try
            {
                Assembly assembly = typeof(GPUResidentDrawer).Assembly;
                s_IsInitialized = true;
                
                s_GPUResidentDrawer_instanceProperty = typeof(GPUResidentDrawer).GetProperty("instance", k_AllFlags)!;
                s_GPUResidentDrawer_GetInstance = (GetInstanceDelegate)s_GPUResidentDrawer_instanceProperty
                    .GetGetMethod(true).CreateDelegate(typeof(GetInstanceDelegate), null);
                
                s_GPUResidentDrawer_batcherProperty = typeof(GPUResidentDrawer).GetProperty("batcher", k_AllFlags)!;
                s_GPUResidentDrawer_GetBatcher = (GetBatcherDelegate)s_GPUResidentDrawer_batcherProperty
                    .GetGetMethod(true).CreateDelegate(typeof(GetBatcherDelegate), null);
                
                Type gpuResidentBatcherType = assembly.GetType("UnityEngine.Rendering.GPUResidentBatcher");
                s_GPUResidentBatcher_occlusionCullingCommonProperty = gpuResidentBatcherType.GetProperty("occlusionCullingCommon", k_AllFlags)!;
                
                Type occlusionCullingCommonType = assembly.GetType("UnityEngine.Rendering.OcclusionCullingCommon");
                Type occluderContextType = assembly.GetType("UnityEngine.Rendering.OccluderContext");
                Type occluderHandlesType = assembly.GetType("UnityEngine.Rendering.OccluderHandles");

                // Get the methods
                MethodInfo getOccluderContextMethod = occlusionCullingCommonType.GetMethod("GetOccluderContext", k_AllFlags)!;
                MethodInfo importMethod = occluderContextType.GetMethod("Import", k_AllFlags)!;
                MethodInfo isValidMethod = occluderHandlesType.GetMethod("IsValid", k_AllFlags)!;

                // Define the parameters for the delegate
                var occlusionCullingCommonParameter = Expression.Parameter(typeof(object), "occlusionCullingCommon");
                var renderGraphParameter = Expression.Parameter(typeof(RenderGraph), "renderGraph");
                var viewInstanceIDParameter = Expression.Parameter(typeof(int), "viewInstanceID");
                var occluderHandlesPublicParameter = Expression.Parameter(typeof(OccluderHandlesRenderGraph).MakeByRefType(), "occluderHandles");

                // Convert the instance parameter to the correct type
                var castOcclusionCullingCommon = Expression.Convert(occlusionCullingCommonParameter, occlusionCullingCommonType);

                // Declare variables to hold intermediate results
                var occluderContextVar = Expression.Variable(occluderContextType, "occluderContext");
                var occluderHandlesVar = Expression.Variable(occluderHandlesType, "occluderHandlesInternal");
                var resultVar = Expression.Variable(typeof(bool), "result");

                // Create expressions for each step in the logic
                var getOccluderContextCall = Expression.Call(castOcclusionCullingCommon, getOccluderContextMethod, viewInstanceIDParameter, occluderContextVar);
                var importCall = Expression.Call(occluderContextVar, importMethod, renderGraphParameter);
                var isValidCall = Expression.Call(occluderHandlesVar, isValidMethod);

                var assignOccluderHandles = Expression.Assign(occluderHandlesVar, importCall);

                // Assign fields to the public struct
                var assignOccluderDepthPyramid = Expression.Assign(
                    Expression.Field(occluderHandlesPublicParameter, nameof(OccluderHandlesRenderGraph.OccluderDepthPyramid)),
                    Expression.Field(occluderHandlesVar, "occluderDepthPyramid"));
                var assignOcclusionDebugOverlay = Expression.Assign(
                    Expression.Field(occluderHandlesPublicParameter, nameof(OccluderHandlesRenderGraph.OcclusionDebugOverlay)), 
                    Expression.Field(occluderHandlesVar, "occlusionDebugOverlay"));

                // Create the conditional block
                var conditionalBlock = Expression.Block(
                    new[] { occluderContextVar, occluderHandlesVar },
                    Expression.IfThenElse(
                        getOccluderContextCall,
                        Expression.Block(
                            assignOccluderHandles,
                            Expression.IfThenElse(
                                isValidCall,
                                Expression.Block(
                                    assignOccluderDepthPyramid,
                                    assignOcclusionDebugOverlay,
                                    Expression.Assign(resultVar, Expression.Constant(true))
                                ),
                                Expression.Assign(resultVar, Expression.Constant(false))
                            )
                        ),
                        Expression.Assign(resultVar, Expression.Constant(false))
                    )
                );

                // Create the final block
                var finalBlock = Expression.Block(
                    new[] { resultVar },
                    conditionalBlock,
                    resultVar
                );

                // Create the lambda expression and compile it
                var lambda = Expression.Lambda<TryGetOccluderHandlesDelegate>(
                    finalBlock,
                    occlusionCullingCommonParameter,
                    renderGraphParameter,
                    viewInstanceIDParameter,
                    occluderHandlesPublicParameter
                );

                s_TryGetOccluderHandles = lambda.Compile();
            }
            catch (Exception e)
            {
                s_IsInitialized = false;
                Debug.Log("GPUDrivenUtility: Failed to get GPUResidentDrawer properties via reflection. " +
                          "This is likely be due to a change in the internal API. " +
                          "Integration disabled.");
                Debug.LogException(e);
            }
        }
        
        static GPUResidentDrawer instance
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => s_GPUResidentDrawer_GetInstance();
        }

        public static bool TryGetOccluderHandles(RenderGraph renderGraph, int viewInstanceID, out OccluderHandlesRenderGraph occluderHandles)
        {
            occluderHandles = default;
            
            if (!s_IsInitialized) return false;
            
            GPUResidentDrawer drawer = instance;
            if (drawer == null) return false;
            
            object batcher = s_GPUResidentDrawer_GetBatcher(drawer);
            if (batcher == null) return false;
            
            object occlusionCullingCommon = s_GPUResidentBatcher_occlusionCullingCommonProperty.GetValue(batcher);
            if (occlusionCullingCommon == null) return false;
            
            return s_TryGetOccluderHandles(occlusionCullingCommon, renderGraph, viewInstanceID, ref occluderHandles);
        }
    }
}
#endif