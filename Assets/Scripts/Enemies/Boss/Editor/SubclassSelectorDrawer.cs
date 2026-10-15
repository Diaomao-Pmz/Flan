using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 给标了 [SubclassSelector] 的 [SerializeReference] 字段画一个"选类型"的下拉框。
///
/// 比喻：字段是一个空插槽，下拉框列出所有能插进去的零件（子类），
/// 插上哪个，下面就展开那个零件自己的参数。
///
/// 切换类型时会把旧图案里同名的字段（通用子弹设置：间隔、速度、大小……）原样带到新图案，
/// 只有专属参数会变成新类型的默认值。
/// </summary>
[CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
public class SubclassSelectorDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUI.GetPropertyHeight(property, label, true);

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.ManagedReference)
        {
            EditorGUI.LabelField(position, label.text, "[SubclassSelector] 只能用在 [SerializeReference] 字段上");
            return;
        }

        EditorGUI.BeginProperty(position, label, property);

        // 下拉按钮放在第一行的右侧（标签右边）
        Rect buttonRect = new Rect(
            position.x + EditorGUIUtility.labelWidth + 2f,
            position.y,
            position.width - EditorGUIUtility.labelWidth - 2f,
            EditorGUIUtility.singleLineHeight);

        object current = property.managedReferenceValue;
        string currentName = current == null ? "（未选择，点此选择弹幕类型）" : DisplayName(current.GetType());

        if (EditorGUI.DropdownButton(buttonRect, new GUIContent(currentName), FocusType.Keyboard))
            ShowMenu(property, current);

        // 字段本体（带折叠箭头和子字段）。标签区域不会和按钮重叠
        EditorGUI.PropertyField(position, property, label, true);

        EditorGUI.EndProperty();
    }

    private void ShowMenu(SerializedProperty property, object current)
    {
        Type baseType = fieldInfo.FieldType;
        var types = TypeCache.GetTypesDerivedFrom(baseType)
            .Where(t => !t.IsAbstract && !t.IsGenericType && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => DisplayName(t))
            .ToList();

        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("（清空）"), current == null, () => Assign(property, null, current));
        menu.AddSeparator("");

        foreach (Type t in types)
        {
            Type captured = t;
            bool isCurrent = current != null && current.GetType() == t;
            menu.AddItem(new GUIContent(DisplayName(t)), isCurrent, () => Assign(property, captured, current));
        }

        menu.ShowAsContext();
    }

    private static void Assign(SerializedProperty property, Type type, object previous)
    {
        property.serializedObject.Update();

        object instance = null;
        if (type != null)
        {
            instance = Activator.CreateInstance(type);

            // 把旧图案的同名字段（主要是通用子弹设置）带过来
            if (previous != null)
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(previous), instance);
        }

        property.managedReferenceValue = instance;
        property.isExpanded = instance != null;
        property.serializedObject.ApplyModifiedProperties();
    }

    private static string DisplayName(Type t)
    {
        var info = t.GetCustomAttribute<BulletPatternInfoAttribute>();
        return info != null ? info.displayName : ObjectNames.NicifyVariableName(t.Name);
    }
}
