using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 套路步骤的 Inspector：
///   · 这一步的招式和候补里都没有弹幕卡 → 不显示「开火时移动」「脱手释放」
///   · 开火时移动选了 Follow Card / Stand Still → 不显示移动参数；其余只显示当前方式用得上的
///   · 脱手释放只有选了 Release 才显示这一步自己的脱手参数
///   · 近身反应只有选了 On 才显示这一步自己的「反应后取回」
/// 只是不显示，隐藏的值照样保存。
/// </summary>
[CustomPropertyDrawer(typeof(RoutineStep))]
public class RoutineStepDrawer : PropertyDrawer
{
    private static readonly string[] HeadFields = { "action", "delayBefore", "whenOutOfRange" };

    private readonly List<SerializedProperty> visible = new List<SerializedProperty>();

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float h = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded) return h;

        CollectVisible(property);
        foreach (SerializedProperty p in visible)
            h += EditorGUI.GetPropertyHeight(p, true) + EditorGUIUtility.standardVerticalSpacing;
        return h;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Rect r = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(r, property.isExpanded, label, true);

        if (property.isExpanded)
        {
            CollectVisible(property);
            EditorGUI.indentLevel++;
            r.y += r.height + EditorGUIUtility.standardVerticalSpacing;
            foreach (SerializedProperty p in visible)
            {
                r.height = EditorGUI.GetPropertyHeight(p, true);
                EditorGUI.PropertyField(r, p, true);
                r.y += r.height + EditorGUIUtility.standardVerticalSpacing;
            }
            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private void CollectVisible(SerializedProperty step)
    {
        visible.Clear();
        foreach (string name in HeadFields)
        {
            SerializedProperty p = step.FindPropertyRelative(name);
            if (p != null) visible.Add(p);
        }

        if (HasBulletCard(step))
        {
            SerializedProperty modeProp = step.FindPropertyRelative("fireMoveMode");
            visible.Add(modeProp);

            var stepMode = (StepFireMoveMode)modeProp.enumValueIndex;
            if (stepMode == StepFireMoveMode.KeepDistance)
                FireMoveGUI.Collect(step.FindPropertyRelative("fireMove"), FireMoveMode.KeepDistance, visible);
            else if (stepMode == StepFireMoveMode.MoveThenHold)
                FireMoveGUI.Collect(step.FindPropertyRelative("fireMove"), FireMoveMode.MoveThenHold, visible);

            SerializedProperty releaseProp = step.FindPropertyRelative("releaseMode");
            visible.Add(releaseProp);
            if ((StepReleaseMode)releaseProp.enumValueIndex == StepReleaseMode.Release)
            {
                ReleaseGUI.Collect(step.FindPropertyRelative("release"), visible);
                SerializedProperty followProp = step.FindPropertyRelative("followBossWhenReleased");
                if (followProp != null) visible.Add(followProp);
            }

            SerializedProperty reactionProp = step.FindPropertyRelative("reactionMode");
            visible.Add(reactionProp);
            if ((StepReactionMode)reactionProp.enumValueIndex == StepReactionMode.On)
                visible.Add(step.FindPropertyRelative("reclaimAfterReaction"));
        }

        SerializedProperty variants = step.FindPropertyRelative("variants");
        if (variants != null) visible.Add(variants);
    }

    /// <summary>这一步的默认招式或任何一个候补是弹幕卡</summary>
    private static bool HasBulletCard(SerializedProperty step)
    {
        if (step.FindPropertyRelative("action").objectReferenceValue is BulletNode) return true;

        SerializedProperty variants = step.FindPropertyRelative("variants");
        if (variants == null) return false;
        for (int i = 0; i < variants.arraySize; i++)
        {
            SerializedProperty a = variants.GetArrayElementAtIndex(i).FindPropertyRelative("action");
            if (a != null && a.objectReferenceValue is BulletNode) return true;
        }
        return false;
    }
}
