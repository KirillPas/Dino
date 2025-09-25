// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora.Rendering
{
    class DebugSampleHistory
    {
        public enum SampleType
        {
            Average,
            Min,
            Max
        }

        public DebugSampleHistory(int initialCapacity)
        {
            m_Samples.Capacity = initialCapacity;
        }

        List<double> m_Samples = new();

        public double SampleAverage;
        public double SampleMin;
        public double SampleMax;

        public void Add(double sample)
        {
            m_Samples.Add(sample);
        }

        public double GetSample(SampleType type)
        {
            return type switch
            {
                SampleType.Average => SampleAverage,
                SampleType.Min     => SampleMin,
                SampleType.Max     => SampleMax,
                _                  => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        public void ComputeAggregateValues()
        {
            double average = 0;
            double min = double.MaxValue;
            double max = double.MinValue;
            int numValidSamples = 0; // Using the struct to record how many valid samples each field has

            for (int i = 0; i < m_Samples.Count; i++)
            {
                double s = m_Samples[i];
                min = math.min(min, s);
                max = math.max(max, s);
                average += s;
                numValidSamples = s > 0.0f ? numValidSamples + 1 : numValidSamples;
            }

            min = numValidSamples > 0 ? min : 0.0f;
            max = numValidSamples > 0 ? max : 0.0f;
            average = numValidSamples > 0 ? average / numValidSamples : 0.0f;

            SampleAverage = average;
            SampleMin = min;
            SampleMax = max;
        }

        public void DiscardOldSamples(int sampleHistorySize)
        {
            Debug.Assert(sampleHistorySize > 0, "Invalid sampleHistorySize");

            while (m_Samples.Count >= sampleHistorySize)
                m_Samples.RemoveAt(0);

            m_Samples.Capacity = sampleHistorySize;
        }

        public void Clear()
        {
            m_Samples.Clear();
        }
    }
}