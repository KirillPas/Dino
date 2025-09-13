// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    readonly struct HandlesColorScope : IDisposable
    {
        readonly Color m_PrevColor;

        public HandlesColorScope(Color color)
        {
            m_PrevColor = Handles.color;
            Handles.color = color;
        }

        public void Dispose()
        {
            Handles.color = m_PrevColor;
        }
    }

    static class InstanceHandlesUtility
    {
        public static float DistanceToCircle(Vector3 point, float radius)
        {
            Vector3 screenPos = HandleUtility.WorldToGUIPointWithDepth(point);
            return screenPos.z < 0 ? float.MaxValue : Mathf.Max(0, Vector2.Distance(screenPos, Event.current.mousePosition) - radius);
        }

        public static bool PlaceObject(Vector2 guiPosition, out Vector3 position, out Vector3 normal, out int colliderInstanceID)
        {
            if (PlaceObject(guiPosition, out RaycastHit hit))
            {
                position = hit.point;
                normal = hit.normal;
                colliderInstanceID = hit.collider.GetInstanceID();
                return true;
            }
            
            position = Vector3.zero;
            normal = Vector3.up;
            colliderInstanceID = 0;
            return false;
        }

        public static bool PlaceObject(Vector2 guiPosition, out RaycastHit hit)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);
            bool positionFound = TryRaySnap(ray, out hit) || HandleUtilityBridge.TryPlaceOnGrid(ray, out hit);
            hit.point = positionFound ? ray.GetPoint(hit.distance) : Vector3.zero;
            hit.normal = positionFound ? hit.normal : Vector3.up;
            return positionFound;
        }
        
        static RaycastHit[] s_RaySnapHits = new RaycastHit[100];

        static bool TryRaySnap(Ray ray, out RaycastHit resultHit)
        {
            object result = HandleUtility.RaySnap(ray);
            if (result == null)
            {
                resultHit = default;
                resultHit.distance = Mathf.Infinity;
                return false;
            }

            resultHit = (RaycastHit)result;
            return true;
        }
        
        const int k_MaxDecimals = 15;

        public static float RoundBasedOnMinimumDifference(float valueToRound, float minDifference)
        {
            if (minDifference == 0)
                return DiscardLeastSignificantDecimal(valueToRound);
            return (float)System.Math.Round(valueToRound, GetNumberOfDecimalsForMinimumDifference(minDifference), System.MidpointRounding.AwayFromZero);
        }

        public static int GetNumberOfDecimalsForMinimumDifference(float minDifference)
        {
            return Mathf.Clamp(-Mathf.FloorToInt(Mathf.Log10(Mathf.Abs(minDifference))), 0, k_MaxDecimals);
        }

        public static float DiscardLeastSignificantDecimal(float v)
        {
            int decimals = Mathf.Clamp((int)(5 - Mathf.Log10(Mathf.Abs(v))), 0, k_MaxDecimals);
            return (float)System.Math.Round(v, decimals, System.MidpointRounding.AwayFromZero);
        }
    }
}