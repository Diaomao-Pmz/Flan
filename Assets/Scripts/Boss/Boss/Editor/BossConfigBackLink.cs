using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 招式卡 / 套路 Inspector 顶上的「用在」一行：列出工程里用到它的 AI 配置表、Tp 配置表、套路，点一下切过去。
/// 不记「刚才从哪来」，而是去工程里找，所以直接从文件夹点开的卡也能跳回去。
/// 只在切换选中对象 / 工程资产变化时重新找一次，不会每帧找。
/// </summary>
[InitializeOnLoad]
public static class BossConfigBackLink
{
    private static Object cachedTarget;
    private static readonly List<Object> cachedHolders = new List<Object>();

    static BossConfigBackLink()
    {
        Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
        Selection.selectionChanged += ClearCache;
        EditorApplication.projectChanged += ClearCache;
    }

    private static void ClearCache() => cachedTarget = null;

    private static void OnHeaderGUI(Editor editor)
    {
        if (editor == null || editor.targets.Length != 1) return;

        Object target = editor.target;
        if (!(target is ActionNode) && !(target is BossRoutine)) return;
        if (!EditorUtility.IsPersistent(target)) return;

        if (cachedTarget != target)
        {
            FindHolders(target, cachedHolders);
            cachedTarget = target;
        }

        if (cachedHolders.Count == 0) return;

        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("用在：", EditorStyles.miniBoldLabel);
        foreach (Object holder in cachedHolders)
        {
            if (holder == null) continue;
            if (GUILayout.Button($"← {KindOf(holder)}：{holder.name}", EditorStyles.miniButton))
                Selection.activeObject = holder;
        }
        EditorGUILayout.Space(2f);
    }

    private static string KindOf(Object holder)
    {
        if (holder is BossAIConfig) return "AI 配置表";
        if (holder is BossTpConfig) return "Tp 配置表";
        if (holder is BossRoutine) return "套路";
        return holder.GetType().Name;
    }

    /// <summary>按「AI 配置表 → Tp 配置表 → 套路」的顺序，找出所有引用了 target 的资产</summary>
    private static void FindHolders(Object target, List<Object> result)
    {
        result.Clear();
        Collect<BossAIConfig>(target, result);
        Collect<BossTpConfig>(target, result);
        if (target is ActionNode) Collect<BossRoutine>(target, result);
    }

    private static void Collect<T>(Object target, List<Object> result) where T : Object
    {
        foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null && asset != target && References(asset, target)) result.Add(asset);
        }
    }

    private static bool References(Object asset, Object target)
    {
        SerializedProperty it = new SerializedObject(asset).GetIterator();
        while (it.Next(true))
        {
            if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == target)
                return true;
        }
        return false;
    }
}
