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

        // ==========================================================
        // 【全项目预输入总纲】三套机制，一条教义
        //
        // ① 点按普攻（ComboInputBuffer.hasBufferedInput）
        //    等的是【自己的动画】—— 时长已知且是玩家自找的，等多久都合理。
        //    所以宽恕期锚定在"闸门打开"：闸门关着不计时，最多滞留 1.5s 兜底。
        //
        // ② 长按蓄力（ComboInputBuffer.pendingHold）
        //    手指还物理按着 = 意图【现在进行时】，根本不存在"过期"的概念。
        //    所以没有计时器，唯一的作废条件是松手。给它加上限反而是 bug ——
        //    会出现"我明明一直按着，它却自己放弃了"。
        //
        // ③ 位移（本队列）
        //    等的是【世界事件】—— CD 转好、落地，时机玩家无法精确预判。
        //    位移延迟兑现的危害远大于攻击（1 秒后自己冲出去 = 失控感），
        //    所以宽恕期锚定在"按下"：只原谅差一点点就赶上的输入（0.15s），
        //    按得太早照常作废。这不是没改的旧毛病，是刻意的不同策略。
        //
        // 三套的分界线就一条：这次等待是"等自己"还是"等世界"，
        // 以及意图是"一瞬间的"还是"持续按着的"。
        //
        // 【Retry / Rejected 的铁律】暂时关着、以后会开的闸门 → Retry 进缓存；
        // 永远不该发生的事 → Rejected 直接丢弃。判断标准是闸门的性质，
        // 不是当下方不方便 —— 攻击中跳跃曾被误标成 Rejected，
        // 表现就是收招前一瞬的按键被吞。
        // ==========================================================
        private readonly InputBufferQueue buffer = new InputBufferQueue(4);

        private bool wasGroundedLastFrame = true;

        /// <summary>上一帧是不是在大rt 里。用来抓「大rt 刚开始」那一个边沿</summary>
        private bool wasInChargeStun = false;

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

            // ==========================================================
            // 【大rt 刚开始、而且人就在地面上】—— 也要问一次 Ctrl 按着没有。
            //
            // 「角色第一次真的能滑的那一帧」有两种可能：
            //   ① 大rt 直接在地面开始       → 就是这一帧（本段）
            //   ② 大rt 在空中开始，之后落地 → 落地那一帧（HandleLandingFrame）
            // 两处都是【边沿】检测，所以大rt 中途新按下的键不算数，
            // "大rt 禁止一切输入"这条规则没有被破坏。
            // ==========================================================
            bool inChargeStun = (sm.currentState == sm.chargeStunState);
            if (inChargeStun && !wasInChargeStun && grounded) TryHeldSlideEscape();
            wasInChargeStun = inChargeStun;

            TryFlushBuffer();
        }

        // ==========================================================
        // 对外入口
        // ==========================================================

        public void OnCommand(InputCmd cmd)
        {
            // 强行打出弱化蓄力后的僵直：禁止一切输入，跳/冲/铲全部无效。
            //
            // 【故意不进缓存】僵直期内按下的键直接丢弃，
            // 否则僵直一解除，攒下的指令会集体兑现，角色像自己动起来一样。
            //
            // 【唯一的出口是受伤】受击时 PlayerStateMachine.HandlePlayerHit
            // 会无条件切进 hitState，僵直自然被接管 —— 不需要在这里开口子。
            if (sm.currentState == sm.chargeStunState)
            {
                if (verboseLog) Debug.Log($"[路由器] 蓄力僵直中，{cmd} 被丢弃");
                return;
            }

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

            // 蓄力僵直同理 —— 而且进僵直时 ClearBuffer 已经倒空过一次，
            // 这里是防止僵直期间有别的路径又塞了东西进来
            if (sm.currentState == sm.chargeStunState) return;

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

        /// <summary>
        /// 空中蓄力姿态生效中（空中起手 且 仍在空中）。
        /// 与角色悬停、方向键锁共用同一个判据 —— 见 PlayerChargeSystem.IsAirChargeHovering。
        /// </summary>
        private bool IsAirChargeHovering
            => chargeSystem != null && chargeSystem.IsAirChargeHovering;

        private DispatchResult TryDashOrThrust()
        {
            // ---- 空中蓄力：朝鼠标落点冲刺 ----
            //
            // 空中蓄力时方向键被锁（见 Jump/FallState），
            // 想调整位置只能花这一次冲刺 —— 高风险承诺换一次精准位移。
            //
            // 【判据是"空中起手"，不是"现在在空中"】
            // 用后者的话，地面起手蓄力再跳起来，Shift 也会变成"指哪去哪"——
            // 可玩家并没有做出空中蓄力那个承诺，凭什么拿到它的特权。
            // 现在与悬停、方向键锁共用同一个判据，三者永远同进同退。
            if (IsAirChargeHovering)
            {
                return TryChargeAirDash();
            }

            // 【P3 起】不再需要为蓄力单独开分支 ——
            // 蓄力不是状态了，冲刺只是普通的状态切换，天然不会打断它。

            // ---- 攻击动画中：位移 + 打断连段 ----
            if (sm.currentState == sm.comboState)
            {
                // 弱化蓄力演出中：不给真冲刺，只兑换成一股初速度。
                //
                // 【刻意排在取消权限检查之前 —— 预付逃逸不受招式的 byMovement 约束】
                // 因为它根本【不是一次打断】：状态没切走，招式照常演完、照常进大rt，
                // 玩家拿到的只是一股在僵直里释放的冲劲。而 byMovement 那个勾表达的是
                // "这一招能不能被位移【打断】"，管不到这里。
                //
                // 更重要的是设计上的定性：预付逃逸是弱化蓄力这个惩罚的固有配套 ——
                // 「有得选，但选完没有回头路」。它和惩罚是同一个整体，
                // 不该被某个单独招式的配置关掉。
                if (WillEnterChargeStun) return TryPrepayEscape(InputCmd.Dash);

                if (!CanCancelCurrentAttackByMovement())
                {
                    // 【Rejected → Retry】这道闸是"暂时关着"，不是"故意拒绝"：
                    // 招式演完之后冲刺就完全合法了。按路由器自己的三态定义，
                    // 这种情况本来就该 Retry —— 原先返回 Rejected 是自相矛盾。
                    //
                    // 效果：在不可打断招式的【末尾 0.15 秒内】按 Shift，
                    // 招式一结束立刻冲出去；按得更早则照常作废。
                    if (verboseLog) Debug.Log("[路由器] 当前招式不允许被位移打断，Shift 已存入缓存");
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

        private DispatchResult TryJumpWithCancelCheck()
        {
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
                if (verboseLog) Debug.Log("[路由器] 后摇中禁止跳跃，Jump 已存入缓存");
                return DispatchResult.Retry;
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

        /// <summary>当前正在演的这一招演完之后会进僵直吗（即：它是强行打出的弱化蓄力）</summary>
        private bool WillEnterChargeStun
            => sm.comboState != null && sm.comboState.TryGetPendingStun(out _);

        /// <summary>
        /// 【预付逃逸】弱化蓄力演出中的冲刺 / 滑铲。
        ///
        /// ==========================================================
        /// 【为什么不能让它真的冲出去】
        ///
        /// 僵直的触发点在 OnAttackAnimationEnd —— 只有"自然演完"才会进。
        /// 于是在动画期间冲刺一下，状态直接切去 DashState，僵直永远不会发生：
        /// 玩家花一次冲刺 CD 就免掉了整段惩罚，弱化版形同虚设。
        ///
        /// 但把它一刀禁掉也不对 —— 打完一发这么亏的招还完全动不了，
        /// 手感上是纯粹的挨罚，没有任何操作空间。
        ///
        /// 折中：这一下【照收 CD】，但兑换成一股初速度存起来，
        /// 等僵直开始时释放。玩家仍然全程不能操作，
        /// 拿到的是一个【方向在按下那一刻就定死】的位移 ——
        /// 有得选，但选完就没有回头路，和「蓄力是一场赌注」是同一个调性。
        /// ==========================================================
        /// </summary>
        private DispatchResult TryPrepayEscape(InputCmd cmd)
        {
            // 【一次大rt 只兑换一个逃逸动作，先来的算数】
            // 否则"演出中点了 Shift，落地时又还按着 Ctrl"会花掉两个位移 CD，
            // 而玩家只看得到一次位移 —— 平白亏一个 CD，且完全看不出为什么。
            if (sm.chargeStunState != null && sm.chargeStunState.HasEscape)
            {
                if (verboseLog) Debug.Log($"[路由器] 本次大rt 已经兑换过逃逸，{cmd} 忽略");
                return DispatchResult.Rejected;
            }

            bool isDash = (cmd == InputCmd.Dash);
            ComboSkill skill = isDash ? sm.dashSkill : sm.slideSkill;

            // CD 没好：当成"闸门暂时没开"存缓存。
            // 缓存会在进僵直时被 ClearBuffer 倒掉，所以不会延迟兑现 ——
            // 但在动画还没演完的这段时间里，CD 一转好就能补上这次预付。
            if (!skill.CanExecute()) return DispatchResult.Retry;

            bool isFirstHit = skill.IsComboIdle;
            skill.Execute();
            skill.StartCooldownIfFirstHit(isFirstHit);

            // 有方向键就朝方向键，没有就朝角色正面 —— 和真冲刺的取向规则一致
            ApplyAimFacingFromInput();
            float direction = controller != null ? controller.facingDirection : 1f;

            // 初速度与衰减曲线都由 PlayerMovementConfig 决定，
            // 本文件只负责"记下方向、告诉僵直是冲刺还是滑铲兑换的"
            sm.chargeStunState.SetEscapeMomentum(isDash, direction);

            if (verboseLog)
                Debug.Log($"[路由器] 弱化蓄力中预付 {cmd} → 僵直时给一股冲劲（方向 {direction}）");

            return DispatchResult.Success;
        }

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
            // ---- 空中蓄力姿态：禁止滑铲 ----
            //
            // 空中蓄力期间唯一的位移手段是 Shift 冲刺，
            // 让 Ctrl 也能位移会削弱"空中蓄力是高风险承诺"这个设计。
            //
            // 同样用"空中起手"判据 —— 地面起手蓄力再跳起来时不受此限。
            if (IsAirChargeHovering)
            {
                if (verboseLog) Debug.Log("[路由器] 空中蓄力姿态中，禁止滑铲");
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

                // 弱化蓄力演出中：不给真滑铲，只兑换成一股初速度。
                // 与 Shift 同理，刻意排在取消权限检查之前 —— 见 TryDashOrThrust 里的说明。
                if (WillEnterChargeStun) return TryPrepayEscape(InputCmd.Crouch);

                if (!CanCancelCurrentAttackByMovement())
                {
                    // 与 Shift 同理：暂时关着的闸门存缓存，招式结束后若仍在时限内就兑现
                    if (verboseLog) Debug.Log("[路由器] 当前招式不允许被位移打断，Ctrl 已存入缓存");
                    return DispatchResult.Retry;
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

            // ==========================================================
            // 【落地那一刻还在大rt】→ 这一下不是普通滑铲，而是滑铲逃逸的位移。
            //
            // 两种结果由「落地那一刻大rt 走完没有」自然分流，不需要玩家记规则：
            //   还在大rt   → 走这里，兑换成一股不可操控的冲劲弹射出去
            //   大rt 已走完 → 落到下面，接正常的落地滑铲
            //
            // 【绝不能让它插一个真滑铲进来】那会直接切进 SlideState，
            // 大rt 被整个跳过，惩罚形同虚设 —— 这正是预付逃逸机制存在的理由。
            // ==========================================================
            if (sm.currentState == sm.chargeStunState)
            {
                TryHeldSlideEscape();
                return;
            }

            if (verboseLog) Debug.Log("[路由器] 落地帧检测到 Ctrl 按住，进入蹲/铲裁决");

            buffer.TryConsume(InputCmd.Crouch);
            buffer.TryConsume(InputCmd.Slide);

            DispatchCrouchOrSlideOnGround();
        }

        /// <summary>
        /// 【按住 Ctrl 兑换滑铲逃逸】大rt 期间，角色第一次真的能滑的那一帧调用。
        ///
        /// 和演出期间的预付（TryPrepayEscape）是同一件事的两条入口：
        ///   演出中【按下】Ctrl  → 脉冲 → TryPrepayEscape（玩家主动、当场承诺）
        ///   一直【按着】Ctrl    → 没有脉冲 → 本方法（落地/大rt 开始那一帧兑现）
        ///
        /// 后者存在的原因：空中长按蓄力时按下的那一次 Ctrl，脉冲发生在
        /// 「空中蓄力姿态」期间 —— 被规则挡掉了，而且不进缓存。之后玩家
        /// 一直按着不放，就再没有第二个脉冲，预付永远不会发生。
        /// 这与 HandleLandingFrame 当初要解决的是同一个问题：
        /// 「按住」这个意图可能持续好几秒，0.15 秒的缓存表达不了。
        ///
        /// 两条入口都要付一次滑铲 CD，也都受 HasEscape 去重 ——
        /// 一次大rt 只兑换一个逃逸动作，先来的算数。
        /// </summary>
        private void TryHeldSlideEscape()
        {
            if (!useHeldCrouchOnLanding) return;
            if (controller == null || !controller.isCrouchHeld) return;

            // 没有方向就不产生位移 —— 与地面规则一致（单按 Ctrl 是蹲/微调，不是滑铲）
            if (!HasDirection) return;

            if (sm.chargeStunState == null || sm.chargeStunState.HasEscape) return;

            // CD 没好就没有。逃逸是要花钱的，这一点和真滑铲一样
            if (!sm.slideSkill.CanExecute())
            {
                if (verboseLog) Debug.Log("[路由器] 想兑换滑铲逃逸，但滑铲 CD 未就绪");
                return;
            }

            bool isFirstHit = sm.slideSkill.IsComboIdle;
            sm.slideSkill.Execute();
            sm.slideSkill.StartCooldownIfFirstHit(isFirstHit);

            // 有方向键就朝方向键 —— 和真滑铲、和预付逃逸的取向规则完全一致
            ApplyAimFacingFromInput();
            float direction = controller.facingDirection;

            sm.chargeStunState.GrantEscapeNow(isDash: false, direction);

            if (verboseLog)
                Debug.Log($"[路由器] 大rt 中按住 Ctrl → 兑换滑铲逃逸（方向 {direction}）");
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