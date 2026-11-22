using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【魔力收支】—— 命中回蓝 + 自然恢复。
    ///
    /// ==========================================================
    /// 为什么单独一个组件，而不是塞进 PlayerHitDetection 或 PlayerHealth：
    ///
    /// PlayerHitDetection 的职责是"判定框打中了谁"，
    /// PlayerHealth 的职责是"存血量魔力并广播变化"。
    /// "打中敌人能换多少蓝"是【战斗经济规则】—— 和这两者都不是一回事，
    /// 而且以后大概率会变复杂（暴击多回？某些招式不回？连段越长回得越多？）。
    ///
    /// 塞进判定框的话，那个文件就会慢慢变成"判定 + 经济 + 打击感"的杂物间。
    /// ==========================================================
    ///
    /// 【回蓝口径】按你的设定，回蓝量与【招式配置的伤害】成正比，
    /// 而不是敌人实际掉的血 —— 这样敌人有护甲/减伤时玩家的资源循环不会崩。
    ///
    /// 【参数搬家】回蓝相关的数值原先挂在本组件的 Inspector 上，
    /// 现在统一放进 PlayerStateConfig（资源类出厂值），本组件只负责"怎么算"。
    /// 每秒回蓝读的是 PlayerStats.mpRegenPerSecond —— 它可以被技能树加成。
    /// </summary>
    public class PlayerManaSystem : MonoBehaviour
    {
        [Header("Debug")]
        public bool verboseLog = false;

        private PlayerState state;
        private PlayerHitDetection hitDetection;

        // ---- 数值读取窗口（出厂值在 PlayerStateConfig）----
        private PlayerStateConfig Cfg => state != null ? state.stateConfig : null;

        private float MpPerDamage => Cfg != null ? Cfg.mpPerDamage : 0.2f;
        private int MaxMpPerHit => Cfg != null ? Cfg.maxMpPerHit : 10;
        private bool GainFromProjectiles => Cfg == null || Cfg.gainMpFromProjectiles;
        private float RegenDelayAfterSpend => Cfg != null ? Cfg.mpRegenDelayAfterSpend : 1f;

        /// <summary>每秒回蓝 = 出厂值 + 成长加成</summary>
        private float RegenPerSecond => state != null ? state.stats.mpRegenPerSecond.Value : 0f;

        // 小数部分累计器：MP 是整数，不攒着的话每帧的零头会被丢掉
        private float mpAccumulator;
        private float regenBlockedUntil;
        private int lastSeenMp;

        private void Awake()
        {
            state = GetComponent<PlayerState>();
            hitDetection = GetComponent<PlayerHitDetection>();

            if (state != null) state.EnsureInitialized();
        }

        private void OnEnable()
        {
            if (hitDetection != null) hitDetection.OnEnemyHit += HandleEnemyHit;
        }

        private void OnDisable()
        {
            if (hitDetection != null) hitDetection.OnEnemyHit -= HandleEnemyHit;
        }

        private void Start()
        {
            if (state != null) lastSeenMp = state.health.currentMP;
        }

        private void Update()
        {
            DetectSpending();
            TickRegen();
        }

        // ==========================================================
        // 命中回蓝
        // ==========================================================

        private void HandleEnemyHit(EntityBase enemy, int moveDamage)
        {
            GainFromDamage(moveDamage);
        }

        /// <summary>
        /// 供远程子弹调用（子弹命中时不经过 PlayerHitDetection）。
        /// Player_Projectile 命中敌人后调一下这个即可。
        /// </summary>
        public void OnProjectileHit(int moveDamage)
        {
            if (!GainFromProjectiles) return;
            GainFromDamage(moveDamage);
        }

        private void GainFromDamage(int moveDamage)
        {
            if (state == null || moveDamage <= 0) return;

            int gain = Mathf.Clamp(
                Mathf.RoundToInt(moveDamage * MpPerDamage), 0, MaxMpPerHit);

            if (gain <= 0) return;

            state.health.AddMP(gain);
            lastSeenMp = state.health.currentMP;

            if (verboseLog) Debug.Log($"[魔力] 命中回蓝 +{gain}（招式伤害 {moveDamage}）");
        }

        // ==========================================================
        // 自然恢复
        // ==========================================================

        /// <summary>
        /// 检测玩家是否正在消耗 MP（飞行、无敌等）。
        ///
        /// 用"MP 掉了"来推断，而不是让飞行状态主动通知 ——
        /// 这样以后任何新的耗蓝行为都自动被覆盖，不需要回来加通知调用。
        /// </summary>
        private void DetectSpending()
        {
            if (state == null) return;

            int now = state.health.currentMP;

            if (now < lastSeenMp)
            {
                regenBlockedUntil = Time.time + RegenDelayAfterSpend;
                mpAccumulator = 0f;
            }

            lastSeenMp = now;
        }

        private void TickRegen()
        {
            float regenPerSecond = RegenPerSecond;
            if (state == null || regenPerSecond <= 0f) return;
            if (Time.time < regenBlockedUntil) return;
            if (state.health.currentMP >= state.health.maxMP) return;

            mpAccumulator += regenPerSecond * Time.deltaTime;

            if (mpAccumulator >= 1f)
            {
                int whole = Mathf.FloorToInt(mpAccumulator);
                mpAccumulator -= whole;

                state.health.AddMP(whole);
                lastSeenMp = state.health.currentMP;
            }
        }
    }
}
