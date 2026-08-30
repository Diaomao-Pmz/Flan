using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【蓄力系统】—— 持有两只手的蓄力模块，挂在玩家身上。
    ///
    /// ==========================================================
    /// 它取代了原来的 ChargeState。
    ///
    /// 蓄力不再是"玩家待在某个状态里"，而是"某只手正攥着一个充能物"。
    /// 于是蓄力期间玩家仍然处于 Idle / Run / Jump / Fall / Combo ——
    /// 跳跃和跑动自然可用，另一只手也能正常打连招。
    ///
    /// 【移动折损用属性修饰器，不用硬编码】
    /// 蓄力开始时往 moveSpeed / jumpForce 上贴一张便利贴，
    /// 结束时按签名撕掉。这样：
    ///   ① 不需要在每个状态里写 if(蓄力中) 速度×0.2
    ///   ② 和宝石、buff 的加成自动叠加，不会互相覆盖
    ///   ③ 撕的时候按签名撕，不可能漏减
    /// ==========================================================
    /// </summary>
    public class PlayerChargeSystem : MonoBehaviour
    {
        [Header("移动折损 (蓄力期间)")]
        [Tooltip("移速倍率。0.2 = 只有平时两成。留空则读 PlayerMovementConfig")]
        public bool overrideMoveMultiplier = false;
        [Range(0f, 1f)] public float moveMultiplierOverride = 0.2f;

        [Tooltip("跳跃力度倍率。0.7 = 跳得比平时低三成")]
        [Range(0.1f, 1f)]
        public float jumpMultiplier = 0.7f;

        // 【已删除】airChargeMaxFallSpeed —— 空中蓄力的「最大下落速度」。
        //
        // 它做的是「每帧把 y 速度钳回上限」，但钳位跑在物理步进【之前】，
        // 每步都有一帧重力从后面漏过去 —— 所以填 0 也停不住，只会缓降。
        // 现在改成直接 gravityScale = 0 把重力源关掉，
        // 见 PlayerStateBase.PinInAirWhileCharging，不再需要这个参数。

        [Header("Debug")]
        public bool verboseLog = false;

        // ---- 事件（UI / 特效订阅）----
        /// <summary>某只手的蓄力等级变化 (槽位, 等级)。0 = 未蓄满，>0 = 已蓄满的等级</summary>
        public event System.Action<WeaponSlot, int> OnChargeLevelChanged;

        /// <summary>某只手的蓄力进度变化 (槽位, 0~1)</summary>
        public event System.Action<WeaponSlot, float> OnChargeProgressChanged;

        /// <summary>某只手开始蓄力</summary>
        public event System.Action<WeaponSlot> OnChargeStarted;

        /// <summary>某只手结束蓄力（无论成败）</summary>
        public event System.Action<WeaponSlot> OnChargeEnded;

        private readonly ChargeModule[] modules =
        {
            new ChargeModule(WeaponSlot.Main),
            new ChargeModule(WeaponSlot.Sub),
        };

        private PlayerState state;
        private PlayerController controller;
        private ComboInputBuffer buffer;
        private WeaponLoadout weapons;
        private PlayerSensor sensor;

        private bool penaltyApplied;

        private void Awake()
        {
            state = GetComponent<PlayerState>();
            controller = GetComponent<PlayerController>();
            buffer = GetComponent<ComboInputBuffer>();
            weapons = GetComponent<WeaponLoadout>();
            sensor = GetComponent<PlayerSensor>();

            if (state != null) state.EnsureInitialized();
        }

        // ==========================================================
        // 查询
        // ==========================================================

        public ChargeModule GetModule(WeaponSlot slot) => modules[(int)slot];

        public ChargeModule GetModule(InputCmd cmd)
            => WeaponMoveSet.TryCommandToSlot(cmd, out WeaponSlot s) ? modules[(int)s] : null;

        /// <summary>有任意一只手在蓄力</summary>
        public bool IsAnyCharging => modules[0].IsCharging || modules[1].IsCharging;

        /// <summary>
        /// 【弱蓄进行中】有任意一只手正在进行一次被定性为「弱化」的蓄力。
        ///
        /// ==========================================================
        /// 用途：弱蓄期间禁止另一只手攻击（规则一）。
        ///
        /// 【为什么需要这条规则】连段计数和蓄力 CD 都是两只手共用的，
        /// 但「这一发是不是弱化版」是在【各自起手那一刻】分别定性的。
        /// 于是 CD 内同时长按左右键，两只手会各自拿到一发弱蓄 ——
        /// 玩家只要错开松手，就能白拿两发。
        ///
        /// 比喻：一次只发一颗哑弹，但两只手同时伸过来各领了一颗。
        ///       现在改成：左手攥着哑弹时，右手不许再拿东西。
        ///
        /// 【为什么要看 IsCharging 而不只看 IsWeakened】
        /// ChargeModule.IsWeakened 在 Stop() 里是刻意不清的（它是"刚结束那次
        /// 蓄力的属性"，调用方要在 Release 之后读）。所以必须叠上"还在蓄"，
        /// 否则一次弱蓄结束后这个标记会一直挂着，把另一只手永久锁死。
        /// ==========================================================
        /// </summary>
        public bool IsAnyWeakenedCharging
            => (modules[0].IsCharging && modules[0].IsWeakened)
            || (modules[1].IsCharging && modules[1].IsWeakened);

        /// <summary>
        /// 有任意一只手的蓄力是【在空中起手】的 —— 空中悬停的判据。
        ///
        /// 取"任意一只"而不是"全部"：空中起手是玩家主动做出的高风险承诺，
        /// 只要有一只手做了这个承诺，悬停这条规则就该生效。
        /// </summary>
        public bool IsAnyChargeStartedInAir
            => (modules[0].IsCharging && modules[0].BeganAirborne)
            || (modules[1].IsCharging && modules[1].BeganAirborne);

        /// <summary>
        /// 【空中蓄力姿态生效中】空中起手 且 现在仍在空中。
        ///
        /// ==========================================================
        /// 这是"空中蓄力"这套特殊规则的【唯一判据】，所有相关行为都问它：
        ///   · 角色钉在原地悬停       （PlayerStateBase.IsAirChargePinned）
        ///   · 方向键与朝向锁死       （同上）
        ///   · Shift 变成"指哪去哪"    （PlayerCommandRouter.TryDashOrThrust）
        ///
        /// 【为什么必须是同一个判据】这三件事在玩家眼里是【一个姿态】的三个侧面。
        /// 各自写各自的条件，就会出现"人钉住了但 Shift 还是普通冲刺"
        /// 这种自相矛盾的组合 —— 玩家没法从表现反推规则，只会觉得随机。
        ///
        /// 【两个条件缺一不可】
        ///   空中起手 → 这是一次高风险承诺，性质在起手那刻定死，中途不变
        ///   仍在空中 → 承诺已经兑现完（落地了）就该恢复常规规则
        /// ==========================================================
        /// </summary>
        public bool IsAirChargeHovering
            => IsAnyChargeStartedInAir && sensor != null && !sensor.IsGrounded();

        public bool IsCharging(InputCmd cmd)
        {
            var m = GetModule(cmd);
            return m != null && m.IsCharging;
        }

        /// <summary>最先开始蓄力的那只手，供动画与光效使用。都没蓄时返回 null</summary>
        public ChargeModule PrimaryChargingModule
        {
            get
            {
                if (modules[0].IsCharging) return modules[0];
                if (modules[1].IsCharging) return modules[1];
                return null;
            }
        }

        // ==========================================================
        // 开始 / 结束
        // ==========================================================

        /// <summary>
        /// 开始蓄力。由 ComboInputBuffer 在「长按确认」时调用。
        /// </summary>
        public void BeginCharge(InputCmd cmd, int targetLevel, bool isAfterCombo)
        {
            ChargeModule m = GetModule(cmd);
            if (m == null || m.IsCharging) return;

            WeaponMoveSet weapon = weapons != null ? weapons.GetWeapon(m.Slot) : null;

            // 【强弱就在这一刻定性】CD 是全局的，不分左右手。
            // 之后蓄多久、什么时候松手都改变不了这一发的定性 —— 见 ChargeModule.IsWeakened。
            bool ready = weapons == null || weapons.IsChargeReady;

            // 【起手位置也在这一刻定性】决定这次蓄力要不要把角色钉在空中。
            // 同样是"这次蓄力的性质"，全程不变 —— 见 ChargeModule.BeganAirborne。
            bool airborne = sensor != null && !sensor.IsGrounded();

            m.Begin(weapon, targetLevel, isAfterCombo, ready, airborne);

            ApplyPenaltyIfNeeded();

            OnChargeStarted?.Invoke(m.Slot);
            OnChargeLevelChanged?.Invoke(m.Slot, 0);
            OnChargeProgressChanged?.Invoke(m.Slot, 0f);

            if (verboseLog)
            {
                if (!m.IsValid)
                {
                    Debug.Log($"[蓄力] {m.Slot} 这只手没有武器，蓄不出东西");
                }
                else if (m.IsWeakened)
                {
                    float left = weapons != null ? weapons.ChargeCooldownRemaining : 0f;
                    Debug.Log(
                        $"[蓄力] {m.Slot} 在 CD 内起手（还剩 {left:F2}s）→ 本发定性为弱化版 AA1，" +
                        "举多久都改不回来");
                }
                else
                {
                    Debug.Log($"[蓄力] {m.Slot} 开始，目标 AA{m.TargetLevel}，需要 {m.RequiredTime:F2}s");
                }
            }
        }

        /// <summary>
        /// 松手结算。由 ComboInputBuffer 在攻击键松开时调用。
        /// </summary>
        public ChargeReleaseResult ReleaseCharge(InputCmd cmd)
        {
            ChargeModule m = GetModule(cmd);
            if (m == null || !m.IsCharging) return ChargeReleaseResult.NotCharging;

            int level = m.TargetLevel;
            ChargeReleaseResult result = m.Release();

            FinishModule(m);

            if (verboseLog)
            {
                Debug.Log(result == ChargeReleaseResult.Fired
                    ? $"[蓄力] {m.Slot} 蓄满 AA{level}，释放"
                    : $"[蓄力] {m.Slot} 未蓄满就松手，连段清零");
            }

            return result;
        }

        /// <summary>
        /// 强制中断某只手的蓄力（受击、死亡、换武器）。
        /// 后果与未蓄满松手相同：什么都不放。
        /// </summary>
        public void CancelCharge(InputCmd cmd)
        {
            ChargeModule m = GetModule(cmd);
            if (m == null || !m.IsCharging) return;

            m.Stop();
            FinishModule(m);

            if (verboseLog) Debug.Log($"[蓄力] {m.Slot} 被打断");
        }

        /// <summary>中断两只手的蓄力</summary>
        public void CancelAll()
        {
            CancelCharge(InputCmd.MainAttack);
            CancelCharge(InputCmd.SubAttack);
        }

        private void FinishModule(ChargeModule m)
        {
            OnChargeLevelChanged?.Invoke(m.Slot, 0);
            OnChargeProgressChanged?.Invoke(m.Slot, 0f);
            OnChargeEnded?.Invoke(m.Slot);

            ReleasePenaltyIfNeeded();
        }

        // ==========================================================
        // 每帧推进
        // ==========================================================

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < modules.Length; i++)
            {
                ChargeModule m = modules[i];
                if (!m.IsCharging) continue;

                bool justCharged = m.Tick(dt);

                OnChargeProgressChanged?.Invoke(m.Slot, m.Progress);

                if (justCharged)
                {
                    OnChargeLevelChanged?.Invoke(m.Slot, m.TargetLevel);
                    if (verboseLog) Debug.Log($"[蓄力] {m.Slot} 蓄满 AA{m.TargetLevel}，松手即发");
                }
            }
        }

        // ==========================================================
        // 移动折损
        // ==========================================================

        private float MoveMultiplier
        {
            get
            {
                if (overrideMoveMultiplier) return moveMultiplierOverride;

                var cfg = state != null ? state.movementConfig : null;
                return cfg != null ? cfg.chargeMoveSpeedMultiplier : 0.2f;
            }
        }

        /// <summary>
        /// 贴便利贴：蓄力期间移速与跳跃力打折。
        ///
        /// 用属性修饰器而不是在每个状态里写 if(蓄力中)，好处是
        /// 和宝石、buff 的加成自动叠加，而且撕的时候按签名撕，不可能漏减。
        /// </summary>
        private void ApplyPenaltyIfNeeded()
        {
            if (penaltyApplied || state == null) return;

            // 倍率转成"减少百分之多少"的加法修饰
            state.stats.moveSpeed.AddModifier(
                StatModifier.Percent(MoveMultiplier - 1f, this));

            state.stats.jumpForce.AddModifier(
                StatModifier.Percent(jumpMultiplier - 1f, this));

            penaltyApplied = true;
        }

        private void ReleasePenaltyIfNeeded()
        {
            if (!penaltyApplied) return;
            if (IsAnyCharging) return;   // 另一只手还在蓄，折损保持

            state?.stats.RemoveAllModifiersFrom(this);
            penaltyApplied = false;
        }

        private void OnDisable()
        {
            // 组件被关掉时别把折损留在属性面板上
            CancelAll();
            ReleasePenaltyIfNeeded();
        }
    }
}