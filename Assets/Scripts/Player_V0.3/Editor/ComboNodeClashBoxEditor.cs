#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Flandre.CombatSystem.EditorTools
{
    /// <summary>
    /// 招式资产（ComboNode）的面板：其余栏位照常显示，多做这几件事 ——
    ///   1. 蓄力减速：没勾 Is Charge Skill 时整组收起；没勾 Charge Slows Movement 时两个倍率收起
    ///   2. 拼刀框：没勾 Can Clash 时收起；勾了之后多一个「编辑拼刀框」按钮
    ///   3. 判定框：单段判定参数下面多一个「编辑判定框」按钮
    ///      （节拍总表里这一招的判定窗口列表为空 → 编辑单段判定框；列表里有条目 → 每一段各一个框，改的是表里的数）
    ///   4. 最上面一个「在节拍总表中打开」：切到节拍总表，只显示这一招
    ///   两个按钮和 Box Collider 的 Edit Collider 一样：按下后在 Scene 视图里以场景中的玩家为原点显示框，
    ///   拖边调大小、拖中间的点调位置，支持撤销。同一时间只编辑一种框。
    ///
    /// 招式资产本身没有位置，所以要借场景里的玩家当原点；场景里没有玩家时按钮会提示。
    /// 选中招式资产时，Scene 视图里总会淡淡地画出拼刀框和伤害判定框（绿色）作参考。
    /// </summary>
    [CustomEditor(typeof(ComboNode))]
    public class ComboNodeClashBoxEditor : Editor
    {
        private enum EditMode { None, Clash, Hitbox }

        private static readonly HashSet<string> ClashDetailFields = new HashSet<string>
        {
            "clashBoxOffset", "clashBoxSize", "clashMask",
            "clashThrust", "clashThrustDuration", "clashThrustPassThrough", "clashBossStagger",
        };

        private static readonly HashSet<string> ChargeSlowFields = new HashSet<string>
        {
            "chargeSlowsMovement", "chargeMoveSpeedMultiplier", "chargeJumpForceMultiplier", "comboChargeStartTime",
            "chargeMoveSpeedStart", "chargeJumpForceStart", "chargeSlowRampTime",
        };

        private static readonly HashSet<string> ChargeSlowMultiplierFields = new HashSet<string>
        {
            "chargeMoveSpeedMultiplier", "chargeJumpForceMultiplier",
            "chargeMoveSpeedStart", "chargeJumpForceStart", "chargeSlowRampTime",
        };

        // ---- 按招式类型显示 / 隐藏（只是不显示，值照样生效）----

        private static readonly HashSet<string> BulletFields = new HashSet<string>
        {
            "projectilePoolKeyOverride", "projectileCount", "projectileInterval",
            "projectileSpeedMultiplier", "projectileScaleMultiplier", "projectileTotalDamage",
            "projectileTotalShieldBreak", "recomputeAimPerShot",
        };

        private static readonly HashSet<string> SingleHitboxFields = new HashSet<string>
        {
            "damage", "hitboxSize", "hitboxOffset", "meleeShieldBreak",
        };

        /// <summary>特殊招式只显示这些（其余隐藏）</summary>
        private static readonly HashSet<string> SpecialVisibleFields = new HashSet<string>
        {
            "nodeName", "moveType", "specialMove",
            "castCondition", "requiredState", "inputSequence",
            "isChargeSkill", "chargeAllowedFrom", "chargeLevel", "comboChargeStartTime", "chargeSlowsMovement", "chargeMoveSpeedMultiplier", "chargeJumpForceMultiplier",
            "chargeMoveSpeedStart", "chargeJumpForceStart", "chargeSlowRampTime",
            "breakMask", "bossKnockbackDistance", "bossPushSpeed", "bossHitStun", "hitReaction", "hitReactionParam",
            "bulletCancel",
            "canClash", "clashBoxOffset", "clashBoxSize", "clashMask", "clashThrust", "clashThrustDuration", "clashThrustPassThrough", "clashBossStagger",
            "childNodes",
        };

        private static bool IsVisibleFor(ComboMoveType type, string path)
        {
            switch (type)
            {
                case ComboMoveType.Melee: return !BulletFields.Contains(path) && path != "specialMove";
                case ComboMoveType.Ranged: return !SingleHitboxFields.Contains(path) && path != "specialMove";
                case ComboMoveType.SpecialMelee:
                case ComboMoveType.SpecialRanged: return SpecialVisibleFields.Contains(path);
                default: return path != "specialMove";
            }
        }

        // 同一时间只编辑一个招式的一种框
        private static ComboNode editingNode;
        private static EditMode editMode = EditMode.None;

        private static PlayerController cachedPlayer;

        private BoxBoundsHandle clashHandle;
        private readonly List<BoxBoundsHandle> hitboxHandles = new List<BoxBoundsHandle>();
        private readonly List<HitboxWindow> windowBuffer = new List<HitboxWindow>();

        // 拼刀框保持原来的绿色调（用户确认过）；判定框是亮绿色，靠框上的文字区分
        private static readonly Color ClashColor = new Color(0f, 0.9f, 0.3f, 0.85f);
        private static readonly Color ClashFaint = new Color(0f, 0.9f, 0.3f, 0.45f);
        private static readonly Color HitboxColor = new Color(0.3f, 1f, 0.3f, 1f);
        private static readonly Color HitboxFaint = new Color(0.3f, 1f, 0.3f, 0.45f);

        private void OnEnable()
        {
            clashHandle = NewHandle(ClashColor);
            SceneView.duringSceneGui += DrawInScene;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawInScene;
            if (editingNode == target)
            {
                editingNode = null;
                editMode = EditMode.None;
            }
        }

        private static BoxBoundsHandle NewHandle(Color color)
        {
            return new BoxBoundsHandle
            {
                axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y,
                handleColor = color,
                wireframeColor = color,
            };
        }

        private bool IsEditing(EditMode mode) => editingNode == target && editMode == mode;

        // ==========================================================
        // Inspector
        // ==========================================================

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawOpenInTableButton();

            bool canClash = serializedObject.FindProperty("canClash").boolValue;
            bool isCharge = serializedObject.FindProperty("isChargeSkill").boolValue;
            bool slows = serializedObject.FindProperty("chargeSlowsMovement").boolValue;
            ComboMoveType moveType = (ComboMoveType)serializedObject.FindProperty("moveType").enumValueIndex;

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

                if (!IsVisibleFor(moveType, path)) continue;
                if (!canClash && ClashDetailFields.Contains(path)) continue;
                if (!isCharge && ChargeSlowFields.Contains(path)) continue;
                if (!slows && ChargeSlowMultiplierFields.Contains(path)) continue;

                if (path == "specialMove")
                {
                    DrawSpecialMove(it, moveType);
                    continue;
                }

                EditorGUILayout.PropertyField(it, true);

                if (path == "bulletCancel" && (it.intValue & (int)BulletCancelMode.Melee) != 0
                    && moveType != ComboMoveType.Melee && moveType != ComboMoveType.Mix)
                    EditorGUILayout.HelpBox("这一招没有近战判定框（远程招 / 特殊招），勾「近战判定」消弹不会有效果。", MessageType.Info);

                if (canClash && path == "clashBoxSize") DrawClashTools();
                if (path == "hitboxOffset") DrawHitboxTools();
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ==========================================================
        // 特殊招式插槽：下拉框选类型（按 Special Melee / Special Ranged 筛选），下面直接展开它的参数
        // ==========================================================

        private void DrawSpecialMove(SerializedProperty prop, ComboMoveType moveType)
        {
            SpecialMoveKind wanted = moveType == ComboMoveType.SpecialMelee ? SpecialMoveKind.Melee : SpecialMoveKind.Ranged;
            object current = prop.managedReferenceValue;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("特殊招式", EditorStyles.boldLabel);

            Rect row = EditorGUILayout.GetControlRect();
            Rect labelRect = new Rect(row.x, row.y, EditorGUIUtility.labelWidth, row.height);
            Rect buttonRect = new Rect(row.x + EditorGUIUtility.labelWidth + 2f, row.y,
                                       row.width - EditorGUIUtility.labelWidth - 2f, row.height);

            EditorGUI.LabelField(labelRect, new GUIContent("类型", "选这一招具体是哪一种特殊招式"));
            string shown = current == null ? "（未选择，点此选择）" : DisplayName(current.GetType());
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(shown), FocusType.Keyboard))
                ShowSpecialMenu(prop.serializedObject, prop.propertyPath, current, wanted);

            if (current == null)
            {
                EditorGUILayout.HelpBox("还没有选特殊招式：这一招不会造成任何伤害。", MessageType.Warning);
                return;
            }

            SpecialMoveInfoAttribute info = InfoOf(current.GetType());
            if (info != null && info.kind != wanted)
            {
                EditorGUILayout.HelpBox(
                    $"「{info.displayName}」属于 {(info.kind == SpecialMoveKind.Melee ? "Special Melee" : "Special Ranged")}，" +
                    "和上面选的招式类型不一致（照样能用，只是提醒一下）。", MessageType.Info);
            }

            SerializedProperty child = prop.Copy();
            SerializedProperty end = prop.GetEndProperty();
            EditorGUI.indentLevel++;
            if (child.NextVisible(true))
            {
                while (!SerializedProperty.EqualContents(child, end))
                {
                    EditorGUILayout.PropertyField(child, true);
                    if (!child.NextVisible(false)) break;
                }
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
        }

        private static void ShowSpecialMenu(SerializedObject so, string path, object current, SpecialMoveKind wanted)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("（清空）"), current == null, () => AssignSpecial(so, path, null));
            menu.AddSeparator("");

            int count = 0;
            foreach (System.Type t in TypeCache.GetTypesDerivedFrom<SpecialMove>())
            {
                if (t.IsAbstract || t.IsGenericType || t.GetConstructor(System.Type.EmptyTypes) == null) continue;

                SpecialMoveInfoAttribute info = InfoOf(t);
                if (info != null && info.kind != wanted) continue;

                System.Type captured = t;
                bool isCurrent = current != null && current.GetType() == t;
                menu.AddItem(new GUIContent(DisplayName(t)), isCurrent, () => AssignSpecial(so, path, captured));
                count++;
            }

            if (count == 0) menu.AddDisabledItem(new GUIContent("（这一类还没有可选的特殊招式）"));
            menu.ShowAsContext();
        }

        private static void AssignSpecial(SerializedObject so, string path, System.Type type)
        {
            so.Update();
            SerializedProperty prop = so.FindProperty(path);
            prop.managedReferenceValue = type != null ? System.Activator.CreateInstance(type) : null;
            prop.isExpanded = true;
            so.ApplyModifiedProperties();
        }

        private static SpecialMoveInfoAttribute InfoOf(System.Type t)
            => (SpecialMoveInfoAttribute)System.Attribute.GetCustomAttribute(t, typeof(SpecialMoveInfoAttribute));

        private static string DisplayName(System.Type t)
        {
            SpecialMoveInfoAttribute info = InfoOf(t);
            return info != null ? info.displayName : ObjectNames.NicifyVariableName(t.Name);
        }

        /// <summary>「在节拍总表中打开」：切到节拍总表，只显示这一招</summary>
        private void DrawOpenInTableButton()
        {
            ComboNode node = (ComboNode)target;
            bool inTable = AttackTimingConfig.Active != null && AttackTimingConfig.Find(node) != null;

            using (new EditorGUI.DisabledScope(!inTable))
            {
                if (GUILayout.Button(new GUIContent("在节拍总表中打开",
                        "切到节拍总表，只显示这一招（连段窗口、三段时长、判定时间、动画与表现、位移、拼刀窗口）")))
                {
                    AttackTimingConfigEditor.OpenFor(node);
                    GUIUtility.ExitGUI();
                }
            }

            if (!inTable)
                EditorGUILayout.HelpBox("这个招式不在节拍总表里：没有动画、三段时长、判定时间，也打不出判定。", MessageType.Warning);

            EditorGUILayout.Space(2);
        }

        /// <summary>和 Box Collider 同款的编辑按钮。返回按钮此刻是否按下</summary>
        private bool DrawEditButton(EditMode mode, string label, string tooltip)
        {
            bool isEditing = IsEditing(mode);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);

            GUIContent icon = EditorGUIUtility.IconContent("EditCollider");
            icon.tooltip = tooltip;
            bool want = GUILayout.Toggle(isEditing, icon, "Button", GUILayout.Width(33), GUILayout.Height(23));
            GUILayout.Label(label, GUILayout.Height(23));

            EditorGUILayout.EndHorizontal();

            if (want != isEditing)
            {
                editingNode = want ? (ComboNode)target : null;
                editMode = want ? mode : EditMode.None;
                SceneView.RepaintAll();
            }

            return IsEditing(mode);
        }

        private void DrawNoPlayerWarning()
        {
            if (FindPlayer() == null)
                EditorGUILayout.HelpBox("当前打开的场景里找不到玩家（PlayerController），没法在 Scene 视图里显示框。", MessageType.Warning);
        }

        private void DrawClashTools()
        {
            ComboNode node = (ComboNode)target;

            if (DrawEditButton(EditMode.Clash, "编辑拼刀框", "在 Scene 视图里拖着调拼刀框（以场景里的玩家为原点）"))
            {
                DrawNoPlayerWarning();
                if (FindPlayer() != null)
                    EditorGUILayout.HelpBox("在 Scene 视图里拖框的边调大小、拖中间的点调位置。按玩家当前的朝向显示（朝左时 X 自动镜像，存下来的永远是「身前为正」）。", MessageType.Info);
            }

        }

        private void DrawHitboxTools()
        {
            ComboNode node = (ComboNode)target;

            if (DrawEditButton(EditMode.Hitbox, "编辑判定框", "在 Scene 视图里拖着调伤害判定框（以场景里的玩家为原点）"))
            {
                DrawNoPlayerWarning();
                if (FindPlayer() != null)
                {
                    EditorGUILayout.HelpBox(
                        node.HasMultipleWindows
                            ? $"节拍总表里这一招有 {node.hitboxWindows.Count} 段判定窗口：Scene 视图里每一段各一个框（标着段名），改的是节拍总表里的数值。"
                            : "节拍总表里这一招的判定窗口列表为空：Scene 视图里编辑的是上面的单段判定框（Hitbox Size / Hitbox Offset）。",
                        MessageType.Info);
                }
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

            // ---- 伤害判定框 ----
            if (IsEditing(EditMode.Hitbox))
            {
                EditHitboxes(node, origin, facing);
            }
            else
            {
                node.CollectWindows(windowBuffer);
                Handles.color = HitboxFaint;
                for (int i = 0; i < windowBuffer.Count; i++)
                {
                    HitboxWindow w = windowBuffer[i];
                    if (w == null || w.size.x <= 0f || w.size.y <= 0f) continue;
                    Handles.DrawWireCube(origin + new Vector2(w.offset.x * facing, w.offset.y), w.size);
                }
            }

            // ---- 拼刀框 ----
            if (!node.canClash) return;

            if (IsEditing(EditMode.Clash))
            {
                Vector2 offset = node.clashBoxOffset;
                Vector2 size = node.clashBoxSize;
                if (EditBox(clashHandle, ClashColor, origin, facing, ref offset, ref size, $"拼刀框 · {node.nodeName}"))
                {
                    Undo.RecordObject(node, "调整拼刀框");
                    node.clashBoxOffset = offset;
                    node.clashBoxSize = size;
                    EditorUtility.SetDirty(node);
                    Repaint();
                }
            }
            else
            {
                Handles.color = ClashFaint;
                Handles.DrawWireCube(origin + new Vector2(node.clashBoxOffset.x * facing, node.clashBoxOffset.y), node.clashBoxSize);
            }
        }

        private void EditHitboxes(ComboNode node, Vector2 origin, float facing)
        {
            if (node.HasMultipleWindows)
            {
                List<HitboxWindow> list = node.hitboxWindows;
                while (hitboxHandles.Count < list.Count) hitboxHandles.Add(NewHandle(HitboxColor));

                for (int i = 0; i < list.Count; i++)
                {
                    HitboxWindow w = list[i];
                    if (w == null) continue;

                    Vector2 offset = w.offset;
                    Vector2 size = w.size;
                    string label = $"判定框 {i + 1} · {(string.IsNullOrEmpty(w.label) ? node.nodeName : w.label)}";
                    if (EditBox(hitboxHandles[i], HitboxColor, origin, facing, ref offset, ref size, label))
                    {
                        // 多段判定窗口存在节拍总表里
                        AttackTimingConfig cfg = AttackTimingConfig.Active;
                        if (cfg != null) Undo.RecordObject(cfg, "调整判定框");
                        w.offset = offset;
                        w.size = size;
                        if (cfg != null) EditorUtility.SetDirty(cfg);
                        Repaint();
                    }
                }
            }
            else
            {
                if (hitboxHandles.Count == 0) hitboxHandles.Add(NewHandle(HitboxColor));

                Vector2 offset = node.hitboxOffset;
                Vector2 size = node.hitboxSize;
                if (EditBox(hitboxHandles[0], HitboxColor, origin, facing, ref offset, ref size, $"判定框 · {node.nodeName}"))
                {
                    Undo.RecordObject(node, "调整判定框");
                    node.hitboxOffset = offset;
                    node.hitboxSize = size;
                    EditorUtility.SetDirty(node);
                    Repaint();
                }
            }
        }

        /// <summary>
        /// 画一个可拖动的框（拖边调大小、拖中间的点调位置）。
        /// offset 是「身前为正」的偏移，按 facing 镜像显示；改了返回 true。
        /// </summary>
        private static bool EditBox(BoxBoundsHandle handle, Color color, Vector2 origin, float facing,
                                    ref Vector2 offset, ref Vector2 size, string label)
        {
            Vector2 center = origin + new Vector2(offset.x * facing, offset.y);

            handle.center = center;
            handle.size = new Vector3(size.x, size.y, 0f);

            // 画笔颜色先归白：BoxBoundsHandle 会把当前画笔颜色叠到自己身上
            Handles.color = Color.white;

            EditorGUI.BeginChangeCheck();

            handle.DrawHandle();

            Handles.color = color;
            Vector3 c = handle.center;
            Vector3 moved = Handles.FreeMoveHandle(c, HandleUtility.GetHandleSize(c) * 0.06f, Vector3.zero, Handles.DotHandleCap);

            bool changed = EditorGUI.EndChangeCheck();

            Handles.Label(center + new Vector2(0f, size.y * 0.5f + 0.15f), label);

            if (!changed) return false;

            Vector2 newCenter = moved;
            Vector2 newSize = handle.size;

            offset = new Vector2(Round((newCenter.x - origin.x) * facing), Round(newCenter.y - origin.y));
            size = new Vector2(Round(Mathf.Abs(newSize.x)), Round(Mathf.Abs(newSize.y)));
            return true;
        }

        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;
    }
}
#endif
