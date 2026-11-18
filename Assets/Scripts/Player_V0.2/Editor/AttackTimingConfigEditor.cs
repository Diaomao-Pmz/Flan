#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Flandre.CombatSystem.EditorTools
{
    /// <summary>
    /// 节拍总表的面板。每个招式一块：
    ///   第一行：招式名（固定招式，直接显示）/ 招式选框（双蓄槽）
    ///   第二行：Combo Window（双蓄没有）
    ///   第三行：三段时长 标题
    ///   四~六行：前摇 / 攻击 / 后摇
    ///   之后：动画与表现（动画剪辑 / 出招位移 / 锁移动）、蓄力位移（勾上才展开）
    /// </summary>
    [CustomEditor(typeof(AttackTimingConfig))]
    public class AttackTimingConfigEditor : Editor
    {
        private const string PhaseHeader = "三段时长 (秒 · 填 -1 = 跟动画事件走)";
        private const string AnimHeader = "动画与表现";

        private GUIStyle titleStyle;
        private GUIStyle groupStyle;

        private void OnEnable()
        {
            var cfg = (AttackTimingConfig)target;
            if (cfg.AutoBind())
            {
                EditorUtility.SetDirty(cfg);
            }
        }

        public override void OnInspectorGUI()
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
                groupStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
            }

            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "【节拍总表】所有招式的连段窗口、三段时长、动画与表现、蓄力位移都在这里调，ComboNode 上已经没有这些栏位了。\n" +
                "表里没有的招式没有动画，也打不出判定。\n" +
                "记得把这张表拖到角色身上 PlayerState 的 Attack Timing Config 栏位。",
                MessageType.None);

            var duplicates = FindDuplicates();

            DrawFixedGroup("平a", serializedObject.FindProperty("normalAttacks"), duplicates);
            DrawFixedGroup("蓄力", serializedObject.FindProperty("chargeAttacks"), duplicates);
            DrawDualGroup("双蓄（未定 · 把要复用的招式拖进选框）", serializedObject.FindProperty("dualAttacks"), duplicates);

            serializedObject.ApplyModifiedProperties();
        }

        // ==========================================================

        private void DrawFixedGroup(string title, SerializedProperty list, HashSet<Object> duplicates)
        {
            GroupTitle(title);

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                string slotName = e.FindPropertyRelative("slotName").stringValue;
                Object node = e.FindPropertyRelative("node").objectReferenceValue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // 第一行：招式名
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(slotName, titleStyle);
                using (new EditorGUI.DisabledScope(node == null))
                {
                    if (GUILayout.Button("定位", GUILayout.Width(40))) EditorGUIUtility.PingObject(node);
                }
                EditorGUILayout.EndHorizontal();

                if (node == null)
                    EditorGUILayout.HelpBox($"没找到名叫「{slotName}」的招式资产（ComboNode），这一行不会生效。", MessageType.Warning);
                else if (duplicates.Contains(node))
                    EditorGUILayout.HelpBox("这个招式在表里出现了不止一次，以排在前面的为准。", MessageType.Warning);

                // 第二行：连段窗口
                EditorGUILayout.PropertyField(e.FindPropertyRelative("comboWindow"));

                // 第三~六行：三段时长
                DrawPhases(e);

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawDualGroup(string title, SerializedProperty list, HashSet<Object> duplicates)
        {
            GroupTitle(title);

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                SerializedProperty nodeProp = e.FindPropertyRelative("node");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // 第一行：招式选框
                EditorGUILayout.PropertyField(nodeProp, new GUIContent($"双蓄 {i + 1}", "把要复用的招式（ComboNode）拖进来"));

                if (nodeProp.objectReferenceValue != null && duplicates.Contains(nodeProp.objectReferenceValue))
                    EditorGUILayout.HelpBox("这个招式在表里出现了不止一次，以排在前面的为准。", MessageType.Warning);

                // 双蓄是连段终点，没有连段窗口
                DrawPhases(e);

                EditorGUILayout.EndVertical();
            }
        }

        private static void DrawPhases(SerializedProperty e)
        {
            EditorGUILayout.LabelField(PhaseHeader, EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(e.FindPropertyRelative("windupOverride"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("activeOverride"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("recoveryOverride"));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(AnimHeader, EditorStyles.miniBoldLabel);
            SerializedProperty clip = e.FindPropertyRelative("attackClip");
            EditorGUILayout.PropertyField(clip);
            if (clip.objectReferenceValue == null && e.FindPropertyRelative("node").objectReferenceValue != null)
                EditorGUILayout.HelpBox("没有动画剪辑：这一招打不出判定 / 子弹 / 特效。", MessageType.Warning);
            EditorGUILayout.PropertyField(e.FindPropertyRelative("forwardThrust"), new GUIContent("Forward Thrust (距离)"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("thrustStart"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("thrustDuration"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("applyThrustInAir"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("lockMovementDuringAttack"));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("蓄力位移 (Aim Dash)", EditorStyles.miniBoldLabel);
            SerializedProperty aim = e.FindPropertyRelative("useAimDash");
            EditorGUILayout.PropertyField(aim);
            if (aim.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(e.FindPropertyRelative("aimDashMode"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("aimDashDistance"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("aimDashDuration"));
                EditorGUI.indentLevel--;
                if (e.FindPropertyRelative("forwardThrust").vector2Value.sqrMagnitude > 0.0001f)
                    EditorGUILayout.HelpBox("勾了蓄力位移，上面的 Forward Thrust 不会生效。", MessageType.Info);
            }
        }

        private void GroupTitle(string title)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(title, groupStyle);
        }

        /// <summary>在表里出现不止一次的招式</summary>
        private HashSet<Object> FindDuplicates()
        {
            var seen = new HashSet<Object>();
            var dup = new HashSet<Object>();
            foreach (string listName in new[] { "normalAttacks", "chargeAttacks", "dualAttacks" })
            {
                SerializedProperty list = serializedObject.FindProperty(listName);
                for (int i = 0; i < list.arraySize; i++)
                {
                    Object o = list.GetArrayElementAtIndex(i).FindPropertyRelative("node").objectReferenceValue;
                    if (o != null && !seen.Add(o)) dup.Add(o);
                }
            }
            return dup;
        }
    }
}
#endif
