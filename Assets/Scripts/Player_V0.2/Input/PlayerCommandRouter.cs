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

<<<<<<< Updated upstream
            // ---- 攻击动画中：位移 + 打断连段 ----
=======
            // ---- 攻击动画中 ----
>>>>>>> Stashed changes
            if (sm.currentState == sm.comboState)
            {
                if (!CanCancelCurrentAttackByMovement())
                {
<<<<<<< Updated upstream
                    // 【Rejected → Retry】这道闸是"暂时关着"，不是"故意拒绝"：
                    // 招式演完之后冲刺就完全合法了。按路由器自己的三态定义，
                    // 这种情况本来就该 Retry —— 原先返回 Rejected 是自相矛盾。
                    //
                    // 效果：在不可打断招式的【末尾 0.15 秒内】按 Shift，
                    // 招式一结束立刻冲出去；按得更早则照常作废。
                    if (verboseLog) Debug.Log("[路由器] 当前招式不允许被位移打断，Shift 已存入缓存");
=======
                    // 这道闸是"暂时关着"：招式演完之后冲刺就完全合法了 → Retry
                    if (verboseLog) PlayerLog.Info("[路由器] 当前招式不允许被位移打断，Shift 已存入缓存");
>>>>>>> Stashed changes
                    return DispatchResult.Retry;
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
        /// </summary>
<<<<<<< Updated upstream
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

            // 【冲到鼠标那一点，而不是朝鼠标冲一个固定距离】
            //
            // 鼠标在最大位移范围【内】 → 精确落在鼠标那里
            // 鼠标在范围【外】         → 朝同一方向冲到最远处
            //
            // 这样空中蓄力唯一的调位手段是「可瞄准的」而不是「盲冲」——
            // 玩家花掉这次冲刺时，能明确知道自己会停在哪。
            float maxDistance = sm.dashSpeed * sm.dashDuration;

            Vector2 dir;
            float distance;

            // 走不降级的落点：AimWorldPoint 在降级到八向时给的是
            // 「沿方向推 5 格」的假点，拿它算距离会得到一个和鼠标无关的数
            if (aim != null && aim.TryGetPointerWorldPoint(out Vector3 target))
            {
                Vector2 delta = (Vector2)(target - transform.position);

                if (delta.sqrMagnitude > 0.0001f)
                {
                    dir = delta.normalized;
                    distance = Mathf.Min(delta.magnitude, maxDistance);
                }
                else
                {
                    // 鼠标压在角色身上：没有有意义的方向，不如别动
                    if (verboseLog) Debug.Log("[路由器] 空中蓄力冲刺：鼠标压在角色身上，忽略");
                    return DispatchResult.Rejected;
                }
            }
            else
            {
                // 没有鼠标（纯手柄）→ 退化成朝正面冲满
                dir = new Vector2(controller.facingDirection, 0f);
                distance = maxDistance;
            }

            sm.dashState.SetNextDash(dir, distance);
            sm.ChangeState(sm.dashState);

            if (verboseLog)
                Debug.Log($"[路由器] 空中蓄力 → 冲向鼠标 {dir}，距离 {distance:F2}/{maxDistance:F2}");

            return DispatchResult.Success;
        }

=======
>>>>>>> Stashed changes
        private DispatchResult TryJumpWithCancelCheck()
        {
            // 飞行中不能跳：起落只由 Space 开关决定，跳跃键（Q）在空中没有意义
            if (sm.IsFlying)
            {
                if (verboseLog) PlayerLog.Info("[路由器] 飞行中，Jump 被拒绝");
                return DispatchResult.Rejected;
            }

<<<<<<< Updated upstream
            if (sm.currentState == sm.comboState)
            {
                // 【Rejected → Retry】"攻击动画中不能跳"这条设计规则没有变 ——
                // 跳跃依然打断不了攻击。变的只是这次按键的下场：
                //
                //   Rejected（旧）→ 输入直接消失。在动画将尽未尽时按跳，
                //                   什么都不发生，必须再按一次 —— 玩家读作"吞键"。
                //   Retry（新）  → 存入缓存。若动画在缓存时限（jumpBufferTime,
                //                   默认 0.15s）内结束，收招那一帧立刻起跳。
                //
                // 这正是平台跳跃游戏的经典宽恕窗口：只有"差一点点就赶上"的
                // 输入被原谅，按得太早照常作废，不会出现延迟很久的自动起跳。
                if (verboseLog) Debug.Log("[路由器] 攻击动画中禁止跳跃，Jump 已存入缓存");
                return DispatchResult.Retry;
=======
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
>>>>>>> Stashed changes
            }

            // ==========================================================
            // 【后摇（rt）期间禁止跳跃】
            //
            // 设计规则：普通招式打完进入 rt，期间方向键、跳跃、同手攻击都被禁用，
            // 只允许换手攻击和 dash/slide 打断。
            //
            // 【原先为什么漏了】跳跃的禁用检查问的是"当前是不是在攻击状态里"，
            // 而 rt 发生在攻击动画【结束之后】—— 那时状态已经切回 Idle/Run，
            // 这个检查自然管不着。方向键之所以一直正常，是因为它走的是
            // 另一条路：问 ComboInputBuffer"手忙不忙"，而不是问"在哪个状态"。
            //
            // 比喻：门禁只认"人在不在会议室里"，可罚站是在会议室门口罚的。
            //       现在改成认"这个人还在不在受罚"。
            //
            // 【为什么这条必须有】rt 是后摇的成本。若能靠跳跃跳过它，
            // 那打断后摇就成了零代价的操作，rt 这个参数等于失效。
            //
            // 【Retry 不是 Rejected】rt 一定会结束，属于"暂时关着的闸门"，
            // 按路由器的三态定义该进缓存 —— 于是在 rt 结束前 0.15 秒
            // （jumpBufferTime）内按跳，一解锁立刻起跳；按得更早照常作废。
            //
            // 【蹲下刻意不加这条】单按 Ctrl 在 rt 期间仍然有效。
            // 那是原地躲擦边攻击，本身不产生位移、也没有跳跃那种"逃掉惩罚"的收益。
            // TODO（设计预留）：以后可做成「rt 期间用 Ctrl 成功躲过一次攻击 →
            //   奖励免去剩余后摇」。挂钩点是 PlayerHitDetection 的受击侧 +
            //   ComboInputBuffer.ClearHandRecovery()。
            // ==========================================================
            if (comboBuffer != null && comboBuffer.IsMovementLockedByRecovery())
            {
<<<<<<< Updated upstream
                if (verboseLog) Debug.Log("[路由器] 后摇中禁止跳跃，Jump 已存入缓存");
=======
                if (verboseLog) PlayerLog.Info("[路由器] 仍有判定在进行，Jump 已存入缓存");
>>>>>>> Stashed changes
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
        private DispatchResult TryFlightDrop()
        {
            if (sm.IsGrounded())
            {
<<<<<<< Updated upstream
                if (verboseLog) Debug.Log("[路由器] 贴地飞行中按 Ctrl → 直接蹲/铲");
                DispatchCrouchOrSlideOnGround();
=======
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
>>>>>>> Stashed changes
                return DispatchResult.Success;
            }

            Vector2 v = sm.rb.linearVelocity;

            sm.fallState.PrepareInvulnerableDrop();
            sm.ChangeState(sm.fallState);
            sm.rb.linearVelocity = new Vector2(v.x, Mathf.Min(v.y, 0f));

<<<<<<< Updated upstream
            if (verboseLog) Debug.Log("[路由器] 飞行中 Ctrl → 无敌下坠");
=======
            if (wasInCombo) comboBuffer?.OnDashSlideInterrupt(true, phase);

            if (verboseLog) PlayerLog.Info(invulnerable ? "[路由器] 空中 Ctrl → 无敌下落" : "[路由器] 空中 Ctrl → 普通下落（滑铲冷却中）");
>>>>>>> Stashed changes
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
            if (sm.currentState == sm.comboState)
            {
<<<<<<< Updated upstream
                if (verboseLog) Debug.Log("[路由器] 攻击动画中，Fly 已存入缓存");
=======
                if (verboseLog) PlayerLog.Info("[路由器] 攻击判定中，Fly 已存入缓存");
>>>>>>> Stashed changes
                return DispatchResult.Retry;
            }

            if (comboBuffer != null && comboBuffer.IsMovementLockedByRecovery())
            {
<<<<<<< Updated upstream
                if (verboseLog) Debug.Log("[路由器] 后摇中，Fly 已存入缓存");
=======
                if (verboseLog) PlayerLog.Info("[路由器] 仍有判定在进行，Fly 已存入缓存");
>>>>>>> Stashed changes
                return DispatchResult.Retry;
            }

            if (sm.playerState != null && sm.playerState.health.currentMP <= 0)
            {
                if (verboseLog) PlayerLog.Info("[路由器] MP 不足，无法起飞");
                return DispatchResult.Rejected;
            }

            // ---- 起飞 ----
            chargeSystem?.CancelAll();
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
            if (sm.IsFlying) return TryFlightDrop();

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

<<<<<<< Updated upstream
                DispatchResult r = TryCrouchOrSlide();
=======
                // 【实验改动 · R1】滑铲任何阶段可用，永不打断连段（处理在 DispatchCrouchOrSlideOnGround 里）。
                // 滑铲 CD 中时不退化成"蹲下"—— 那会无缘无故把攻击动画切掉，改为存缓存。
                if (sm.IsGrounded() && !sm.slideSkill.CanExecute())
                {
                    if (verboseLog) PlayerLog.Info("[路由器] 攻击中 方向+ctrl，但滑铲 CD 中 → 存入缓存");
                    return DispatchResult.Retry;
                }
>>>>>>> Stashed changes

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

            if (verboseLog) PlayerLog.Info($"[路由器] Jump 放行 ({sm.jumpCount + 1}/{sm.maxJumps})");
            sm.ChangeState(sm.jumpState);
            return DispatchResult.Success;
        }

        private DispatchResult TryDash()
        {
            if (!sm.dashSkill.CanExecute()) return DispatchResult.Retry;

            // 【修复】按方向键先把朝向定好，再切状态。
            ApplyAimFacingFromInput();

<<<<<<< Updated upstream
            if (verboseLog) Debug.Log("[路由器] Dash 放行");
=======
            // 【2026-11-16】普通状态也是八方向冲刺（和飞行冲刺同一套取向规则）
            Vector2 dashDir = ResolveGroundAirDashDirection();
            if (dashDir == Vector2.zero) sm.dashState.SetNextDashStationary();
            else sm.dashState.SetNextDashDirection(dashDir);

            // 【实验 · R1】切状态之前先记下"打断的是哪个阶段"—— 切走之后就问不到了
            bool wasInCombo = sm.currentState == sm.comboState;
            AttackPhase phase = comboBuffer != null ? comboBuffer.CurrentAttackPhase : AttackPhase.None;

            if (verboseLog) PlayerLog.Info("[路由器] Dash 放行");
>>>>>>> Stashed changes
            sm.ChangeState(sm.dashState);
            return DispatchResult.Success;
        }

        /// <summary>有方向键输入时，把角色朝向对齐到该方向</summary>
<<<<<<< Updated upstream
=======
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

>>>>>>> Stashed changes
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
        /// </summary>
        private void DispatchCrouchOrSlideOnGround()
        {
            // 已经在蹲/铲了就别重复进入（会重置碰撞体，产生抖动）
            if (sm.currentState == sm.crouchState || sm.currentState == sm.slideState) return;

            bool hasDirection = HasDirection;

            if (hasDirection && sm.slideSkill.CanExecute())
            {
<<<<<<< Updated upstream
                if (verboseLog) Debug.Log("[路由器] Ctrl + 方向 → 滑铲");
=======
                // 【实验 · R1】同冲刺：先记阶段，滑出去之后通知连招引擎
                bool wasInCombo = sm.currentState == sm.comboState;
                AttackPhase phase = comboBuffer != null ? comboBuffer.CurrentAttackPhase : AttackPhase.None;

                if (verboseLog) PlayerLog.Info("[路由器] Ctrl + 方向 → 滑铲");
>>>>>>> Stashed changes
                sm.ChangeState(sm.slideState);
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