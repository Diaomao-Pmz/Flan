using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【蓄力系统】—— 持有两只手的蓄力模块，挂在玩家身上。
    /// </summary>
    public class PlayerChargeSystem : MonoBehaviour
    {
        // 【参数搬家】蓄力的移动折损（开关、移速倍率、跳跃倍率）统一放在 PlayerMovementConfig 的 Charge Settings。
        // 原先本组件上还有一份「覆盖移速倍率」和「跳跃倍率」—— 同一条规则两个来源，
        // 改了 config 却被组件上的覆盖值顶掉，所以删掉，只留 config 一处。

        [Header("Debug")]
        public bool verboseLog = false;

        // ---- 事件（UI / 特效订阅）----
        /// <summary>某只手的蓄力等级变化 (槽位, 等级)。0 = 还没到 1 级 / 已结束，>0 = 当前达到的等级（每升一级广播一次）</summary>
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

        private bool penaltyApplied;

        private void Awake()
        {
            state = GetComponent<PlayerState>();
            controller = GetComponent<PlayerController>();
            buffer = GetComponent<ComboInputBuffer>();
            weapons = GetComponent<WeaponLoadout>();

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

<<<<<<< Updated upstream
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

=======
>>>>>>> Stashed changes
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

<<<<<<< Updated upstream
=======
        /// <summary>这只手正在蓄力，且已过起步时间（松手能打出东西）</summary>
        public bool IsValidCharging(InputCmd cmd)
        {
            var m = GetModule(cmd);
            return m != null && m.IsCharging && m.IsCharged;
        }

        /// <summary>某只手的蓄力上限变化 (槽位, 新上限 1~3)</summary>
        public event System.Action<WeaponSlot, int> OnChargeCapChanged;

        /// <summary>
        /// 【实验】把某只手的蓄力上限提高一级（鼠标前侧键 = 主手，后侧键 = 副手）。
        /// 3 级之后再按回到 1 级；可以连按。只在该手蓄力中有效。
        /// 等级会沿 1→2→3→1 升到上限后停住。
        /// </summary>
        public void RaiseLevelCap(InputCmd cmd)
        {
            ChargeModule m = GetModule(cmd);
            if (m == null || !m.IsCharging || !m.IsValid) return;

            int cap = m.RaiseCap();
            OnChargeCapChanged?.Invoke(m.Slot, cap);

            if (verboseLog)
                PlayerLog.Info($"[蓄力] {m.Slot} 上限 → {cap} 级（当前 {m.CurrentLevel} 级，起步 {m.StartLevel} 级）");
        }

>>>>>>> Stashed changes
        // ==========================================================
        // 开始 / 结束
        // ==========================================================

        /// <summary>
        /// 开始蓄力。由 ComboInputBuffer 在「长按确认」时调用。
        /// 按住越久等级越高（见 ChargeModule）。
        /// </summary>
        /// <param name="startLevel">起步等级：原地起手 0，第 N 段普攻后为 N</param>
        /// <param name="minHold">普攻后起手时的保险锁时长（刚打完那一招的 chargeMinHoldAfter）</param>
        public void BeginCharge(InputCmd cmd, int startLevel, float minHold, bool isAfterCombo)
        {
            ChargeModule m = GetModule(cmd);
            if (m == null || m.IsCharging) return;

            WeaponMoveSet weapon = weapons != null ? weapons.GetWeapon(m.Slot) : null;

            m.Begin(weapon, startLevel, minHold, isAfterCombo);

            ApplyPenaltyIfNeeded();

            OnChargeStarted?.Invoke(m.Slot);
            OnChargeLevelChanged?.Invoke(m.Slot, 0);
            OnChargeProgressChanged?.Invoke(m.Slot, 0f);

            if (verboseLog)
            {
                PlayerLog.Info(!m.IsValid
                    ? $"[蓄力] {m.Slot} 这只手没有武器，蓄不出东西"
                    : m.StartLevel > 0
                        ? $"[蓄力] {m.Slot} 普攻后起手，从 {m.StartLevel} 级起步，按住满 {minHold:F2}s 解锁"
                        : $"[蓄力] {m.Slot} 原地起手，从 0 级开始");
            }
        }

        /// <summary>
        /// 松手结算。由 ComboInputBuffer 在攻击键松开时调用。
        /// </summary>
        public ChargeReleaseResult ReleaseCharge(InputCmd cmd)
        {
            ChargeModule m = GetModule(cmd);
            if (m == null || !m.IsCharging) return ChargeReleaseResult.NotCharging;

            int level = m.CurrentLevel;
            ChargeReleaseResult result = m.Release();

            FinishModule(m);

            if (verboseLog)
            {
                PlayerLog.Info(result == ChargeReleaseResult.Fired
                    ? $"[蓄力] {m.Slot} 松手，打出 {level} 级"
                    : $"[蓄力] {m.Slot} 还没到 1 级就松手，连段清零");
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

            if (verboseLog) PlayerLog.Info($"[蓄力] {m.Slot} 被打断");
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

                bool leveledUp = m.Tick(dt);

                OnChargeProgressChanged?.Invoke(m.Slot, m.Progress);

                if (leveledUp)
                {
                    OnChargeLevelChanged?.Invoke(m.Slot, m.CurrentLevel);
<<<<<<< Updated upstream
                    if (verboseLog) Debug.Log($"[蓄力] {m.Slot} 升到 {m.CurrentLevel} 级，松手即发");
=======
                    if (verboseLog) PlayerLog.Info($"[蓄力] {m.Slot} 现在 {m.CurrentLevel} 级，松手即发");
>>>>>>> Stashed changes
                }
            }
        }

        // ==========================================================
        // 移动折损
        // ==========================================================

        private PlayerMovementConfig MoveCfg => state != null ? state.movementConfig : null;

        private float MoveMultiplier => MoveCfg != null ? MoveCfg.chargeMoveSpeedMultiplier : 0.2f;
        private float JumpMultiplier => MoveCfg != null ? MoveCfg.chargeJumpForceMultiplier : 0.7f;

        /// <summary>
        /// 贴便利贴：蓄力期间移速与跳跃力打折。
        /// </summary>
        private void ApplyPenaltyIfNeeded()
        {
            if (penaltyApplied || state == null) return;

            // 开关关闭（默认）→ 蓄力时正常跑跳，不贴折损便利贴。倍率参数原样保留
            if (state.movementConfig != null && !state.movementConfig.chargeSlowsMovement) return;

            // 倍率转成"减少百分之多少"的加法修饰
            state.stats.moveSpeed.AddModifier(
                StatModifier.Percent(MoveMultiplier - 1f, this));

            state.stats.jumpForce.AddModifier(
                StatModifier.Percent(JumpMultiplier - 1f, this));

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