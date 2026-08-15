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
    /// </summary>
    public class PlayerManaSystem : MonoBehaviour
    {
        [Header("命中回蓝")]
        [Tooltip("每点招式伤害回多少 MP。0.2 表示 10 伤害的招回 2 点")]
        public float mpPerDamage = 0.2f;

        [Tooltip("单次命中最多回多少 MP。防止高伤大招一下回满")]
        public int maxMpPerHit = 10;

        [Tooltip("远程子弹命中是否也回蓝")]
        public bool gainFromProjectiles = true;

        [Header("自然恢复")]
        [Tooltip("每秒自然回复多少 MP。填 0 关闭")]
        public float regenPerSecond = 2f;

        [Tooltip("飞行等消耗 MP 的行为结束后，隔多久才开始自然恢复")]
        public float regenDelayAfterSpend = 1.0f;

        [Header("Debug")]
        public bool verboseLog = false;

        private PlayerState state;
        private PlayerHitDetection hitDetection;

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
            if (!gainFromProjectiles) return;
            GainFromDamage(moveDamage);
        }

        private void GainFromDamage(int moveDamage)
        {
            if (state == null || moveDamage <= 0) return;

            int gain = Mathf.Clamp(
                Mathf.RoundToInt(moveDamage * mpPerDamage), 0, maxMpPerHit);

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
                regenBlockedUntil = Time.time + regenDelayAfterSpend;
                mpAccumulator = 0f;
            }

            lastSeenMp = now;
        }

        private void TickRegen()
        {
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
