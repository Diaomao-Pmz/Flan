using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【指令路由器】—— 走廊里的前台。
    ///
    /// 两个核心职责：
    ///   1. 守卫前置：切状态【之前】就问闸门，不通过就不切
    ///   2. 预输入宽恕：闸门没开就存缓存，开的那一帧自动消费
    ///
    /// ==========================================================
    /// 【手感修订 · 滑铲/蹲下的落地判定】
    ///
    /// 修订前用的判定是：
    ///     if (sm.currentState == sm.runState && slideSkill.CanExecute()) → 滑铲
    ///
    /// currentState == runState 只是"玩家在跑"的【代理指标】，不是事实本身。
    /// 落地那一帧 FallState 还没来得及把状态切成 RunState，
    /// 而两个 Update() 之间没有执行顺序保证 ——
    /// 于是缓存兑现时看到的还是 FallState，条件不成立，全都落到蹲下。
    ///
    /// 比喻：门卫不看访客本人，而是看"走廊监控里他有没有在跑"。
    ///       监控画面延迟一帧，判断就全错。应该直接看他脚往哪儿迈。
    ///
    /// 修订后直接看 moveInput.x（方向键有没有按着），
    /// 并且新增"落地帧判定"：落地那一刻手上按着什么，就出什么。
    ///
    /// 期望行为：
    ///   下落中按着 Ctrl + 方向  → 落地滑铲
    ///   下落中只按着 Ctrl       → 落地蹲下
    ///   下落中只按着方向        → 落地跑动（路由器不介入）
    /// ==========================================================
    /// </summary>
    [RequireComponent(typeof(PlayerStateMachine))]
    public class PlayerCommandRouter : MonoBehaviour
    {
        [Header("预输入宽恕期")]
        [Tooltip("跳跃指令的缓存时长（秒）")]
        public float jumpBufferTime = 0.15f;

        [Tooltip("冲刺指令的缓存时长（秒）")]
        public float dashBufferTime = 0.15f;

        [Tooltip("蹲/铲【点按】的缓存时长（秒）。按住不放的情况见下方开关")]
        public float crouchBufferTime = 0.15f;

        [Header("落地判定")]
        [Tooltip(
            "开启后：落地瞬间只要还按着 Ctrl，就按当时的方向键决定滑铲还是蹲下，" +
            "不受上方缓存时长限制（推荐，符合「按住即意图」的直觉）。\n" +
            "关闭后：退化为纯缓存模式，必须在落地前 crouchBufferTime 内按下才生效。")]
        public bool useHeldCrouchOnLanding = true;

        [Tooltip("方向键按到多大才算「有方向」。用于区分滑铲与蹲下")]
        [Range(0.05f, 0.9f)]
        public float directionThreshold = 0.1f;

        [Header("Debug")]
        [Tooltip("打开后会在 Console 输出每次指令的裁决过程")]
        public bool verboseLog = false;

        private PlayerStateMachine sm;
        private PlayerController controller;
        private LoadoutManager loadout;
        private ComboInputBuffer comboBuffer;

        private readonly InputBufferQueue buffer = new InputBufferQueue(4);

        // 落地帧检测
        private bool wasGroundedLastFrame = true;

        private void Awake()
        {
            sm = GetComponent<PlayerStateMachine>();
            controller = GetComponent<PlayerController>();
            loadout = GetComponent<LoadoutManager>();
            comboBuffer = GetComponent<ComboInputBuffer>();
        }

        private void Update()
        {
            buffer.Tick();

            bool grounded = sm.IsGrounded();

            // ---- 落地帧：这一刻手上按着什么，就出什么 ----
            if (grounded && !wasGroundedLastFrame)
            {
                HandleLandingFrame();
            }
            wasGroundedLastFrame = grounded;

            // ---- 常规：尝试兑现缓存里的指令 ----
            TryFlushBuffer();
        }

        // ==========================================================
        // 对外入口：由 PlayerController 在按键瞬间调用
        // ==========================================================

        public void OnCommand(InputCmd cmd)
        {
            // ---- 受击硬直：只有能受身的宝石才放行 ----
            if (sm.currentState == sm.hitState)
            {
                TryBreakHitStun(cmd);
                return;
            }

            if (TryDispatch(cmd)) return;

            // 闸门没开 → 存进缓存，等它开
            buffer.Push(cmd, GetBufferTime(cmd));
            if (verboseLog) Debug.Log($"[路由器] {cmd} 闸门未开，已存入预输入缓存");
        }

        // ==========================================================
        // 落地帧判定
        // ==========================================================

        /// <summary>
        /// 从空中落到地面的那一帧。
        ///
        /// 这里不看缓存、不看 currentState，只看两件事实：
        ///   Ctrl 还按着吗？方向键还按着吗？
        ///
        /// 之所以要单独处理落地帧，是因为「按住 Ctrl 下落」这个意图
        /// 可能持续好几秒，用 0.15 秒的缓存表达不了。
        /// </summary>
        private void HandleLandingFrame()
        {
            if (!useHeldCrouchOnLanding) return;
            if (controller == null || !controller.isCrouchHeld) return;

            if (verboseLog) Debug.Log("[路由器] 落地帧检测到 Ctrl 按住，进入蹲/铲裁决");

            // 这条指令已经由落地帧处理掉了，清掉缓存里的重复项
            buffer.TryConsume(InputCmd.Crouch);
            buffer.TryConsume(InputCmd.Slide);

            DispatchCrouchOrSlideOnGround();
        }

        // ==========================================================
        // 派发核心
        // ==========================================================

        /// <returns>true = 已经成功切了状态；false = 闸门未开</returns>
        private bool TryDispatch(InputCmd cmd)
        {
            // 【批次F 新增】取消权限：当前招式允许被位移动作打断吗？
            //
            // 原先跳/冲/铲可以无条件打断任何招式。
            // 现在由 ComboNode.cancelPermission.byMovement 控制 ——
            // 默认全部为 true，行为与之前一致；
            // 想做「大招收招硬直不可取消」这类设计时，取消勾选即可。
            if (sm.currentState == sm.comboState
                && comboBuffer != null
                && !comboBuffer.CanCurrentNodeBeCanceledByMovement())
            {
                if (verboseLog) Debug.Log($"[路由器] 当前招式不允许被位移打断，{cmd} 已拦截");
                return false;
            }

            switch (cmd)
            {
                case InputCmd.Jump: return TryJump();
                case InputCmd.Dash: return TryDash();

                case InputCmd.Crouch:
                case InputCmd.Slide:     // 兼容旧枚举，后续删除
                    return TryCrouchOrSlide();

                default: return false;
            }
        }

        private void TryFlushBuffer()
        {
            if (buffer.Count == 0) return;

            // 受击中不消费缓存，避免硬直一结束就自动冲出去
            if (sm.currentState == sm.hitState) return;

            if (buffer.Contains(InputCmd.Jump) && TryJump())
            {
                buffer.TryConsume(InputCmd.Jump);
                return;
            }

            if (buffer.Contains(InputCmd.Dash) && TryDash())
            {
                buffer.TryConsume(InputCmd.Dash);
                return;
            }

            if (buffer.Contains(InputCmd.Crouch) && TryCrouchOrSlide())
            {
                buffer.TryConsume(InputCmd.Crouch);
                return;
            }
        }

        // ==========================================================
        // 闸门（守卫前置）
        // ==========================================================

        private bool TryJump()
        {
            if (sm.jumpCount >= sm.maxJumps) return false;

            if (verboseLog) Debug.Log($"[路由器] Jump 放行 ({sm.jumpCount + 1}/{sm.maxJumps})");
            sm.ChangeState(sm.jumpState);
            return true;
        }

        private bool TryDash()
        {
            if (!sm.dashSkill.CanExecute()) return false;

            if (verboseLog) Debug.Log("[路由器] Dash 放行");
            sm.ChangeState(sm.dashState);
            return true;
        }

        /// <summary>C 键的三岔口</summary>
        private bool TryCrouchOrSlide()
        {
            if (!sm.IsGrounded())
            {
                return TryAirCommand(InputCmd.Crouch);
            }

            DispatchCrouchOrSlideOnGround();
            return true;
        }

        /// <summary>
        /// 地面上的蹲/铲裁决。落地帧与常规按键共用同一套判断。
        ///
        /// 【关键】用 moveInput.x 而不是 currentState == runState。
        /// 前者是事实（方向键按着没有），后者是延迟一帧的代理指标。
        /// </summary>
        private void DispatchCrouchOrSlideOnGround()
        {
            // 已经在蹲/铲了就别重复进入（会重置碰撞体，产生抖动）
            if (sm.currentState == sm.crouchState || sm.currentState == sm.slideState) return;

            bool hasDirection = Mathf.Abs(controller.moveInput.x) > directionThreshold;

            if (hasDirection && sm.slideSkill.CanExecute())
            {
                if (verboseLog) Debug.Log("[路由器] Ctrl + 方向 → 滑铲");
                sm.ChangeState(sm.slideState);
                return;
            }

            // 没有方向键，或滑铲还在 CD → 蹲下
            if (verboseLog)
            {
                Debug.Log(hasDirection
                    ? "[路由器] 有方向但滑铲 CD 中 → 蹲下"
                    : "[路由器] 仅 Ctrl → 蹲下");
            }
            sm.ChangeState(sm.crouchState);
        }

        // ==========================================================
        // 【扩展点】空中指令
        // ==========================================================

        /// <summary>
        /// 空中按 C 该做什么 —— 留好的接口，尚未实现具体动作。
        ///
        /// 填法 A：由铲槽宝石决定（推荐，不需要新建状态卡带）
        ///   在对应宝石里覆写 TryHandleAirCommand() 即可，本文件一行不用改。
        ///
        /// 填法 B：固定动作（所有人一样，比如下砸）
        ///   新建 AirSlamState 卡带，把下方 TODO 换成
        ///   sm.ChangeState(sm.airSlamState); return true;
        ///
        /// 当前行为：宝石不接管就返回 false。
        /// 此时指令会留在缓存里、落地后仍可能兑现 —— 这是有意的，
        /// 正是"空中按 C 落地滑铲"这一手感的来源之一。
        /// </summary>
        private bool TryAirCommand(InputCmd cmd)
        {
            if (loadout != null && loadout.TryHandleAirCommand(cmd))
            {
                if (verboseLog) Debug.Log($"[路由器] 空中 {cmd} 已被宝石接管");
                return true;
            }

            // TODO: 填法 B 的入口 —— 想做固定的空中动作（下砸等）就写在这里
            // sm.ChangeState(sm.airSlamState);
            // return true;

            return false;
        }

        // ==========================================================
        // 受身打断
        // ==========================================================

        private void TryBreakHitStun(InputCmd cmd)
        {
            if (loadout == null || !loadout.CanBreakHitStun(cmd)) return;

            // 一次性标记：让宝石区分"受身"与"常规使用该动作"
            sm.isBreakingHitStun = true;

            bool dispatched = TryDispatch(cmd);

            if (!dispatched) sm.isBreakingHitStun = false;
            else if (verboseLog) Debug.Log($"[路由器] 受身成功，{cmd} 打断硬直");
        }

        // ==========================================================
        // 杂项
        // ==========================================================

        private float GetBufferTime(InputCmd cmd)
        {
            switch (cmd)
            {
                case InputCmd.Jump: return jumpBufferTime;
                case InputCmd.Dash: return dashBufferTime;
                case InputCmd.Crouch:
                case InputCmd.Slide: return crouchBufferTime;
                default: return 0f;
            }
        }

        /// <summary>清空缓存。受击、死亡、切场景时调用，防止脏指令残留</summary>
        public void ClearBuffer() => buffer.Clear();
    }
}