// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using MA.Mathematics;

namespace MA.Flora
{
    sealed class FoliageTreeManager : FoliageManager<FoliageTreeProvider> { }
    sealed class FoliageDetailManager : FoliageManager<FoliageDetailProvider> { }

    abstract class FoliageManager<TProvider> : IDisposable
        where TProvider : IFoliageProvider
    {
        bool m_IsInitialized;
        TProvider m_Provider;
        FoliageScheduler m_Scheduler;
        FoliageLayer<TProvider>[] m_Layers = Array.Empty<FoliageLayer<TProvider>>();
        bool m_IsDirty;

        public void Initialize(TProvider provider, FoliageScheduler scheduler)
        {
            if (m_IsInitialized)
                return;

            m_IsInitialized = true;
            m_Provider = provider;
            m_Scheduler = scheduler;
            m_IsDirty = true;
        }

        public void Dispose()
        {
            if (!m_IsInitialized)
                return;

            m_IsInitialized = false;
            m_IsDirty = true;
            m_Provider.Dispose();

            foreach (FoliageLayer<TProvider> layer in m_Layers)
                layer.Dispose();

            m_Layers = Array.Empty<FoliageLayer<TProvider>>();
        }

        public void MarkDirty(bool forceUpdate = false)
        {
            if (m_IsDirty && !forceUpdate)
                return;

            m_IsDirty = true;

            foreach (FoliageLayer<TProvider> layer in m_Layers)
            {
                layer.MarkAllDirty();
                if (forceUpdate)
                    layer.ForceRebuild();
            }
        }

        public void MarkRegionDirty(AxisAlignedBox2D region)
        {
            foreach (FoliageLayer<TProvider> layer in m_Layers)
                layer.MarkRegionDirty(region);
        }

        public void MarkRenderStateDirty()
        {
            foreach (FoliageLayer<TProvider> layer in m_Layers)
                layer.MarkRenderStateDirty();
        }

        public void NextFrame(FoliageStreamingSource streamingSource)
        {
            if (m_IsDirty)
            {
                m_IsDirty = false;

                FoliageDataChangeFlags changeFlags = m_Provider.RefreshData();
                if (changeFlags != FoliageDataChangeFlags.None)
                {
                    if ((changeFlags & FoliageDataChangeFlags.Layers) != 0)
                    {
                        int oldLayerCount = m_Layers.Length;
                        int newLayerCount = m_Provider.LayerCount;

                        if (newLayerCount > oldLayerCount)
                        {
                            Array.Resize(ref m_Layers, newLayerCount);
                            for (int i = oldLayerCount; i < newLayerCount; ++i)
                                m_Layers[i] = new FoliageLayer<TProvider>(m_Provider, m_Scheduler, i);
                        }
                        else if (newLayerCount < oldLayerCount)
                        {
                            for (int i = newLayerCount; i < oldLayerCount; ++i)
                                m_Layers[i].Dispose();

                            Array.Resize(ref m_Layers, newLayerCount);
                        }

                        foreach (FoliageLayer<TProvider> foliageLayer in m_Layers)
                            foliageLayer.UpdatePrototype();
                    }

                    if ((changeFlags & FoliageDataChangeFlags.Size) != 0)
                    {
                        foreach (FoliageLayer<TProvider> foliageLayer in m_Layers)
                            foliageLayer.Resize(m_Provider.GridSize);
                    }

                    if ((changeFlags & FoliageDataChangeFlags.Force) != 0)
                    {
                        foreach (FoliageLayer<TProvider> foliageLayer in m_Layers)
                            foliageLayer.ForceRebuild();
                    }
                    else if ((changeFlags & FoliageDataChangeFlags.Dirty) != 0)
                    {
                        foreach (FoliageLayer<TProvider> foliageLayer in m_Layers)
                            foliageLayer.MarkAllDirty();
                    }
                    else if ((changeFlags & FoliageDataChangeFlags.Position) != 0)
                    {
                        foreach (FoliageLayer<TProvider> foliageLayer in m_Layers)
                            foliageLayer.MarkRenderStateDirty();
                    }
                }
            }

            for (var layerIndex = 0; layerIndex < m_Layers.Length; layerIndex++)
            {
                if (!m_Provider.IsLayerEnabled(layerIndex))
                    continue;

                m_Layers[layerIndex].NextFrame(streamingSource);
            }
        }
    }
}
