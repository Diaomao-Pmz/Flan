using System.Text;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>逐帧回看的按键指令。由 PlayerController 读输入后转发给调试面板</summary>
    public enum DebugReviewCommand
    {
        Toggle,       // 暂停并进入回看 / 退出回看
        PrevFrame,    // 往回一帧
        NextFrame,    // 往前一帧
        PrevChange,   // 跳到上一次状态变化
        NextChange,   // 跳到下一次状态变化
    }

    /// <summary>
    /// 【战斗调试面板 + 逐帧回看】
    /// 平时：把战斗内部状态画在屏幕左上角。
    /// 回看：每帧录一份状态，按键暂停后可以一帧一帧往回翻，角色的位置 / 朝向 / 画面帧也跟着倒回去。
    ///
    /// 用法：挂在玩家 prefab 上（和 ComboInputBuffer 同一个物体）。
    /// 只在编辑器和 Development Build 里工作，正式包里不录、不画。
    /// 本组件不读输入（按键由 PlayerController 转发），除回看时的暂停和摆姿势外不改任何状态。
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatDebugPanel : MonoBehaviour
    {
        [Header("面板")]
        [Tooltip("是否显示面板。运行中可以随时勾选 / 取消。回看时一定显示")]
        public bool show = true;

        [Tooltip("面板左上角在屏幕上的位置（像素）")]
        public Vector2 position = new Vector2(10f, 10f);

        [Tooltip("字号")]
        [Range(10, 28)]
        public int fontSize = 14;

        [Header("逐帧回看 (F5 暂停/继续，F6/F7 上一帧/下一帧，F8/F9 上/下一次变化)")]
        [Tooltip("是否录制。关掉后不能回看，面板照常显示")]
        public bool record = true;

        [Tooltip("最多保留最近多少帧。60 帧/秒时 600 帧 = 10 秒。运行中修改会清空已录内容")]
        [Range(60, 3600)]
        public int recordFrames = 600;

        [Tooltip("回看时把角色摆回那一帧的位置、朝向和画面帧（特效、子弹、敌人不会倒回去）。退出回看时恢复")]
        public bool rewindPose = true;

        private struct HandSnap
        {
            public byte busy;             // 0 空闲，1 出招中，2 后摇
            public float recoveryRemain;
            public bool held;
            public bool charging, valid, notStarted;
            public int level, cap, start;
            public float progress;
        }

        private struct Snap
        {
            public int frame;
            public float time;
            public string state;
            public bool flying;
            public int depth;
            public float window;
            public string node;
            public AttackPhase phase;
            public bool instAlive;
            public float elapsed, duration, windupEnd, activeEnd;
            public HandSnap main, sub;
            public float dashCd, slideCd;
            public Vector3 pos;
            public Quaternion rot;
            public Vector2 velocity;
            public Sprite sprite;
        }

        private PlayerStateMachine sm;
        private ComboInputBuffer combo;
        private PlayerChargeSystem charge;
        private PlayerController controller;
        private SpriteRenderer sr;
        private Rigidbody2D rb;

        // 环形缓冲：head 是下一次写入的位置，count 是已录帧数
        private Snap[] buffer;
        private int head;
        private int count;

        private bool reviewing;
        private int viewBack;          // 正在看倒数第几帧（0 = 最新一帧）
        private float savedTimeScale = 1f;

        private readonly StringBuilder sb = new StringBuilder(768);
        private GUIStyle style;
        private readonly GUIContent content = new GUIContent();

        private void Awake()
        {
            sm = GetComponent<PlayerStateMachine>();
            combo = GetComponent<ComboInputBuffer>();
            charge = GetComponent<PlayerChargeSystem>();
            controller = GetComponent<PlayerController>();
            sr = GetComponent<SpriteRenderer>();
            rb = GetComponent<Rigidbody2D>();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnEnable()
        {
            if (controller != null) controller.DebugReviewCommandPressed += OnReviewCommand;
        }

        private void OnDisable()
        {
            if (controller != null) controller.DebugReviewCommandPressed -= OnReviewCommand;
            if (reviewing) ExitReview();
        }

        // ==========================================================
        // 录制
        // ==========================================================

        private void LateUpdate()
        {
            if (reviewing)
            {
                // 暂停期间每帧重摆一次，免得被刚体插值之类的写回去
                if (rewindPose && count > 0) ApplyPose(GetSnap(viewBack));
                return;
            }

            if (!record && !show) return;

            int size = record ? Mathf.Max(60, recordFrames) : 1;
            if (buffer == null || buffer.Length != size)
            {
                buffer = new Snap[size];
                head = 0;
                count = 0;
            }

            buffer[head] = Capture();
            head = (head + 1) % buffer.Length;
            if (count < buffer.Length) count++;
        }

        /// <summary>取倒数第 back 帧（0 = 最新）。调用前保证 0 &lt;= back &lt; count</summary>
        private Snap GetSnap(int back)
        {
            int n = buffer.Length;
            return buffer[((head - 1 - back) % n + n) % n];
        }

        private Snap Capture()
        {
            Snap s = default;
            s.frame = Time.frameCount;
            s.time = Time.time;
            s.pos = transform.position;
            s.rot = transform.rotation;
            s.sprite = sr != null ? sr.sprite : null;
            s.velocity = rb != null ? rb.linearVelocity : Vector2.zero;

            if (sm != null)
            {
                s.state = sm.currentState != null ? sm.currentState.GetType().Name : "无";
                s.flying = sm.IsFlying;
                s.dashCd = sm.dashSkill != null ? sm.dashSkill.GetRemainingCooldown() : 0f;
                s.slideCd = sm.slideSkill != null ? sm.slideSkill.GetRemainingCooldown() : 0f;
            }

            if (combo != null)
            {
                s.depth = combo.comboDepth;
                s.window = combo.ComboWindowRemaining;
                s.node = combo.currentNode != null ? combo.currentNode.nodeName : null;
                s.phase = combo.CurrentAttackPhase;

                AttackInstance inst = combo.CurrentInstance;
                if (inst != null && inst.alive)
                {
                    s.instAlive = true;
                    s.elapsed = inst.elapsed;
                    s.duration = inst.duration;
                    s.windupEnd = inst.windupEnd;
                    s.activeEnd = inst.activeEnd;
                }

                s.main = CaptureHand(InputCmd.MainAttack, WeaponSlot.Main, controller != null && controller.isMainAttackHeld);
                s.sub = CaptureHand(InputCmd.SubAttack, WeaponSlot.Sub, controller != null && controller.isSubAttackHeld);
            }

            return s;
        }

        private HandSnap CaptureHand(InputCmd cmd, WeaponSlot slot, bool held)
        {
            HandSnap h = default;
            h.held = held;

            if (combo.IsHandReady(cmd)) h.busy = 0;
            else if (combo.IsHandAttacking(cmd)) h.busy = 1;
            else
            {
                h.busy = 2;
                h.recoveryRemain = combo.GetHandRecoveryRemaining(cmd);
            }

            ChargeModule m = charge != null ? charge.GetModule(slot) : null;
            if (m != null && m.IsCharging)
            {
                h.charging = true;
                h.valid = m.IsValid;
                if (m.IsValid)
                {
                    h.level = m.CurrentLevel;
                    h.cap = m.CapLevel;
                    h.start = m.StartLevel;
                    h.progress = m.Progress;
                    h.notStarted = m.IsLocked;
                }
            }
            return h;
        }

        // ==========================================================
        // 回看
        // ==========================================================

        private void OnReviewCommand(DebugReviewCommand cmd)
        {
            if (cmd == DebugReviewCommand.Toggle)
            {
                if (reviewing) ExitReview();
                else if (record && count > 0) EnterReview();
                return;
            }

            if (!reviewing) return;

            switch (cmd)
            {
                case DebugReviewCommand.PrevFrame:
                    viewBack = Mathf.Min(viewBack + 1, count - 1);
                    break;

                case DebugReviewCommand.NextFrame:
                    viewBack = Mathf.Max(viewBack - 1, 0);
                    break;

                case DebugReviewCommand.PrevChange:
                    for (int b = viewBack + 1; b < count - 1; b++)
                    {
                        if (IsChange(GetSnap(b + 1), GetSnap(b))) { viewBack = b; break; }
                    }
                    break;

                case DebugReviewCommand.NextChange:
                    for (int b = viewBack - 1; b >= 0; b--)
                    {
                        if (b + 1 < count && IsChange(GetSnap(b + 1), GetSnap(b))) { viewBack = b; break; }
                    }
                    break;
            }
        }

        private void EnterReview()
        {
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            reviewing = true;
            viewBack = 0;
        }

        private void ExitReview()
        {
            // 先把角色摆回暂停那一刻（最新一帧），再放开时间
            if (rewindPose && count > 0) ApplyPose(GetSnap(0));
            Time.timeScale = savedTimeScale;
            reviewing = false;
            viewBack = 0;
        }

        private void ApplyPose(Snap s)
        {
            transform.position = s.pos;
            transform.rotation = s.rot;
            if (sr != null) sr.sprite = s.sprite;
        }

        /// <summary>从 prev 到 cur 有没有值得停下来看的变化</summary>
        private static bool IsChange(Snap prev, Snap cur)
        {
            return prev.state != cur.state
                || prev.node != cur.node
                || prev.depth != cur.depth
                || prev.phase != cur.phase
                || prev.flying != cur.flying
                || IsHandChange(prev.main, cur.main)
                || IsHandChange(prev.sub, cur.sub);
        }

        private static bool IsHandChange(HandSnap a, HandSnap b)
        {
            return a.busy != b.busy || a.held != b.held || a.charging != b.charging
                || a.level != b.level || a.cap != b.cap;
        }

        // ==========================================================
        // 绘制
        // ==========================================================

        private void OnGUI()
        {
            if (!show && !reviewing) return;
            if (buffer == null || count == 0) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.UpperLeft };
                style.normal.textColor = Color.white;
            }
            style.fontSize = fontSize;

            int back = reviewing ? viewBack : 0;
            Snap cur = GetSnap(back);

            sb.Length = 0;
            if (reviewing)
            {
                sb.Append("<color=#ffe066><b>回看</b>  倒数第 ").Append(back).Append(" 帧 / 共录 ").Append(count).Append(" 帧</color>\n");
                sb.Append("F6 上一帧  F7 下一帧  F8 上一次变化  F9 下一次变化  F5 继续\n");
                if (back + 1 < count) AppendChanges(GetSnap(back + 1), cur);
            }
            AppendSnap(cur);

            content.text = sb.ToString();
            Vector2 size = style.CalcSize(content);
            Rect rect = new Rect(position.x, position.y, size.x + 16f, size.y + 12f);

            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;

            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, size.x, size.y), content, style);
        }

        private void AppendSnap(Snap s)
        {
            sb.Append("<b>帧</b>  ").Append(s.frame).Append("   t = ").Append(s.time.ToString("F3")).Append("s\n");

            sb.Append("<b>状态</b>  ").Append(s.state ?? "无");
            if (s.flying) sb.Append("  <color=#7fd4ff>[飞行]</color>");
            sb.Append("   速度 (").Append(s.velocity.x.ToString("F1")).Append(", ").Append(s.velocity.y.ToString("F1")).Append(")\n");

            sb.Append("<b>连段</b>  深度 ").Append(s.depth).Append(" / ").Append(ComboInputBuffer.MaxComboDepth);
            sb.Append("   窗口剩 ").Append(s.window.ToString("F2")).Append("s\n");

            sb.Append("<b>当前招</b>  ").Append(s.node ?? "—").Append("   阶段 ").Append(PhaseText(s.phase));
            if (s.instAlive)
            {
                sb.Append("   ").Append(s.elapsed.ToString("F2")).Append(" / ").Append(s.duration.ToString("F2")).Append("s");
                sb.Append("  (前摇到 ").Append(s.windupEnd.ToString("F2"));
                sb.Append("，判定到 ").Append(s.activeEnd.ToString("F2")).Append(")");
            }
            sb.Append('\n');

            AppendHand("主手", s.main);
            AppendHand("副手", s.sub);

            sb.Append("<b>冷却</b>  冲刺 ").Append(CooldownText(s.dashCd));
            sb.Append("   滑铲 ").Append(CooldownText(s.slideCd));
        }

        private void AppendHand(string label, HandSnap h)
        {
            sb.Append("<b>").Append(label).Append("</b>  ");
            sb.Append(h.held ? "<color=#ffffff>[按住]</color> " : "[松开] ");

            if (h.busy == 0) sb.Append("<color=#8fe38f>空闲</color>");
            else if (h.busy == 1) sb.Append("<color=#ffb347>出招中</color>");
            else sb.Append("<color=#ffb347>后摇 ").Append(h.recoveryRemain.ToString("F2")).Append("s</color>");

            if (h.charging)
            {
                sb.Append("   <color=#ffe066>蓄力</color> ");
                if (!h.valid)
                {
                    sb.Append("无效（没装武器）");
                }
                else
                {
                    sb.Append(h.level).Append(" 级");
                    sb.Append("  上限 ").Append(h.cap);
                    sb.Append("  起步 ").Append(h.start);
                    sb.Append("  进度 ").Append(Mathf.RoundToInt(h.progress * 100f)).Append("%");
                    if (h.notStarted) sb.Append("  (未过起步时间)");
                }
            }
            sb.Append('\n');
        }

        /// <summary>写一行「本帧变化」：这一帧相对上一帧变了什么</summary>
        private void AppendChanges(Snap prev, Snap cur)
        {
            int start = sb.Length;
            sb.Append("<color=#ff9f7f><b>本帧变化</b>  ");
            int mark = sb.Length;

            if (prev.state != cur.state) sb.Append("状态 ").Append(prev.state ?? "无").Append("→").Append(cur.state ?? "无").Append("；");
            if (prev.node != cur.node) sb.Append("招式 ").Append(prev.node ?? "—").Append("→").Append(cur.node ?? "—").Append("；");
            if (prev.depth != cur.depth) sb.Append("深度 ").Append(prev.depth).Append("→").Append(cur.depth).Append("；");
            if (prev.phase != cur.phase) sb.Append("阶段 ").Append(PhasePlain(prev.phase)).Append("→").Append(PhasePlain(cur.phase)).Append("；");
            if (prev.flying != cur.flying) sb.Append(cur.flying ? "起飞；" : "落下；");
            AppendHandChanges("主手", prev.main, cur.main);
            AppendHandChanges("副手", prev.sub, cur.sub);

            if (sb.Length == mark) sb.Length = start;   // 这一帧没变化，整行不写
            else sb.Append("</color>\n");
        }

        private void AppendHandChanges(string label, HandSnap a, HandSnap b)
        {
            if (a.held != b.held) sb.Append(label).Append(b.held ? "按下；" : "松开；");
            if (a.busy != b.busy) sb.Append(label).Append(BusyText(a.busy)).Append("→").Append(BusyText(b.busy)).Append("；");
            if (a.charging != b.charging) sb.Append(label).Append(b.charging ? "开始蓄力；" : "结束蓄力；");
            else if (b.charging)
            {
                if (a.level != b.level) sb.Append(label).Append("蓄力 ").Append(a.level).Append("→").Append(b.level).Append(" 级；");
                if (a.cap != b.cap) sb.Append(label).Append("上限 ").Append(a.cap).Append("→").Append(b.cap).Append("；");
            }
        }

        private static string BusyText(byte busy)
        {
            return busy == 0 ? "空闲" : busy == 1 ? "出招中" : "后摇";
        }

        private static string PhasePlain(AttackPhase phase)
        {
            switch (phase)
            {
                case AttackPhase.Windup: return "前摇";
                case AttackPhase.Active: return "判定";
                case AttackPhase.Recovery: return "后摇";
                default: return "—";
            }
        }

        private static string PhaseText(AttackPhase phase)
        {
            switch (phase)
            {
                case AttackPhase.Windup: return "<color=#7fd4ff>前摇</color>";
                case AttackPhase.Active: return "<color=#ff7f7f>判定</color>";
                case AttackPhase.Recovery: return "<color=#8fe38f>后摇</color>";
                default: return "—";
            }
        }

        private static string CooldownText(float remain)
        {
            return remain > 0f ? remain.ToString("F2") + "s" : "<color=#8fe38f>好了</color>";
        }
#endif
    }
}
