#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Flandre.CombatSystem.EditorTools
{
    /// <summary>
    /// 特效表里每一条的显示方式：
    /// - 标题直接写出"用途 · 预制体 → 给谁用"，折叠着也能看出是哪条；
    /// - 选"招式特效"只显示招式相关的栏位，选"蓄力光效"只显示武器和等级；
    /// - 没拖预制体、Scale 有 0 时给出提示。
    /// </summary>
    [CustomPropertyDrawer(typeof(EffectManager.EffectEntry))]
    public class EffectEntryDrawer : PropertyDrawer
    {
        private const float Gap = 2f;
        private const float HelpHeight = 30f;

        private static float Line => EditorGUIUtility.singleLineHeight;

        private static readonly List<SerializedProperty> fields = new List<SerializedProperty>();
        private static readonly List<string> warnings = new List<string>();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = Line;
            if (!property.isExpanded) return h;

            Collect(property);
            for (int i = 0; i < fields.Count; i++) h += EditorGUI.GetPropertyHeight(fields[i], true) + Gap;
            h += warnings.Count * (HelpHeight + Gap);
            return h + Gap;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect r = new Rect(position.x, position.y, position.width, Line);
            property.isExpanded = EditorGUI.Foldout(r, property.isExpanded, Title(property, label), true);

            if (property.isExpanded)
            {
                Collect(property);
                EditorGUI.indentLevel++;
                r.y += Line + Gap;

                for (int i = 0; i < warnings.Count; i++)
                {
                    Rect hr = EditorGUI.IndentedRect(new Rect(r.x, r.y, r.width, HelpHeight));
                    EditorGUI.HelpBox(hr, warnings[i], MessageType.Warning);
                    r.y += HelpHeight + Gap;
                }

                for (int i = 0; i < fields.Count; i++)
                {
                    float fh = EditorGUI.GetPropertyHeight(fields[i], true);
                    EditorGUI.PropertyField(new Rect(r.x, r.y, r.width, fh), fields[i], true);
                    r.y += fh + Gap;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        /// <summary>按用途挑出要显示的栏位和要提示的问题</summary>
        private static void Collect(SerializedProperty property)
        {
            fields.Clear();
            warnings.Clear();

            SerializedProperty prefab = property.FindPropertyRelative("prefab");
            SerializedProperty s = property.FindPropertyRelative("settings");
            SerializedProperty usage = s.FindPropertyRelative("usage");
            bool isCharge = usage.enumValueIndex == (int)EffectUsage.Charge;

            fields.Add(prefab);
            fields.Add(usage);

            if (isCharge)
            {
                fields.Add(s.FindPropertyRelative("weapons"));
                fields.Add(s.FindPropertyRelative("chargeLevel"));
                fields.Add(s.FindPropertyRelative("offset"));
            }
            else
            {
                fields.Add(s.FindPropertyRelative("nodes"));
                fields.Add(s.FindPropertyRelative("key"));
                SerializedProperty mode = s.FindPropertyRelative("lifetimeMode");
                fields.Add(mode);
                // Fixed = 播完一遍，原速播放，不需要倍速适配
                if (mode.enumValueIndex != (int)EffectLifetimeMode.Fixed)
                    fields.Add(s.FindPropertyRelative("fitDuration"));

                SerializedProperty useHitbox = s.FindPropertyRelative("useHitboxOffset");
                fields.Add(useHitbox);
                if (!useHitbox.boolValue) fields.Add(s.FindPropertyRelative("offset"));
            }

            fields.Add(s.FindPropertyRelative("scale"));
            fields.Add(s.FindPropertyRelative("rotation"));

            if (prefab.objectReferenceValue == null)
                warnings.Add("没拖预制体：这一条不会生效");

            Vector2 scale = s.FindPropertyRelative("scale").vector2Value;
            if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f))
                warnings.Add("Scale 有 0，特效会看不见。一般填 (1, 1)");
        }

        /// <summary>标题：[招式] FX_A1 → A1, A2 / [蓄力 2 级] FXL_AA2 → MainWeapon</summary>
        private static GUIContent Title(SerializedProperty property, GUIContent label)
        {
            SerializedProperty prefab = property.FindPropertyRelative("prefab");
            SerializedProperty s = property.FindPropertyRelative("settings");
            bool isCharge = s.FindPropertyRelative("usage").enumValueIndex == (int)EffectUsage.Charge;

            string prefabName = prefab.objectReferenceValue != null ? prefab.objectReferenceValue.name : "（没拖预制体）";

            string tag;
            SerializedProperty targets;
            if (isCharge)
            {
                tag = $"[蓄力 {s.FindPropertyRelative("chargeLevel").intValue} 级]";
                targets = s.FindPropertyRelative("weapons");
            }
            else
            {
                tag = "[招式]";
                targets = s.FindPropertyRelative("nodes");
            }

            var names = new List<string>();
            for (int i = 0; i < targets.arraySize && names.Count < 4; i++)
            {
                Object o = targets.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null) names.Add(o.name);
            }
            if (targets.arraySize > 4) names.Add("…");

            if (!isCharge)
            {
                string key = s.FindPropertyRelative("key").stringValue;
                if (!string.IsNullOrEmpty(key)) names.Add($"Key: {key}");
            }

            string who = names.Count > 0 ? string.Join(", ", names) : "（没指定给谁用）";
            return new GUIContent($"{tag} {prefabName} → {who}", label.tooltip);
        }
    }
}
#endif
