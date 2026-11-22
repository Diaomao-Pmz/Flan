#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Flandre.CombatSystem.EditorTools
{
    /// <summary>
    /// 节拍总表的面板。每个招式一块：
    ///   第一行：标题 + 招式选框（平a / 蓄力的标题固定，用哪个招式看选框）/ 招式选框（双蓄槽）
    ///   第二行：Combo Window（双蓄没有）
    ///   第三行：三段时长 标题
    ///   四~六行：前摇 / 攻击 / 后摇
    ///   之后：判定（单段持续时间 / 多段判定窗口）、动画与表现（动画剪辑 / 出招位移 / 锁移动）、
    ///         蓄力位移（勾上才展开）、拼刀（招式上勾了 Can Clash 才显示）
    /// 每一块标题旁有「打开招式」，点了 Inspector 切到那个招式；
    /// 从招式面板点「在节拍总表中打开」过来时，只显示那一招（顶上有「显示全部」）。
    /// </summary>
    [CustomEditor(typeof(AttackTimingConfig))]
    public class AttackTimingConfigEditor : Editor
    {
        private const string PhaseHeader = "三段时长 (秒 · 填 -1 = 跟动画事件走)";
        private const string AnimHeader = "动画";
        private const string ShowHeader = "表现";
        private const string DuplicateMessage = "这个招式在表里出现了不止一次：用到它的行全部不生效，运行时会报错。请只保留一行。";

        private GUIStyle titleStyle;
        private GUIStyle groupStyle;

        /// <summary>只显示这一招（从招式面板跳过来时设置）。null = 显示全部</summary>
        public static ComboNode FocusNode;

        /// <summary>从招式面板跳到节拍总表，只显示这一招</summary>
        public static void OpenFor(ComboNode node)
        {
            AttackTimingConfig cfg = AttackTimingConfig.Active;
            if (cfg == null) return;
            FocusNode = node;
            Selection.activeObject = cfg;
        }

        private static bool Visible(Object node) => FocusNode == null || node == FocusNode;

        private void OnEnable()
        {
            var cfg = (AttackTimingConfig)target;
            if (cfg.AutoBind())
            {
                EditorUtility.SetDirty(cfg);
            }
        }

        // 离开节拍总表就恢复「显示全部」，下次正常打开时不会还停在只看一招
        private void OnDisable()
        {
            FocusNode = null;
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

            if (FocusNode != null)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"只显示：{FocusNode.nodeName}", EditorStyles.boldLabel);
                if (GUILayout.Button("显示全部", GUILayout.Width(70))) FocusNode = null;
                EditorGUILayout.EndHorizontal();
            }

            DrawFixedGroup("平a", serializedObject.FindProperty("normalAttacks"), duplicates);
            DrawFixedGroup("蓄力", serializedObject.FindProperty("chargeAttacks"), duplicates);
            DrawDualGroup("双蓄（未定 · 把要复用的招式拖进选框）", serializedObject.FindProperty("dualAttacks"), duplicates);

            serializedObject.ApplyModifiedProperties();
        }

        // ==========================================================

        private void DrawFixedGroup(string title, SerializedProperty list, HashSet<Object> duplicates)
        {
            if (!AnyVisible(list)) return;
            GroupTitle(title);

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                string slotName = e.FindPropertyRelative("slotName").stringValue;
                SerializedProperty nodeProp = e.FindPropertyRelative("node");
                Object node = nodeProp.objectReferenceValue;
                if (!Visible(node)) continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // 第一行：标题 + 招式选框
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(slotName, titleStyle, GUILayout.Width(EditorGUIUtility.labelWidth));
                EditorGUILayout.PropertyField(nodeProp, GUIContent.none);
                OpenNodeButton(nodeProp.objectReferenceValue);
                EditorGUILayout.EndHorizontal();

                node = nodeProp.objectReferenceValue;
                if (node == null)
                    EditorGUILayout.HelpBox("这一行还没有拖招式（ComboNode），不会生效。", MessageType.Warning);
                else if (duplicates.Contains(node))
                    EditorGUILayout.HelpBox(DuplicateMessage, MessageType.Error);

                // 第二行：连段窗口
                EditorGUILayout.PropertyField(e.FindPropertyRelative("comboWindow"));

                // 第三~六行：三段时长
                DrawPhases(e);

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawDualGroup(string title, SerializedProperty list, HashSet<Object> duplicates)
        {
            if (!AnyVisible(list)) return;
            GroupTitle(title);

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                SerializedProperty nodeProp = e.FindPropertyRelative("node");
                if (!Visible(nodeProp.objectReferenceValue)) continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // 第一行：招式选框
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(nodeProp, new GUIContent($"双蓄 {i + 1}", "把要复用的招式（ComboNode）拖进来"));
                OpenNodeButton(nodeProp.objectReferenceValue);
                EditorGUILayout.EndHorizontal();

                if (nodeProp.objectReferenceValue != null && duplicates.Contains(nodeProp.objectReferenceValue))
                    EditorGUILayout.HelpBox(DuplicateMessage, MessageType.Error);

                // 双蓄是连段终点，没有连段窗口
                DrawPhases(e);

                EditorGUILayout.EndVertical();
            }
        }

        private static bool AnyVisible(SerializedProperty list)
        {
            if (FocusNode == null) return true;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("node").objectReferenceValue == FocusNode) return true;
            }
            return false;
        }

        /// <summary>「打开招式」：Inspector 切到这个招式的面板</summary>
        private static void OpenNodeButton(Object node)
        {
            using (new EditorGUI.DisabledScope(node == null))
            {
                if (GUILayout.Button(new GUIContent("打开招式", "Inspector 切到这个招式（ComboNode）的面板"), GUILayout.Width(70)))
                {
                    FocusNode = null;
                    Selection.activeObject = node;
                }
            }
        }

        private static void DrawPhases(SerializedProperty e)
        {
            EditorGUILayout.LabelField(PhaseHeader, EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(e.FindPropertyRelative("windupOverride"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("activeOverride"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("recoveryOverride"));

            // 判定 / 出弹：按招式类型只显示用得上的（近战 = 判定，远程 = 出弹，Mix 两个都有，特殊招式都不用）
            ComboNode owner = e.FindPropertyRelative("node").objectReferenceValue as ComboNode;
            ComboMoveType type = owner != null ? owner.moveType : ComboMoveType.Mix;
            bool showHitbox = type == ComboMoveType.Melee || type == ComboMoveType.Mix;
            bool showProjectile = type == ComboMoveType.Ranged || type == ComboMoveType.Mix;
            float activeLen = owner != null ? AttackTimelineData.ResolveTiming(owner).active : -1f;

            if (showHitbox)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("判定", EditorStyles.miniBoldLabel);
                DrawStartTime(e.FindPropertyRelative("hitboxStartTime"), "Hitbox Start Time (秒)",
                              "判定段开始后第几秒开判定框。-1 = 跟动画里的 TriggerAttackHitbox 事件走", activeLen, "判定框");
                SerializedProperty windows = e.FindPropertyRelative("hitboxWindows");
                if (windows.arraySize == 0)
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("hitboxDuration"), new GUIContent("Hitbox Duration (单段 · 秒)"));
                EditorGUILayout.PropertyField(windows, new GUIContent("Hitbox Windows (一招多段)"), true);
            }

            if (showProjectile)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("出弹", EditorStyles.miniBoldLabel);
                DrawStartTime(e.FindPropertyRelative("projectileStartTime"), "Projectile Start Time (秒)",
                              "判定段开始后第几秒出弹。-1 = 跟动画里的 SpawnProjectile 事件走（连射发数 / 间隔在招式的弹幕参数里）", activeLen, "子弹");
            }

            if (!showHitbox && !showProjectile)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("判定", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("特殊招式按三段时长自己跑，不用出刀 / 出弹时间。", MessageType.None);
            }

            EditorGUILayout.PropertyField(e.FindPropertyRelative("switchCutPhases"), new GUIContent("可被抢拍的阶段", "这一招在哪几段可以被下一招（另一只手的平a、松手放蓄力）抢拍，可多选"));

            // 动画：剪辑、锁移动、特效时间
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(AnimHeader, EditorStyles.miniBoldLabel);
            SerializedProperty clip = e.FindPropertyRelative("attackClip");
            EditorGUILayout.PropertyField(clip);
            if (clip.objectReferenceValue == null && e.FindPropertyRelative("node").objectReferenceValue != null)
                EditorGUILayout.HelpBox("没有动画剪辑：这一招打不出判定 / 子弹 / 特效。", MessageType.Warning);
            EditorGUILayout.PropertyField(e.FindPropertyRelative("lockMovementDuringAttack"));
            SerializedProperty fxTime = e.FindPropertyRelative("playEffectTime");
            EditorGUILayout.PropertyField(fxTime, new GUIContent("Play Effect Time (秒)",
                "出招后第几秒播这一招的招式特效（从动画第 0 秒算）。-1 = 跟动画里的 PlayEffect 事件走"));
            if (owner != null && fxTime.floatValue >= 0f)
            {
                float total = AttackTimelineData.ResolveTiming(owner).Duration;
                if (fxTime.floatValue > total + 0.0001f)
                    EditorGUILayout.HelpBox($"填的时间（{fxTime.floatValue:F2}s）比整招（{total:F2}s）还长，特效不会出现。", MessageType.Warning);
            }

            // 表现：出招位移
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(ShowHeader, EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(e.FindPropertyRelative("forwardThrust"), new GUIContent("Forward Thrust (距离)"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("thrustStart"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("thrustDuration"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("passThroughBoss"), new GUIContent("Pass Through Boss", "位移走着的时候可以穿过 Boss"));
            EditorGUILayout.PropertyField(e.FindPropertyRelative("applyThrustInAir"));

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

            // 拼刀：招式上勾了 Can Clash 才显示（拼刀框和效果在招式资产上调）
            ComboNode n = e.FindPropertyRelative("node").objectReferenceValue as ComboNode;
            if (n != null && n.canClash)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("拼刀 (Clash)", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(e.FindPropertyRelative("clashWindow"), new GUIContent("Clash Window (秒)"));
            }
        }

        /// <summary>出刀 / 出弹时间：-1 时注明跟动画事件走；比判定段还晚时提示</summary>
        private static void DrawStartTime(SerializedProperty prop, string label, string tooltip, float activeLen, string what)
        {
            EditorGUILayout.PropertyField(prop, new GUIContent(label, tooltip));
            float v = prop.floatValue;
            if (v >= 0f && activeLen >= 0f && v > activeLen + 0.0001f)
                EditorGUILayout.HelpBox($"填的时间（{v:F2}s）比判定段（{activeLen:F2}s）还长，{what}不会出现。", MessageType.Warning);
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
