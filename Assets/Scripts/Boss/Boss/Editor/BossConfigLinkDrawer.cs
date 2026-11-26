using UnityEditor;
using UnityEngine;

/// <summary>
/// 招式卡 / 套路栏位旁的「→」按钮：点一下 Inspector 切到那张卡（或那个套路）。
/// 只在 AI 配置表、Tp 配置表、套路里显示；其它地方的招式栏位照旧，没有按钮。
/// 从卡跳回来用卡顶上的「用在」按钮（BossConfigBackLink）。
/// </summary>
[CustomPropertyDrawer(typeof(ActionNode), true)]
[CustomPropertyDrawer(typeof(BossRoutine), true)]
public class BossConfigLinkDrawer : PropertyDrawer
{
    private const float ButtonWidth = 24f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUIUtility.singleLineHeight;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.ObjectReference)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        if (!ShowLink(property))
        {
            EditorGUI.ObjectField(position, property, label);
            return;
        }

        Rect fieldRect = new Rect(position.x, position.y, position.width - ButtonWidth - 2f, position.height);
        Rect buttonRect = new Rect(position.xMax - ButtonWidth, position.y, ButtonWidth, EditorGUIUtility.singleLineHeight);

        EditorGUI.ObjectField(fieldRect, property, label);

        UnityEngine.Object target = property.objectReferenceValue;
        using (new EditorGUI.DisabledScope(target == null))
        {
            // 缩进会让按钮内容偏移，画按钮时临时归零
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            if (GUI.Button(buttonRect, new GUIContent("→", "在 Inspector 里打开它"), EditorStyles.miniButton))
                Selection.activeObject = target;
            EditorGUI.indentLevel = indent;
        }
    }

    /// <summary>只在这几种资产里显示跳转按钮</summary>
    private static bool ShowLink(SerializedProperty property)
    {
        UnityEngine.Object owner = property.serializedObject.targetObject;
        return owner is BossAIConfig || owner is BossTpConfig || owner is BossRoutine;
    }
}
