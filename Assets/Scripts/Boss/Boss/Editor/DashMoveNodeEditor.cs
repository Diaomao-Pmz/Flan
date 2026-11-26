using UnityEditor;
using UnityEngine;

/// <summary>
/// 冲刺卡的 Inspector：按冲刺方式只显示用得上的距离参数；
/// 选 Through Player 时「能穿过玩家」固定为开，不显示；关掉终点判定时收起穿过比例和重叠容差。
/// 只是不显示，隐藏的值照样保存。
/// </summary>
[CustomEditor(typeof(DashMoveNode))]
public class DashMoveNodeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var mode = (DashMoveTarget)serializedObject.FindProperty("target").enumValueIndex;
        bool guard = serializedObject.FindProperty("endpointGuard").boolValue;

        SerializedProperty it = serializedObject.GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;

            switch (it.propertyPath)
            {
                case "m_Script":
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(it);
                    continue;

                case "landingDistance":
                    if (mode != DashMoveTarget.ToDistance) continue;
                    break;
                case "retreatDistance":
                    if (mode != DashMoveTarget.AwayFromPlayer) continue;
                    break;
                case "behindDistance":
                    if (mode != DashMoveTarget.ThroughPlayer) continue;
                    break;

                case "canPassThrough":
                    if (mode == DashMoveTarget.ThroughPlayer) continue;
                    break;
                case "endpointPassRatio":
                case "endpointOverlapTolerance":
                    if (!guard) continue;
                    break;
            }

            EditorGUILayout.PropertyField(it, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
