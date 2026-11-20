using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【指令路由器】—— 走廊里的前台。
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

        [Tooltip("起飞指令的缓存时长（秒）。攻击动画/后摇将尽时按 Space，收招那一帧立刻起飞")]
        public float flyBufferTime = 0.15f;

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

        // ==========================================================
        // 【全项目预输入总纲】三套机制，一条教义
        // ==========================================================
        private readonly InputBufferQueue buffer = new InputBufferQueue(4);

        private bool wasGroundedLastFrame = true;

        // 飞行中按 Ctrl 的限时不可选中：到点解除。计时放在这里（路由器每帧都跑），
        // 不放在飞行状态里 —— 中途退出飞行也要把这几秒走完，不能提前收掉也不能漏收
        private bool flightUntargetableActive;
        private float flightUntargetableUntil;

        private void Awake()
        {
            sm = GetComponent<PlayerStateMachine>();
            controller = GetComponent<PlayerController>();
            loadout = GetComponent<LoadoutManager>();
            comboBuffer = GetComponent<ComboInputBuffer>();
            chargeSystem = GetComponent<PlayerChargeSystem>();
        }

        private void Update()
        {
            buffer.Tick();

            bool grounded = sm.IsGrounded();

            // 落地帧：这一刻手上按着什么，就出什么
            if (grounded && !wasGroundedLastFrame) HandleLandingFrame();
            wasGroundedLastFrame = grounded;

            if (flightUntargetableActive && Time.time >= flightUntargetableUntil) EndFlightUntargetable();

            TryFlushBuffer();
        }

        private void OnDisable()
        {
            // 组件被关掉时别把不可选中留在身上
            EndFlightUntargetable();
        }

        private void EndFlightUntargetable()
        {
            if (!flightUntargetableActive) return;

            flightUntargetableActive = false;
            sm.playerState?.health.ReleaseUntargetable(InvulnerableTokens.FlightSlide);
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
                if (verboseLog) PlayerLog.Info($"[路由器] {cmd} 闸门未开，已存入预输入缓存");
            }
            else if (verboseLog)
            {
                PlayerLog.Info($"[路由器] {cmd} 被拒绝，不进缓存");
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
            TryFlushOne(InputCmd.Fly);
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

                case InputCmd.Fly: return TryToggleFly();

                default: return DispatchResult.Rejected;
            }
        }

        // ==========================================================
        // shift 二义：方向+shift = 打断冲刺 / 单按 shift = 突刺
        // ==========================================================

        /// <summary>
        /// 【P1b 重写】攻击动画中的 Shift。
        /// </summary>
        /// <summary>有任意一只手在蓄力</summary>
        private bool IsCharging => chargeSystem != null && chargeSystem.IsAnyCharging;

        private DispatchResult TryDashOrThrust()
        {
            // ---- 飞行中：八方向冲刺，冲完继续飞 ----
            if (sm.IsFlying) return TryFlightDash();

            // 【P3 起】不再需要为蓄力单独开分支 ——
            // 蓄力不是状态了，冲刺只是普通的状态切换，天然不会打断它。

            // ---- 攻击动画中 ----
            if (sm.currentState == sm.comboState)
            {
                if (!CanCancelCurrentAttackByMovement())
                {
                    // 这道闸是"暂时关着"：招式演完之后冲刺就完全合法了 → Retry
                    if (verboseLog) PlayerLog.Info("[路由器] 当前招式不允许被位移打断，Shift 已存入缓存");
                    return DispatchResult.Retry;
                }

                return TryDash();
            }

            // ---- 连段间隔中 / 非战斗：普通冲刺（同样免掉剩余后摇，不碰连段）----
            return TryDash();
        }

        /// <summary>
        /// 【P1b 重写】跳跃。
        /// </summary>
        private DispatchResult TryJumpWithCancelCheck()
        {
            // 飞行中不能跳：起落只由 Space 开关决定，跳跃键（Q）在空中没有意义
            if (sm.IsFlying)
            {
                if (verboseLog) PlayerLog.Info("[路由器] 飞行中，Jump 被拒绝");
                return DispatchResult.Rejected;
            }

            // ==========================================================
            // 【实验改动 · R1】跳跃算"移动"，按攻击阶段处理：
            //   前摇 → 允许：本招作废，连段归零
            // ==========================================================
            if (sm.currentState == sm.comboState)
            {
                AttackPhase phase = comboBuffer != null ? comboBuffer.MovementPhase : AttackPhase.None;

                if (phase == AttackPhase.Active)
                {
                    if (verboseLog) PlayerLog.Info("[路由器] 攻击判定中禁止跳跃，Jump 已存入缓存");
                    return DispatchResult.Retry;
                }

                // 跳不起来（段数用完）就别白白作废这一招
                if (sm.jumpCount >= sm.maxJumps) return DispatchResult.Retry;

                if (phase == AttackPhase.Windup) comboBuffer.VoidCurrentAttackAndResetCombo();

                if (verboseLog) PlayerLog.Info($"[路由器] 攻击{(phase == AttackPhase.Windup ? "前摇" : "后摇")}中跳跃 → 取消动作");
                return TryJump();
            }

            // 不在攻击动画里，但还有脱离动画的判定在跑 → 同样按"判定中"处理
            if (comboBuffer != null && comboBuffer.IsMovementLockedByRecovery())
            {
                if (verboseLog) PlayerLog.Info("[路由器] 仍有判定在进行，Jump 已存入缓存");
                return DispatchResult.Retry;
            }

            // 【P3 起放开】蓄力中可以跳跃。
            return TryJump();
        }

        private bool CanCancelCurrentAttackByMovement()
            => comboBuffer == null || comboBuffer.CanCurrentNodeBeCanceledByMovement();

        // ==========================================================
        // 飞行中的 Shift / Ctrl
        // ==========================================================

        /// <summary>
        /// 【飞行中 Shift】八方向冲刺。
        /// </summary>
        private DispatchResult TryFlightDash()
        {
            if (!sm.dashSkill.CanExecute()) return DispatchResult.Retry;

            Vector2 dir = SnapToEightWay(controller != null ? controller.moveInput : Vector2.zero);
            if (dir == Vector2.zero)
                dir = new Vector2(controller != null ? controller.facingDirection : 1f, 0f);

            // 冷却：和 DashState 同一套记账（出手即记，首段才起大 CD）
            bool isFirstHit = sm.dashSkill.IsComboIdle;
            sm.dashSkill.Execute();
            sm.dashSkill.StartCooldownIfFirstHit(isFirstHit);

            // 朝向跟着冲刺方向走，避免"往左冲却面朝右"
            if (controller != null && Mathf.Abs(dir.x) > 0.01f)
                controller.SetFacingDirection(dir.x > 0f ? 1 : -1);

            sm.flightMotor.StartDash(dir);

            if (verboseLog) PlayerLog.Info($"[路由器] 飞行冲刺 → {dir}");
            return DispatchResult.Success;
        }

        /// <summary>把任意方向吸附到 8 个方向之一（各轴超过阈值才算按下）。没有输入返回 zero</summary>
        private Vector2 SnapToEightWay(Vector2 raw)
        {
            float x = Mathf.Abs(raw.x) > directionThreshold ? Mathf.Sign(raw.x) : 0f;
            float y = Mathf.Abs(raw.y) > directionThreshold ? Mathf.Sign(raw.y) : 0f;
            Vector2 v = new Vector2(x, y);
            return v == Vector2.zero ? Vector2.zero : v.normalized;
        }

        /// <summary>
        /// 【飞行中 Ctrl】解除飞行，自由下落。
        /// </summary>
        // 【2026-11-16】上面这段注释描述的是旧的"飞行中 Ctrl = 解除飞行 + 无敌下坠（贴地则蹲 / 铲）"。
        // 那套效果已从飞行中移除：无敌下坠搬去给"普通状态在空中按 Ctrl"用（TryAirSlideDrop），
        // 飞行中按 Ctrl 现在是下面的 TryFlightUntargetable。

        /// <summary>
        /// 飞行中按 Ctrl：变为不可选中状态 n 秒，人继续飞。
        /// 时长 n = MovementConfig.flySlideUntargetableDuration；
        /// 冷却用滑铲的那一套（飞行参数分离时用 flySlideCooldown）。
        /// </summary>
        private DispatchResult TryFlightUntargetable()
        {
            var cfg = sm.playerState != null ? sm.playerState.movementConfig : null;
            float duration = cfg != null ? cfg.flySlideUntargetableDuration : 0.5f;

            // 时长填 0 = 关闭这个功能
            if (duration <= 0f) return DispatchResult.Rejected;

            // CD 中：这道闸是暂时关着的 → Retry
            if (!sm.slideSkill.CanExecute()) return DispatchResult.Retry;

            bool isFirstHit = sm.slideSkill.IsComboIdle;
            sm.slideSkill.Execute();
            sm.slideSkill.StartCooldownIfFirstHit(isFirstHit);

            if (!flightUntargetableActive)
            {
                flightUntargetableActive = true;
                sm.playerState?.health.RequestUntargetable(InvulnerableTokens.FlightSlide);
            }
            flightUntargetableUntil = Time.time + duration;

            if (verboseLog) PlayerLog.Info($"[路由器] 飞行中 Ctrl → 不可选中 {duration:F2}s");
            return DispatchResult.Success;
        }

        /// <summary>
        /// 普通状态在空中按 Ctrl（空中滑铲）：收掉上升速度往下落，落地切进蹲下姿态。
        /// </summary>
        private DispatchResult TryAirSlideDrop()
        {
            if (loadout != null && loadout.TryHandleAirCommand(InputCmd.Crouch))
            {
                if (verboseLog) PlayerLog.Info("[路由器] 空中 Ctrl 已被宝石接管");
                return DispatchResult.Success;
            }

            // 已经在 Ctrl 下落里了：再按不重复触发（否则冷却没好的第二下会把这次的无敌换成普通下落）
            if (sm.currentState == sm.fallState && sm.fallState.IsCtrlDrop) return DispatchResult.Rejected;

            // 同冲刺 / 滑铲：先记下打断的是攻击的哪个阶段，切走之后就问不到了
            bool wasInCombo = sm.currentState == sm.comboState;
            AttackPhase phase = comboBuffer != null ? comboBuffer.CurrentAttackPhase : AttackPhase.None;

            Vector2 v = sm.rb.linearVelocity;

            // 无敌与滑铲共用冷却：好了才无敌，并把冷却用掉
            bool invulnerable = sm.slideSkill.CanExecute();
            if (invulnerable)
            {
                bool isFirstHit = sm.slideSkill.IsComboIdle;
                sm.slideSkill.Execute();
                sm.slideSkill.StartCooldownIfFirstHit(isFirstHit);
            }

            // 无敌由 FallState 自己申请与释放 —— 这样无论从哪条路离开下落
            // （落地 / 二段跳 / 受击 / 冲刺 / 起飞），无敌都一定被收回。
            sm.fallState.PrepareCtrlDrop(invulnerable);
            sm.ChangeState(sm.fallState);

            // 向上的速度清掉：按下即开始往下落，不会先飘一段。水平速度保留
            sm.rb.linearVelocity = new Vector2(v.x, Mathf.Min(v.y, 0f));

            if (wasInCombo) comboBuffer?.OnDashSlideInterrupt(true, phase);

            if (verboseLog) PlayerLog.Info(invulnerable ? "[路由器] 空中 Ctrl → 无敌下落" : "[路由器] 空中 Ctrl → 普通下落（滑铲冷却中）");
            return DispatchResult.Success;
        }

        // ==========================================================
        // 飞行开关
        // ==========================================================

        /// <summary>
        /// 【Space】飞行开关：没在飞 → 起飞；正在飞（巡航或交战）→ 落下。
        /// </summary>
        private DispatchResult TryToggleFly()
        {
            // ---- 正在飞 → 落下 ----
            if (sm.IsFlying)
            {
                sm.ChangeState(sm.fallState);
                if (verboseLog) PlayerLog.Info("[路由器] Space → 结束飞行");
                return DispatchResult.Success;
            }

            // ---- 起飞闸门（与跳跃同一套规则）----
            // 【实验改动 · R1】起飞算"移动"：判定中无效（进缓存），前摇 / 后摇里允许
            bool inCombo = sm.currentState == sm.comboState;
            AttackPhase phase = (inCombo && comboBuffer != null) ? comboBuffer.MovementPhase : AttackPhase.None;

            if (inCombo && phase == AttackPhase.Active)
            {
                if (verboseLog) PlayerLog.Info("[路由器] 攻击判定中，Fly 已存入缓存");
                return DispatchResult.Retry;
            }

            if (!inCombo && comboBuffer != null && comboBuffer.IsMovementLockedByRecovery())
            {
                if (verboseLog) PlayerLog.Info("[路由器] 仍有判定在进行，Fly 已存入缓存");
                return DispatchResult.Retry;
            }

            if (sm.playerState != null && sm.playerState.health.currentMP <= 0)
            {
                if (verboseLog) PlayerLog.Info("[路由器] MP 不足，无法起飞");
                return DispatchResult.Rejected;
            }

            // 前摇里起飞 → 本招作废，连段归零；后摇里起飞 → 连段保留
            if (inCombo && phase == AttackPhase.Windup) comboBuffer.VoidCurrentAttackAndResetCombo();

            // ---- 起飞 ----
            // 【实验改动】起飞不再取消蓄力 —— 飞行中可以蓄力，地面蓄着的力带上天继续蓄
            sm.flightMotor.BeginSession();   // 新的一次飞行：耗蓝零头清零
            sm.ChangeState(sm.flyState);

            if (verboseLog) PlayerLog.Info("[路由器] Space → 起飞");
            return DispatchResult.Success;
        }

        // ==========================================================
        // ctrl 三态
        // ==========================================================

        /// <summary>
        /// 【P1b 重写】Ctrl 在不同时机下的三种含义。
        /// </summary>
        private DispatchResult TryCrouchSlideOrCancel()
        {
            // ---- 飞行中：解除飞行，无敌下坠 ----
            if (sm.IsFlying) return TryFlightUntargetable();

            // ---- 攻击动画中 ----
            if (sm.currentState == sm.comboState)
            {
                // 单按（无方向）→ 碰撞箱微调。
                // 这件事【不切状态】，所以路由器不做任何事 ——
                // ComboState 每帧读 isCrouchHeld 自己处理，
                // 也只有它知道攻击结束时该把碰撞箱还原。
                if (!HasDirection)
                {
                    if (verboseLog) PlayerLog.Info("[路由器] 攻击中单按 ctrl → 碰撞箱微调（交给 ComboState）");
                    return DispatchResult.Rejected;   // 不进缓存，避免几帧后跑出来滑铲
                }

                if (!CanCancelCurrentAttackByMovement())
                {
                    // 与 Shift 同理：暂时关着的闸门存缓存，招式结束后若仍在时限内就兑现
                    if (verboseLog) PlayerLog.Info("[路由器] 当前招式不允许被位移打断，Ctrl 已存入缓存");
                    return DispatchResult.Retry;
                }

                // 【实验改动 · R1】滑铲任何阶段可用，永不打断连段（处理在 DispatchCrouchOrSlideOnGround 里）。
                // 滑铲 CD 中时不退化成"蹲下"—— 那会无缘无故把攻击动画切掉，改为存缓存。
                if (sm.IsGrounded() && !sm.slideSkill.CanExecute())
                {
                    if (verboseLog) PlayerLog.Info("[路由器] 攻击中 方向+ctrl，但滑铲 CD 中 → 存入缓存");
                    return DispatchResult.Retry;
                }

                return TryCrouchOrSlide();
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

            if (verboseLog) PlayerLog.Info($"[路由器] Jump 放行 ({sm.jumpCount + 1}/{sm.maxJumps})");
            sm.ChangeState(sm.jumpState);
            return DispatchResult.Success;
        }

        private DispatchResult TryDash()
        {
            if (!sm.dashSkill.CanExecute()) return DispatchResult.Retry;

            // 【修复】按方向键先把朝向定好，再切状态。
            ApplyAimFacingFromInput();

            // 【2026-11-16】普通状态也是八方向冲刺（和飞行冲刺同一套取向规则）
            Vector2 dashDir = ResolveGroundAirDashDirection();
            if (dashDir == Vector2.zero) sm.dashState.SetNextDashStationary();
            else sm.dashState.SetNextDashDirection(dashDir);

            // 【实验 · R1】切状态之前先记下"打断的是哪个阶段"—— 切走之后就问不到了
            bool wasInCombo = sm.currentState == sm.comboState;
            AttackPhase phase = comboBuffer != null ? comboBuffer.CurrentAttackPhase : AttackPhase.None;

            if (verboseLog) PlayerLog.Info("[路由器] Dash 放行");
            sm.ChangeState(sm.dashState);

            comboBuffer?.OnDashSlideInterrupt(wasInCombo, phase);
            return DispatchResult.Success;
        }

        /// <summary>有方向键输入时，把角色朝向对齐到该方向</summary>
        private Vector2 ResolveGroundAirDashDirection()
        {
            float facing = controller != null ? controller.facingDirection : 1f;

            Vector2 dir = SnapToEightWay(controller != null ? controller.moveInput : Vector2.zero);
            if (dir == Vector2.zero) return new Vector2(facing, 0f);

            if (dir.y < 0f && sm.IsGrounded())
            {
                if (Mathf.Abs(dir.x) > 0.01f) return new Vector2(Mathf.Sign(dir.x), 0f);
                return Vector2.zero;
            }

            return dir;
        }

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
                // 【2026-11-16】空中 Ctrl = 空中滑铲（无敌下落）。原先没有宝石接管时什么都不做
                return TryAirSlideDrop();
            }

            DispatchCrouchOrSlideOnGround();
            return DispatchResult.Success;
        }

        /// <summary>
        /// 地面上的蹲/铲裁决。落地帧与常规按键共用同一套判断。
        /// </summary>
        private void DispatchCrouchOrSlideOnGround()
        {
            // 已经在蹲/铲了就别重复进入（会重置碰撞体，产生抖动）
            if (sm.currentState == sm.crouchState || sm.currentState == sm.slideState) return;

            bool hasDirection = HasDirection;

            if (hasDirection && sm.slideSkill.CanExecute())
            {
                // 【实验 · R1】同冲刺：先记阶段，滑出去之后通知连招引擎
                bool wasInCombo = sm.currentState == sm.comboState;
                AttackPhase phase = comboBuffer != null ? comboBuffer.CurrentAttackPhase : AttackPhase.None;

                if (verboseLog) PlayerLog.Info("[路由器] Ctrl + 方向 → 滑铲");
                sm.ChangeState(sm.slideState);

                comboBuffer?.OnDashSlideInterrupt(wasInCombo, phase);
                return;
            }

            if (verboseLog)
            {
                PlayerLog.Info(hasDirection
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
        /// </summary>
        private void HandleLandingFrame()
        {
            if (!useHeldCrouchOnLanding) return;
            if (controller == null || !controller.isCrouchHeld) return;
            if (IsInCombatStance) return;   // 攻击中落地不强行插入蹲/铲

            // 空中 Ctrl 下落的落地由 FallState 自己切进蹲下，这里不再插一个滑铲
            if (sm.currentState == sm.fallState && sm.fallState.IsCtrlDrop) return;

            // 飞行中贴地不算"落地"—— 否则按着 Ctrl 飞到地面会被插一个滑铲，飞行被打断
            if (sm.IsFlying) return;

            if (verboseLog) PlayerLog.Info("[路由器] 落地帧检测到 Ctrl 按住，进入蹲/铲裁决");

            buffer.TryConsume(InputCmd.Crouch);
            buffer.TryConsume(InputCmd.Slide);

            DispatchCrouchOrSlideOnGround();
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
            else if (verboseLog) PlayerLog.Info($"[路由器] 受身成功，{cmd} 打断硬直");
        }

        // ==========================================================
        // 杂项
        // ==========================================================

        private float GetBufferTime(InputCmd cmd)
        {
            switch (cmd)
            {
                case InputCmd.Jump: return jumpBufferTime;
                case InputCmd.Fly: return flyBufferTime;
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