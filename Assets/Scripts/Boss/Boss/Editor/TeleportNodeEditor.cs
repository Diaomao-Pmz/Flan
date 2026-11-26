using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 传送卡的 Inspector：按落点类型只显示用得上的落点参数；没勾 React To Hit 时收起受击相关的栏位。
/// 只是不显示，隐藏的值照样保存。
/// </summary>
[CustomEditor(typeof(TeleportNode))]
public class TeleportNodeEditor : Editor
{
    // 绕背 / 意图距离都用
    private static readonly HashSet<string> SnapshotFields = new HashSet<string> { "snapshotPlayerPosition" };

    // 只有绕背用
    private static readonly HashSet<string> BehindFields = new HashSet<string> { "behindDistance", "matchPlayerHeight" };

    // 只有意图距离用
    private static readonly HashSet<string> IntentFields = new HashSet<string> { "landingDistance" };

    // 勾了 React To Hit 才显示
    private static readonly HashSet<string> ReactFields = new HashSet<string>
    {
        "reactMode", "reactDamageType", "baseChance", "chanceGainPerHit", "chanceResetAfter", "interruptAction",
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var type = (TeleportTargetType)serializedObject.FindProperty("targetType").enumValueIndex;
        bool react = serializedObject.FindProperty("reactToHit").boolValue;

        bool behind = type == TeleportTargetType.BehindPlayer;
        bool intent = type == TeleportTargetType.IntentDistance;

        SerializedProperty it = serializedObject.GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;
            string path = it.propertyPath;

            if (path == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(it);
                continue;
            }

            if (SnapshotFields.Contains(path) && !behind && !intent) continue;
            if (BehindFields.Contains(path) && !behind) continue;
            if (IntentFields.Contains(path) && !intent) continue;
            if (ReactFields.Contains(path) && !react) continue;

            EditorGUILayout.PropertyField(it, true);
        }

        if (type == TeleportTargetType.RandomPoint || type == TeleportTargetType.Center)
        {
            EditorGUILayout.HelpBox(
                type == TeleportTargetType.RandomPoint
                    ? "随机点的传送点列表在 Boss 身上的 BAE_Teleporter 里配（一阶段 / 二阶段各一组）。"
                    : "中央锚点在 Boss 身上的 BAE_Teleporter 里配。",
                MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
