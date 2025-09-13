// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UI;
using UnityEngine.UI;

namespace MA.Flora.Rendering
{
    class DebugUIHandlerValueTuple : DebugUIHandlerWidget
    {
        public Text NameLabel;
        public Text ValueLabel;

        DebugUIExt.ValueTuple m_Field;
        Text[] m_ValueElements;
        float m_Timer;
        DebugUI.Widget m_PreviousWidget;

        const float k_XOffset = 230f;
        static readonly Color s_ZeroColor = Color.gray;

        protected override void OnEnable()
        {
            m_Timer = 0f;
        }

        public override bool OnSelection(bool fromNext, DebugUIHandlerWidget previous)
        {
            NameLabel.color = colorSelected;
            return true;
        }

        public override void OnDeselection()
        {
            NameLabel.color = colorDefault;
        }

        void Initialize()
        {
            if (m_Field == null || m_PreviousWidget != m_Widget)
            {
                m_PreviousWidget = m_Widget;
                m_Field = CastWidget<DebugUIExt.ValueTuple>();
                NameLabel.text = m_Field.displayName;

                Debug.Assert(m_Field.NumElements > 0);
                int numElements = m_Field.NumElements;
                m_ValueElements = new Text[numElements];
                m_ValueElements[0] = ValueLabel;

                float columnOffset = k_XOffset / (float)numElements;
                for (int index = 1; index < numElements; ++index)
                {
                    GameObject valueElement = Instantiate(ValueLabel.gameObject, transform);
                    valueElement.AddComponent<LayoutElement>().ignoreLayout = true;

                    RectTransform rectTransform = valueElement.transform as RectTransform;
                    rectTransform!.anchorMax = rectTransform.anchorMin = new Vector2(0, 1);
                    rectTransform!.sizeDelta = new Vector2(100, 26);

                    RectTransform originalTransform = NameLabel.transform as RectTransform;
                    Vector3 pos = originalTransform!.anchoredPosition;
                    pos.x += (index + 1) * columnOffset + 200f;

                    rectTransform!.anchoredPosition = pos;
                    rectTransform!.pivot = new Vector2(0, 1);

                    m_ValueElements[index] = valueElement.GetComponent<Text>();
                }
            }
        }

        void UpdateValueLabels()
        {
            for (int index = 0; index < m_Field.NumElements; ++index)
            {
                if (index < m_ValueElements.Length && m_ValueElements[index] != null)
                {
                    object value = m_Field.Values[index].GetValue();
                    m_ValueElements[index].text = m_Field.Format(value);
                    // De-emphasize zero values by switching to dark gray color
                    if (value is float f)
                        m_ValueElements[index].color = f == 0f ? s_ZeroColor : colorDefault;
                }
            }
        }

        void Update()
        {
            Initialize();

            if (m_Field != null && m_Timer >= m_Field.RefreshRate)
            {
                UpdateValueLabels();
                m_Timer -= m_Field.RefreshRate;
            }

            m_Timer += Time.deltaTime;
        }
    }
}
