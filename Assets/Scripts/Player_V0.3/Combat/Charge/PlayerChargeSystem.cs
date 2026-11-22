using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【蓄力系统】—— 持有两只手的蓄力模块，挂在玩家身上。
    /// </summary>
    public class PlayerChargeSystem : MonoBehaviour
    {
        // 【参数搬家 2026-11-21】蓄力的移动折损（开关、移速倍率、跳跃倍率）放在每个蓄力招式（ComboNode）的
        // 「蓄力设定」里：蓄到哪一级就用哪一招的设定，两只手同时蓄时取更重的。只此一处，没有全局开关。

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

        // 当前贴在属性上的折损倍率（1 = 没折损）
        private float appliedMoveMultiplier = 1f;
        private float appliedJumpMultiplier = 1f;

        // ---- 减速渐变：每只手各记一份（下标同 modules）----
        private readonly bool[] slowTracking = new bool[2];           // 这只手正在蓄力（已经开始记了）
        private readonly ComboNode[] slowNode = new ComboNode[2];     // 当前生效的减速招式（null = 不减速）
        private readonly float[] slowStartTime = new float[2];        // 这一招的减速从什么时候开始
        private readonly float[] slowMoveFrom = new float[2];         // 渐变起点（移速）
        private readonly float[] slowJumpFrom = new float[2];         // 渐变起点（跳跃）
        private readonly float[] slowMoveNow = { 1f, 1f };            // 这只手此刻的倍率（移速）
        private readonly float[] slowJumpNow = { 1f, 1f };            // 这只手此刻的倍率（跳跃）

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

        // ==========================================================
        // 开始 / 结束
        // ==========================================================

        /// <summary>
        /// 开始蓄力。由 ComboInputBuffer 在「长按确认」时调用。
        /// 按住越久等级越高（见 ChargeModule）。
        /// </summary>
        /// <param name="startLevel">起步等级：连段为 0 时 1，第 N 段普攻后为 N</param>
        /// <param name="minHold">起步时间（ComboInputBuffer.chargeStartHoldTime，默认 0.3s）</param>
        public void BeginCharge(InputCmd cmd, int startLevel, float minHold, bool isAfterCombo)
        {
            ChargeModule m = GetModule(cmd);
            if (m == null || m.IsCharging) return;

            WeaponMoveSet weapon = weapons != null ? weapons.GetWeapon(m.Slot) : null;

            m.Begin(weapon, startLevel, minHold, isAfterCombo);

            RefreshPenalty();

            OnChargeStarted?.Invoke(m.Slot);
            OnChargeLevelChanged?.Invoke(m.Slot, 0);
            OnChargeProgressChanged?.Invoke(m.Slot, 0f);

            if (verboseLog)
            {
                PlayerLog.Info(!m.IsValid
                    ? $"[蓄力] {m.Slot} 这只手没有武器，蓄不出东西"
                    : $"[蓄力] {m.Slot} 起手，从 {m.StartLevel} 级起步，按住满 {minHold:F2}s 生效" +
                      $"，上限 {m.CapLevel} 级（按侧键 +1）");
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
                    : $"[蓄力] {m.Slot} 还没过起步时间就松手，什么都不放");
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

            RefreshPenalty();
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

                bool levelChanged = m.Tick(dt);

                OnChargeProgressChanged?.Invoke(m.Slot, m.Progress);

                if (levelChanged)
                {
                    OnChargeLevelChanged?.Invoke(m.Slot, m.CurrentLevel);
                    if (verboseLog) PlayerLog.Info($"[蓄力] {m.Slot} 现在 {m.CurrentLevel} 级，松手即发");
                }
            }

            // 等级变了，对应的招式就变了 → 折损跟着换（没变化时不动属性）
            RefreshPenalty();
        }

        // ==========================================================
        // 移动折损
        // ==========================================================

        /// <summary>
        /// 这只手此刻蓄的是哪一招：按当前等级（还没到 1 级时按起步等级）在武器里找
        /// 「等级 ≤ 它」里最高的那个蓄力招。只看裸按键的那一招（AA1/AA2/AA3），不看方向变招。
        /// </summary>
        private static ComboNode ChargeNodeFor(ChargeModule m)
        {
            WeaponMoveSet weapon = m.Weapon;
            if (weapon == null) return null;

            int level = m.CurrentLevel > 0 ? m.CurrentLevel : Mathf.Max(1, m.StartLevel);

            ComboNode best = null;
            Pick(weapon.followUps, level, ref best);
            Pick(weapon.openers, level, ref best);
            return best;
        }

        private static void Pick(System.Collections.Generic.List<ComboNode> nodes, int level, ref ComboNode best)
        {
            if (nodes == null) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                ComboNode n = nodes[i];
                if (n == null || !n.IsValidChargeNode || n.chargeLevel > level) continue;
                if (n.inputSequence != null && n.inputSequence.Count > 1) continue;   // 方向变招不算
                if (best == null || n.chargeLevel > best.chargeLevel) best = n;
            }
        }

        /// <summary>
        /// 按此刻两只手蓄的招，算出该贴的移速 / 跳跃折损；和已贴的不一样才重贴。
        /// </summary>
        private void RefreshPenalty()
        {
            if (state == null || state.stats == null) return;

            float move = 1f;
            float jump = 1f;

            for (int i = 0; i < modules.Length && i < slowTracking.Length; i++)
            {
                TickHandSlow(i);
                move = Mathf.Min(move, slowMoveNow[i]);
                jump = Mathf.Min(jump, slowJumpNow[i]);
            }

            // 渐变中每帧都在变：变化太小就不重贴，省得每帧都拆装修饰
            bool sameMove = Mathf.Abs(move - appliedMoveMultiplier) < 0.001f;
            bool sameJump = Mathf.Abs(jump - appliedJumpMultiplier) < 0.001f;
            if (sameMove && sameJump) return;

            bool wasSlowed = appliedMoveMultiplier < 1f || appliedJumpMultiplier < 1f;

            state.stats.RemoveAllModifiersFrom(this);

            // 倍率转成"减少百分之多少"的加法修饰
            if (move < 1f) state.stats.moveSpeed.AddModifier(StatModifier.Percent(move - 1f, this));
            if (jump < 1f) state.stats.jumpForce.AddModifier(StatModifier.Percent(jump - 1f, this));

            appliedMoveMultiplier = move;
            appliedJumpMultiplier = jump;

            // 只在开始减速 / 解除时打日志（渐变途中每帧都会走到这里）
            bool isSlowed = move < 1f || jump < 1f;
            if (verboseLog && isSlowed != wasSlowed)
                PlayerLog.Info(isSlowed
                    ? $"[蓄力] 移动折损开始：移速 ×{move:F2}，跳跃 ×{jump:F2}"
                    : "[蓄力] 移动折损解除");
        }

        /// <summary>
        /// 算出第 i 只手此刻的移速 / 跳跃倍率：
        /// 当前蓄的招勾了减速 → 从渐变起点按 Charge Slow Ramp Time 线性变到终点倍率。
        /// 刚开始蓄 → 起点 = 这一招的起始倍率；中途换到另一招 → 起点 = 换招那一刻的实际倍率（不会突然变快）。
        /// </summary>
        private void TickHandSlow(int i)
        {
            ChargeModule m = modules[i];
            if (!m.IsCharging)
            {
                slowTracking[i] = false;
                slowNode[i] = null;
                slowMoveNow[i] = 1f;
                slowJumpNow[i] = 1f;
                return;
            }

            ComboNode node = ChargeNodeFor(m);
            ComboNode slow = node != null && node.chargeSlowsMovement ? node : null;

            if (!slowTracking[i] || slow != slowNode[i])
            {
                bool fresh = !slowTracking[i];
                slowTracking[i] = true;
                slowNode[i] = slow;
                slowStartTime[i] = Time.time;

                if (slow != null)
                {
                    slowMoveFrom[i] = fresh ? Mathf.Clamp01(slow.chargeMoveSpeedStart) : slowMoveNow[i];
                    slowJumpFrom[i] = fresh ? Mathf.Clamp(slow.chargeJumpForceStart, 0.1f, 1f) : slowJumpNow[i];
                }
            }

            if (slow == null)
            {
                slowMoveNow[i] = 1f;
                slowJumpNow[i] = 1f;
                return;
            }

            float ramp = slow.ResolvedChargeSlowRampTime;
            float t = ramp > 0f ? Mathf.Clamp01((Time.time - slowStartTime[i]) / ramp) : 1f;

            slowMoveNow[i] = Mathf.Lerp(slowMoveFrom[i], Mathf.Clamp01(slow.chargeMoveSpeedMultiplier), t);
            slowJumpNow[i] = Mathf.Lerp(slowJumpFrom[i], Mathf.Clamp(slow.chargeJumpForceMultiplier, 0.1f, 1f), t);
        }

        private void OnDisable()
        {
            // 组件被关掉时别把折损留在属性面板上
            CancelAll();
            if (state != null && state.stats != null) state.stats.RemoveAllModifiersFrom(this);
            appliedMoveMultiplier = 1f;
            appliedJumpMultiplier = 1f;
        }
    }
}