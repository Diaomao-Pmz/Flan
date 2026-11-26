using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 「开火时移动」参数的显示规则：弹幕卡和套路步骤共用。
/// 只是不显示，隐藏的值照样保存。
/// </summary>
internal static class FireMoveGUI
{
    private static readonly string[] KeepDistanceFields = { "distanceRange", "distanceBias", "speedMultiplier" };
    private static readonly string[] MoveThenHoldFields = { "direction", "moveDistance", "speedMultiplier" };
    private static readonly string[] NoFields = { };

    /// <summary>这种移动方式要显示 FireMoveSettings 里的哪些栏位</summary>
    public static string[] FieldsFor(FireMoveMode mode)
    {
        switch (mode)
        {
            case FireMoveMode.KeepDistance: return KeepDistanceFields;
            case FireMoveMode.MoveThenHold: return MoveThenHoldFields;
            default: return NoFields;
        }
    }

    /// <summary>把要显示的子栏位收进 output（顺序就是显示顺序）</summary>
    public static void Collect(SerializedProperty settings, FireMoveMode mode, List<SerializedProperty> output)
    {
        if (settings == null) return;
        foreach (string name in FieldsFor(mode))
        {
            SerializedProperty p = settings.FindPropertyRelative(name);
            if (p != null) output.Add(p);
        }
    }
}

/// <summary>
/// 「脱手释放」参数的显示：弹幕卡和套路步骤共用。
/// </summary>
internal static class ReleaseGUI
{
    private static readonly string[] Fields = { "beforeEnd", "holdTime", "allowDash" };

    /// <summary>把 ReleaseSettings 的子栏位收进 output（顺序就是显示顺序）</summary>
    public static void Collect(SerializedProperty settings, List<SerializedProperty> output)
    {
        if (settings == null) return;
        foreach (string name in Fields)
        {
            SerializedProperty p = settings.FindPropertyRelative(name);
            if (p != null) output.Add(p);
        }
    }
}

/// <summary>
/// 弹幕卡的 Inspector：开火时移动只显示当前方式用得上的参数；
/// 没勾「允许脱手释放」时不显示脱手参数；没勾「允许近身反应」时不显示「反应后取回」
/// （只是不显示，值照样保存）
/// </summary>
[CustomEditor(typeof(BulletNode))]
public class BulletNodeEditor : Editor
{
    private readonly List<SerializedProperty> fireFields = new List<SerializedProperty>();

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var mode = (FireMoveMode)serializedObject.FindProperty("fireMoveMode").enumValueIndex;
        bool allowRelease = serializedObject.FindProperty("allowRelease").boolValue;
        bool allowReaction = serializedObject.FindProperty("allowCloseReaction").boolValue;

        SerializedProperty it = serializedObject.GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (it.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(it);
                continue;
            }

            if (it.propertyPath == "fireMove")
            {
                fireFields.Clear();
                FireMoveGUI.Collect(it, mode, fireFields);
                EditorGUI.indentLevel++;
                foreach (SerializedProperty p in fireFields) EditorGUILayout.PropertyField(p, true);
                EditorGUI.indentLevel--;
                continue;
            }

            if (it.propertyPath == "release")
            {
                if (!allowRelease) continue;
                fireFields.Clear();
                ReleaseGUI.Collect(it, fireFields);
                EditorGUI.indentLevel++;
                foreach (SerializedProperty p in fireFields) EditorGUILayout.PropertyField(p, true);
                EditorGUI.indentLevel--;
                continue;
            }

            if (it.propertyPath == "reclaimAfterReaction" && !allowReaction) continue;

            EditorGUILayout.PropertyField(it, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
