// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Linq;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering
{
    static class DebugUIExt
    {
        public class ValueTuple : DebugUI.Widget
        {
            public int NumElements
            {
                get
                {
                    Assert.IsTrue(Values.Length > 0);
                    return Values.Length;
                }
            }
            
            public DebugUI.Value[] Values;
            public string FormatString;
            public float RefreshRate => Values.FirstOrDefault()?.refreshRate ?? 0.1f;
            public int PinnedElementIndex = -1;
            
            public string Format(object value) => string.IsNullOrEmpty(FormatString) ? $"{value}" : string.Format(FormatString, value);
        }
    }
}
