#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Flandre.CombatSystem.EditorTools
{
    /// <summary>
    /// 招式资产（ComboNode）的面板：其余栏位照常显示，只多做两件事 ——
    ///   1. 没勾 Can Clash 时，拼刀框的几个栏位收起来
    ///   2. 勾了之后多一个「编辑拼刀框」按钮（和 Box Collider 的 Edit Collider 一样）：
    ///      按下后在 Scene 视图里以场景中的玩家为原点显示拼刀框，拖边调大小、拖中间的点调位置
    ///
    /// 招式资产本身没有位置，所以要借场景里的玩家当原点；场景里没有玩家时按钮会提示。
    /// 选中招式资产时，Scene 视图里总会淡淡地画出拼刀框（青色）和伤害判定框（绿色）作参考。
    /// </summary>
    [CustomEditor(typeof(ComboNode))]
    public class ComboNodeClashBoxEditor : Editor
    {
        private static readonly HashSet<string> ClashDetailFields = new HashSet<string>
        {
            "clashBoxOffset", "clashBoxSize", "clashEffect",
        };

        // 同一时间只编辑一个招式的拼刀框
        private static ComboNode editingNode;

        private static PlayerController cachedPlayer;

        private BoxBoundsHandle boxHandle;
        private readonly List<HitboxWindow> windowBuffer = new List<HitboxWindow>();

        private static readonly Color ClashColor = new Color(0f, 0.9f, 1f, 1f);
        private static readonly Color ClashFaint = new Color(0f, 0.9f, 1f, 0.45f);
        private static readonly Color HitboxFaint = new Color(0.3f, 1f, 0.3f, 0.45f);

        private void OnEnable()
        {
            boxHandle = new BoxBoundsHandle
            {
                axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y,
                handleColor = ClashColor,
                wireframeColor = ClashColor,
            };
            SceneView.duringSceneGui += DrawInScene;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawInScene;
            if (editingNode == target) editingNode = null;
        }

        // ==========================================================
        // Inspector
        // ==========================================================

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            bool canClash = serializedObject.FindProperty("canClash").boolValue;

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

                if (!canClash && ClashDetailFields.Contains(it.propertyPath)) continue;

                EditorGUILayout.PropertyField(it, true);

                if (canClash && it.propertyPath == "clashEffect") DrawClashTools();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawClashTools()
        {
            ComboNode node = (ComboNode)target;
            bool isEditing = editingNode == node;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);

            GUIContent icon = EditorGUIUtility.IconContent("EditCollider");
            icon.tooltip = "在 Scene 视图里拖着调拼刀框（以场景里的玩家为原点）";
            bool wantEditing = GUILayout.Toggle(isEditing, icon, "Button", GUILayout.Width(33), GUILayout.Height(23));
            GUILayout.Label("编辑拼刀框", GUILayout.Height(23));

            EditorGUILayout.EndHorizontal();

            if (wantEditing != isEditing)
            {
                editingNode = wantEditing ? node : null;
                SceneView.RepaintAll();
            }

            if (editingNode == node)
            {
                EditorGUILayout.HelpBox(
                    FindPlayer() == null
                        ? "当前打开的场景里找不到玩家（PlayerController），没法在 Scene 视图里显示拼刀框。"
                        : "在 Scene 视图里拖框的边调大小、拖中间的点调位置。按玩家当前的朝向显示（朝左时 X 自动镜像，存下来的永远是「身前为正」）。",
                    FindPlayer() == null ? MessageType.Warning : MessageType.Info);
            }

            if (AttackTimingConfig.Find(node) == null)
            {
                EditorGUILayout.HelpBox("这个招式不在节拍总表里：拼刀窗口时长读不到，不会开拼刀框。", MessageType.Warning);
            }
        }

        // ==========================================================
        // Scene 视图
        // ==========================================================

        private static PlayerController FindPlayer()
        {
            if (cachedPlayer == null) cachedPlayer = Object.FindAnyObjectByType<PlayerController>();
            return cachedPlayer;
        }

        private void DrawInScene(SceneView view)
        {
            ComboNode node = target as ComboNode;
            if (node == null) return;

            PlayerController player = FindPlayer();
            if (player == null) return;

            float facing = player.facingDirection < 0 ? -1f : 1f;
            Vector2 origin = player.transform.position;

            // 伤害判定框作参考（绿色，只画不编辑）
            node.CollectWindows(windowBuffer);
            Handles.color = HitboxFaint;
            for (int i = 0; i < windowBuffer.Count; i++)
            {
                HitboxWindow w = windowBuffer[i];
                if (w == null || w.size.x <= 0f || w.size.y <= 0f) continue;
                Handles.DrawWireCube(origin + new Vector2(w.offset.x * facing, w.offset.y), w.size);
            }

            if (!node.canClash) return;

            Vector2 center = origin + new Vector2(node.clashBoxOffset.x * facing, node.clashBoxOffset.y);

            if (editingNode != node)
            {
                Handles.color = ClashFaint;
                Handles.DrawWireCube(center, node.clashBoxSize);
                return;
            }

            // ---- 编辑中：拖边调大小，拖中间的点调位置 ----
            boxHandle.center = center;
            boxHandle.size = new Vector3(node.clashBoxSize.x, node.clashBoxSize.y, 0f);

            EditorGUI.BeginChangeCheck();

            boxHandle.DrawHandle();

            Handles.color = ClashColor;
            Vector3 c = boxHandle.center;
            Vector3 moved = Handles.FreeMoveHandle(c, HandleUtility.GetHandleSize(c) * 0.06f, Vector3.zero, Handles.DotHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(node, "调整拼刀框");

                Vector2 newCenter = moved;
                Vector2 newSize = boxHandle.size;

                node.clashBoxOffset = new Vector2(
                    Round((newCenter.x - origin.x) * facing),
                    Round(newCenter.y - origin.y));
                node.clashBoxSize = new Vector2(Round(Mathf.Abs(newSize.x)), Round(Mathf.Abs(newSize.y)));

                EditorUtility.SetDirty(node);
                Repaint();
            }

            Handles.Label(center + new Vector2(0f, node.clashBoxSize.y * 0.5f + 0.15f), $"拼刀框 · {node.nodeName}");
        }

        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;
    }
}
#endif
