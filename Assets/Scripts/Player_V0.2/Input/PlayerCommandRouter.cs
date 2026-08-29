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
    /// 【本次修复】缓存兑现绕过了派发判断
    ///
    /// 症状：蓄力中按 shift 想突刺，但冲刺正在 CD →
    ///       起势失败 → 指令被存进缓存 →
    ///       0.15 秒后 CD 一好，它以【普通冲刺】的身份兑现 →
    ///       角色莫名其妙冲出去，打断了正在蓄的力。
    ///
    /// 根因：TryFlushBuffer 直接调 TryDash()，绕过了突刺判断与取消权限校验，
    ///       等于缓存和即时派发走了两条不同的规则。
    ///
    /// 比喻：前台判断"这位访客不该放行"，但没把便条撕掉。
    ///       半小时后换班的人捡到便条，照着放行了。
    ///
    /// 修法两条：
    ///   ① 缓存兑现和即时派发【共用同一个 TryDispatch】，规则只有一份
    ///   ② 派发结果区分三态 —— 成功 / 闸门没开(该等) / 故意拒绝(该丢弃)
    ///      "故意拒绝"的指令不进缓存，不会在几帧后借尸还魂
    /// ==========================================================
    /// </summary>
    [RequireComponent(typeof(PlayerStateMachine))]
    public class PlayerCommandRouter : MonoBehaviour
    {
        /// <summary>派发结果三态</summary>
        private enum DispatchResult
        {
            /// <summary>成功切了状态</summary>
            Success,

            /// <summary>闸门没开，但以后可能开 —— 存进缓存等一等</summary>
            Retry,

            /// <summary>本次输入被【故意】拒绝，不该在几帧后再兑现 —— 直接丢弃</summary>
            Rejected
        }

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

        [Tooltip("方向键按到多大才算「有方向」。用于区分滑铲与蹲下、突刺与冲刺")]
        [Range(0.05f, 0.9f)]
        public float directionThreshold = 0.1f;

        [Header("Debug")]
        [Tooltip("打开后会在 Console 输出每次指令的裁决过程")]
        public bool verboseLog = false;

        private PlayerStateMachine sm;
        private PlayerController controller;
        private LoadoutManager loadout;
        private ComboInputBuffer comboBuffer;
        private PlayerChargeSystem chargeSystem;
        private PlayerAimProvider aim;

        private readonly InputBufferQueue buffer = new InputBufferQueue(4);

        private bool wasGroundedLastFrame = true;

        private void Awake()
        {
            sm = GetComponent<PlayerStateMachine>();
            controller = GetComponent<PlayerController>();
            loadout = GetComponent<LoadoutManager>();
            comboBuffer = GetComponent<ComboInputBuffer>();
            chargeSystem = GetComponent<PlayerChargeSystem>();
            aim = GetComponent<PlayerAimProvider>();
        }

        private void Update()
        {
            buffer.Tick();

            bool grounded = sm.IsGrounded();

            // 落地帧：这一刻手上按着什么，就出什么
            if (grounded && !wasGroundedLastFrame) HandleLandingFrame();
            wasGroundedLastFrame = grounded;

            TryFlushBuffer();
        }

        // ==========================================================
        // 对外入口
        // ==========================================================

        public void OnCommand(InputCmd cmd)
        {
            // 受击硬直：只有能受身的宝石才放行
            if (sm.currentState == sm.hitState)
            {
                TryBreakHitStun(cmd);
                return;
            }

            DispatchResult result = TryDispatch(cmd);

            if (result == DispatchResult.Success) return;

            // 【关键】只有"闸门没开"才进缓存。
            // "故意拒绝"的指令直接丢弃，不会在几帧后借尸还魂。
            if (result == DispatchResult.Retry)
            {
                buffer.Push(cmd, GetBufferTime(cmd));
                if (verboseLog) Debug.Log($"[路由器] {cmd} 闸门未开，已存入预输入缓存");
            }
            else if (verboseLog)
            {
                Debug.Log($"[路由器] {cmd} 被拒绝，不进缓存");
            }
        }

        // ==========================================================
        // 缓存兑现
        // ==========================================================

        private void TryFlushBuffer()
        {
            if (buffer.Count == 0) return;

            // 受击中不消费缓存，避免硬直一结束就自动冲出去
            if (sm.currentState == sm.hitState) return;

            TryFlushOne(InputCmd.Jump);
            TryFlushOne(InputCmd.Dash);
            TryFlushOne(InputCmd.Crouch);
        }

        /// <summary>
        /// 兑现走的是和即时派发【完全相同】的 TryDispatch ——
        /// 这是本次修复的核心：规则只有一份，不会出现"当场不让、过会儿放行"。
        /// </summary>
        private void TryFlushOne(InputCmd cmd)
        {
            if (!buffer.Contains(cmd)) return;

            DispatchResult result = TryDispatch(cmd);

            // 成功要消费；被拒绝也要消费（否则它会一直赖在缓存里反复尝试）
            if (result != DispatchResult.Retry) buffer.TryConsume(cmd);
        }

        // ==========================================================
        // 派发核心
        // ==========================================================

        /// <summary>玩家当前是否处于战斗姿态（连招中或蓄力架势中）</summary>
        /// <summary>
        /// 【P3 改动】战斗姿态只剩「攻击动画中」。
        ///
        /// 蓄力不再是一个 State，所以不能再用 currentState 判断 ——
        /// 而且蓄力期间玩家本来就该能正常跑动跳跃，
        /// 把它算作"战斗姿态"反而会误伤这些行为。
        /// </summary>
        private bool IsInCombatStance
            => sm.currentState == sm.comboState;

        /// <summary>方向键有没有按着 —— 突刺与冲刺、蹲下与滑铲的唯一分流依据</summary>
        private bool HasDirection
            => controller != null && Mathf.Abs(controller.moveInput.x) > directionThreshold;

        private DispatchResult TryDispatch(InputCmd cmd)
        {
            switch (cmd)
            {
                case InputCmd.Jump: return TryJumpWithCancelCheck();
                case InputCmd.Dash: return TryDashOrThrust();

                case InputCmd.Crouch:
                case InputCmd.Slide:     // 兼容旧枚举，后续删除
                    return TryCrouchSlideOrCancel();

                case InputCmd.Fly: return TryFlyDerivation();

                default: return DispatchResult.Rejected;
            }
        }

        // ==========================================================
        // shift 二义：方向+shift = 打断冲刺 / 单按 shift = 突刺
        // ==========================================================

        /// <summary>
        /// 【P1b 重写】攻击动画中的 Shift。
        ///
        /// 新规则很简单：**攻击动画中按 Shift 一律位移，并且打断连段。**
        ///   单按     → 朝角色当前面朝方向冲
        ///   方向+shift → 朝方向键方向冲（顺带改变朝向）
        ///
        /// 已删除的旧行为：
        ///   「单按 = 突刺（不打断连段）」—— 新规则表里没有突刺这一项
        ///   「蓄力中单按 = 跳级」—— 蓄力只能升一级后，跳级设计已冗余
        ///
        /// 蓄力中的 Shift 只是单纯位移，【不打断蓄力】。
        /// </summary>
        /// <summary>有任意一只手在蓄力</summary>
        private bool IsCharging => chargeSystem != null && chargeSystem.IsAnyCharging;

        private DispatchResult TryDashOrThrust()
        {
            // ---- 空中蓄力：朝鼠标方向冲刺 ----
            //
            // 空中蓄力时方向键被锁（见 Jump/FallState），
            // 想调整位置只能花这一次冲刺 —— 高风险承诺换一次精准位移。
            if (IsCharging && !sm.IsGrounded())
            {
                return TryChargeAirDash();
            }

            // 【P3 起】不再需要为蓄力单独开分支 ——
            // 蓄力不是状态了，冲刺只是普通的状态切换，天然不会打断它。

            // ---- 攻击动画中：位移 + 打断连段 ----
            if (sm.currentState == sm.comboState)
            {
                if (!CanCancelCurrentAttackByMovement())
                {
                    if (verboseLog) Debug.Log("[路由器] 当前招式不允许被位移打断");
                    return DispatchResult.Rejected;
                }

                DispatchResult r = TryDash();

                // 只有真的冲出去了才算打断 —— CD 中被拦下时连段应该保住
                if (r == DispatchResult.Success)
                {
                    if (verboseLog) Debug.Log("[路由器] 攻击中 shift → 位移并打断连段");
                    comboBuffer?.ResetCombo();
                }
                return r;
            }

            // ---- 连段间隔中 / 非战斗：普通冲刺，不碰连段 ----
            return TryDash();
        }

        /// <summary>
        /// 【P1b 重写】跳跃。
        ///
        /// **攻击动画中完全不能跳** —— 这是新规则里跳跃与冲刺/滑铲最大的区别：
        /// 后两者能用（代价是打断连段），跳跃则是彻底禁用。
        ///
        /// 但【连段间隔中】可以自由跳，而且不会中断连段 ——
        /// 「A1 放完 → 跳 → 接 b2」是合法且鼓励的操作。
        /// </summary>
        /// <summary>
        /// 空中蓄力时的冲刺：朝【鼠标方向】任意角度冲出去。
        ///
        /// 【宝石扩展点】先问宝石要不要接管这次空中行为 ——
        /// 以后想让某颗宝石把"空中蓄力冲刺"改成别的（瞬移、下砸、留残影…），
        /// 只需要在那颗宝石里覆写 TryHandleAirCommand，本文件一行不用改。
        /// </summary>
        private DispatchResult TryChargeAirDash()
        {
            // 扩展点：宝石优先
            if (loadout != null && loadout.TryHandleAirCommand(InputCmd.Dash))
            {
                if (verboseLog) Debug.Log("[路由器] 空中蓄力冲刺已被宝石接管");
                return DispatchResult.Success;
            }

            if (!sm.dashSkill.CanExecute()) return DispatchResult.Retry;

            Vector2 dir = (aim != null)
                ? aim.AimDirection
                : new Vector2(controller.facingDirection, 0f);

            sm.dashState.SetNextDashDirection(dir);
            sm.ChangeState(sm.dashState);

            if (verboseLog) Debug.Log($"[路由器] 空中蓄力 → 朝鼠标冲刺 {dir}");
            return DispatchResult.Success;
        }

        private DispatchResult TryJumpWithCancelCheck()
        {
            if (sm.currentState == sm.comboState)
            {
                if (verboseLog) Debug.Log("[路由器] 攻击动画中禁止跳跃");
                return DispatchResult.Rejected;
            }

            // 【P3 起放开】蓄力中可以跳跃。
            //
            // 蓄力已经不是一个 State 了 —— 玩家蓄力时仍然待在 Idle/Run/Fall 里，
            // 跳跃只是普通的状态切换，不会碰到蓄力模块。
            // 跳跃高度的折损由 PlayerChargeSystem 贴在 jumpForce 上的修饰器负责。
            return TryJump();
        }

        private bool CanCancelCurrentAttackByMovement()
            => comboBuffer == null || comboBuffer.CanCurrentNodeBeCanceledByMovement();

        // ==========================================================
        // fly 衍生
        // ==========================================================

        /// <summary>
        /// 【点按 Q】打出蓄力招后跃起直接进飞行。
        ///
        /// 触发条件是"刚打出的是一个蓄力招" —— 用 currentNode.isChargeSkill 判断。
        /// 因为连招指针在窗口期内一直存活（批次F 的闲置计时器管理），
        /// 所以 AAn 收招后的一小段时间里点 Q 都有效，不需要卡在动画帧上。
        ///
        /// 落点由【按下那一刻】的方向键决定，跃起途中改方向不再影响位移 ——
        /// 这是设计明确要求的。但朝向仍可调整，所以还有操作空间。
        /// </summary>
        private DispatchResult TryFlyDerivation()
        {
            if (comboBuffer == null || comboBuffer.currentNode == null)
                return DispatchResult.Rejected;

            if (!comboBuffer.currentNode.isChargeSkill)
            {
                if (verboseLog) Debug.Log("[路由器] 点按 Q 但上一招不是蓄力招，忽略");
                return DispatchResult.Rejected;
            }

            // 蓝不够就别起飞，否则刚跃出去就因为耗蓝失败掉下来
            if (sm.playerState != null && sm.playerState.health.currentMP <= 0)
            {
                if (verboseLog) Debug.Log("[路由器] 点按 Q 但 MP 不足");
                return DispatchResult.Rejected;
            }

            Vector2 dir = controller != null ? controller.moveInput : Vector2.zero;

            sm.flyState.PrepareLeap(dir);
            sm.ChangeState(sm.flyState);

            if (verboseLog) Debug.Log($"[路由器] 蓄力招衍生 → 跃起进飞行（方向 {dir}）");
            return DispatchResult.Success;
        }

        // ==========================================================
        // ctrl 三态
        // ==========================================================

        /// <summary>
        /// 【P1b 重写】Ctrl 在不同时机下的三种含义。
        ///
        /// | 时机 | 输入 | 行为 | 连段 |
        /// |---|---|---|---|
        /// | 蓄力中 | 任意 | 滑铲位移，蓄力不中断 | — |
        /// | 攻击动画中 | 单按 | 【碰撞箱微调】，由 ComboState 持续处理 | 保留 |
        /// | 攻击动画中 | 方向+ | 滑铲位移 | **打断** |
        /// | 其他 | — | 蹲下 / 滑铲 | 保留 |
        ///
        /// 「单按微调身位」和「带方向滑铲」是两件不同的事：
        /// 前者是原地躲擦边攻击，不该有代价；
        /// 后者是主动位移，付出打断连段的代价换来脱离。
        /// </summary>
        private DispatchResult TryCrouchSlideOrCancel()
        {
            // ---- 空中蓄力：禁止滑铲 ----
            //
            // 空中蓄力期间唯一的位移手段是 Shift 冲刺，
            // 让 Ctrl 也能位移会削弱"空中蓄力是高风险承诺"这个设计。
            if (IsCharging && !sm.IsGrounded())
            {
                if (verboseLog) Debug.Log("[路由器] 空中蓄力时禁止滑铲");
                return DispatchResult.Rejected;
            }

            // ---- 攻击动画中 ----
            if (sm.currentState == sm.comboState)
            {
                // 单按（无方向）→ 碰撞箱微调。
                // 这件事【不切状态】，所以路由器不做任何事 ——
                // ComboState 每帧读 isCrouchHeld 自己处理，
                // 也只有它知道攻击结束时该把碰撞箱还原。
                if (!HasDirection)
                {
                    if (verboseLog) Debug.Log("[路由器] 攻击中单按 ctrl → 碰撞箱微调（交给 ComboState）");
                    return DispatchResult.Rejected;   // 不进缓存，避免几帧后跑出来滑铲
                }

                if (!CanCancelCurrentAttackByMovement())
                {
                    if (verboseLog) Debug.Log("[路由器] 当前招式不允许被位移打断");
                    return DispatchResult.Rejected;
                }

                DispatchResult r = TryCrouchOrSlide();

                if (r == DispatchResult.Success)
                {
                    if (verboseLog) Debug.Log("[路由器] 攻击中 方向+ctrl → 滑铲并打断连段");
                    comboBuffer?.ResetCombo();
                }
                return r;
            }

            // ---- 连段间隔中 / 非战斗 ----
            return TryCrouchOrSlide();
        }

        // ==========================================================
        // 闸门（守卫前置）
        // ==========================================================

        private DispatchResult TryJump()
        {
            if (sm.jumpCount >= sm.maxJumps) return DispatchResult.Retry;

            if (verboseLog) Debug.Log($"[路由器] Jump 放行 ({sm.jumpCount + 1}/{sm.maxJumps})");
            sm.ChangeState(sm.jumpState);
            return DispatchResult.Success;
        }

        private DispatchResult TryDash()
        {
            if (!sm.dashSkill.CanExecute()) return DispatchResult.Retry;

            // 【修复】按方向键先把朝向定好，再切状态。
            //
            // DashState 读的是 facingDirection，而 SlideState 读的是 moveInput.x ——
            // 所以以前「方向+ctrl」正常、「方向+shift」却总是朝面朝方向冲。
            //
            // 以前不明显是因为跑动时朝向一直跟着方向键走；
            // 但 P1b 删掉了「攻击中按方向键转向」之后朝向被冻住，
            // 冲刺就用上了过期的朝向。
            ApplyAimFacingFromInput();

            if (verboseLog) Debug.Log("[路由器] Dash 放行");
            sm.ChangeState(sm.dashState);
            return DispatchResult.Success;
        }

        /// <summary>有方向键输入时，把角色朝向对齐到该方向</summary>
        private void ApplyAimFacingFromInput()
        {
            if (controller == null) return;

            float x = controller.moveInput.x;
            if (Mathf.Abs(x) <= directionThreshold) return;

            controller.SetFacingDirection(x > 0f ? 1 : -1);
        }

        private DispatchResult TryCrouchOrSlide()
        {
            if (!sm.IsGrounded())
            {
                return TryAirCommand(InputCmd.Crouch);
            }

            DispatchCrouchOrSlideOnGround();
            return DispatchResult.Success;
        }

        /// <summary>
        /// 地面上的蹲/铲裁决。落地帧与常规按键共用同一套判断。
        ///
        /// 【关键】用 moveInput.x 而不是 currentState == runState。
        /// 前者是事实（方向键按着没有），后者是延迟一帧的代理指标 ——
        /// 落地那一帧 FallState 还没来得及切成 RunState，用它判断会全落到蹲下。
        /// </summary>
        private void DispatchCrouchOrSlideOnGround()
        {
            // 已经在蹲/铲了就别重复进入（会重置碰撞体，产生抖动）
            if (sm.currentState == sm.crouchState || sm.currentState == sm.slideState) return;

            bool hasDirection = HasDirection;

            if (hasDirection && sm.slideSkill.CanExecute())
            {
                if (verboseLog) Debug.Log("[路由器] Ctrl + 方向 → 滑铲");
                sm.ChangeState(sm.slideState);
                return;
            }

            if (verboseLog)
            {
                Debug.Log(hasDirection
                    ? "[路由器] 有方向但滑铲 CD 中 → 蹲下"
                    : "[路由器] 仅 Ctrl → 蹲下");
            }
            sm.ChangeState(sm.crouchState);
        }

        // ==========================================================
        // 落地帧判定
        // ==========================================================

        /// <summary>
        /// 从空中落到地面的那一帧。
        /// 不看缓存、不看 currentState，只看两件事实：Ctrl 还按着吗？方向键还按着吗？
        ///
        /// 之所以要单独处理，是因为「按住 Ctrl 下落」这个意图可能持续好几秒，
        /// 用 0.15 秒的缓存表达不了。
        /// </summary>
        private void HandleLandingFrame()
        {
            if (!useHeldCrouchOnLanding) return;
            if (controller == null || !controller.isCrouchHeld) return;
            if (IsInCombatStance) return;   // 攻击中落地不强行插入蹲/铲

            if (verboseLog) Debug.Log("[路由器] 落地帧检测到 Ctrl 按住，进入蹲/铲裁决");

            buffer.TryConsume(InputCmd.Crouch);
            buffer.TryConsume(InputCmd.Slide);

            DispatchCrouchOrSlideOnGround();
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
        /// 填法 B：固定动作（比如下砸）
        ///   新建 AirSlamState 卡带，把下方 TODO 换成 sm.ChangeState(sm.airSlamState)。
        ///
        /// 当前返回 Retry：指令留在缓存里，落地后仍可兑现 ——
        /// 这正是"空中按 C 落地滑铲"这一手感的来源。
        /// </summary>
        private DispatchResult TryAirCommand(InputCmd cmd)
        {
            if (loadout != null && loadout.TryHandleAirCommand(cmd))
            {
                if (verboseLog) Debug.Log($"[路由器] 空中 {cmd} 已被宝石接管");
                return DispatchResult.Success;
            }

            // TODO: 填法 B 的入口 —— 想做固定的空中动作（下砸等）就写在这里
            // sm.ChangeState(sm.airSlamState);
            // return DispatchResult.Success;

            return DispatchResult.Retry;
        }

        // ==========================================================
        // 受身打断
        // ==========================================================

        private void TryBreakHitStun(InputCmd cmd)
        {
            if (loadout == null || !loadout.CanBreakHitStun(cmd)) return;

            // 一次性标记：让宝石区分"受身"与"常规使用该动作"
            sm.isBreakingHitStun = true;

            DispatchResult result = TryDispatch(cmd);

            if (result != DispatchResult.Success) sm.isBreakingHitStun = false;
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