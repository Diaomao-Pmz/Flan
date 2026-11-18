using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【生命/魔力数据总线】
    /// </summary>
    [System.Serializable]
    public class PlayerHealth
    {
        [Header("Health & Mana (运行时)")]
        public int currentHP = 100;
        public int currentMP = 100;

        // ==========================================================
        // 【最大值不再存在这里】改为实时读 PlayerStats 上的 maxHP / maxMP。
        // ==========================================================
        private PlayerStats stats;

        /// <summary>最大生命值 = 出厂值（PlayerStateConfig）+ 成长加成</summary>
        public int maxHP => stats != null ? Mathf.Max(1, stats.maxHP.IntValue) : 100;

        /// <summary>最大魔力值 = 出厂值（PlayerStateConfig）+ 成长加成</summary>
        public int maxMP => stats != null ? Mathf.Max(0, stats.maxMP.IntValue) : 100;

        // 上一次的最大值，用来算"上限涨了多少"
        private int lastMaxHP;
        private int lastMaxMP;
        [Tooltip("受击后的无敌保护期总长")]
        public float invulnerableDuration = 0.35f;

        [Header("Runtime Debug (只读观察)")]
        [SerializeField]
        [Tooltip("当前是否无敌。由下方引用计数决定，不要手动勾选")]
        private bool isUntargetableDisplay = false;

        // ==========================================
        // 无敌引用计数
        // ==========================================
        private readonly HashSet<object> untargetableSources = new HashSet<object>();

        /// <summary>当前是否处于无敌。计数 > 0 即为真</summary>
        public bool isUntargetable => untargetableSources.Count > 0;

        // ==========================================
        // 事件广播
        // ==========================================
        public event System.Action OnStatChanged;
        public event System.Action<Vector2> OnPlayerHit;

        // ==========================================================
        // 【阶段0 预埋 · 当前按要求注释】玩家死亡广播
        // ==========================================================

        public void Init(PlayerCombatConfig config, PlayerStats playerStats)
        {
            if (config != null)
            {
                invulnerableDuration = config.invulnerableDuration;
            }

            // 订阅最大值变化：先退订再订阅，防止重复 Init 时订两次
            if (stats != null)
            {
                stats.maxHP.OnValueChanged -= HandleMaxHPChanged;
                stats.maxMP.OnValueChanged -= HandleMaxMPChanged;
            }

            stats = playerStats;

            if (stats != null)
            {
                stats.maxHP.OnValueChanged += HandleMaxHPChanged;
                stats.maxMP.OnValueChanged += HandleMaxMPChanged;
            }

            currentHP = maxHP;
            currentMP = maxMP;
            lastMaxHP = maxHP;
            lastMaxMP = maxMP;

            untargetableSources.Clear();
            isUntargetableDisplay = false;

            OnStatChanged?.Invoke();
        }

        // ==========================================
        // 受伤
        // ==========================================

        public void TakeDamage(int damage, Vector2 knockbackForce, MonoBehaviour coroutineRunner)
        {
            // 正在无敌 —— 直接无视后续所有伤害和击飞
            if (isUntargetable) return;

            currentHP -= damage;
            if (currentHP < 0) currentHP = 0;
            OnStatChanged?.Invoke();

            // 从被击中的这一帧立刻开始无敌倒计时
            if (coroutineRunner != null)
            {
                coroutineRunner.StartCoroutine(InvulnerableRoutine());
            }

            // 广播：通知状态机去击飞，通知 Controller 去闪烁
            OnPlayerHit?.Invoke(knockbackForce);

            Debug.Log($"[健康总线] 芙兰受到了 {damage} 点伤害，当前血量: {currentHP}");

            // TODO: 阶段4 接 DeadState 时解除注释
            // CheckDeath();
        }

        /// <summary>
        /// 【阶段0 预埋 · 当前按要求注释】统一死亡判定入口。
        /// </summary>
        private void CheckDeath()
        {
            // TODO: 阶段4 接 DeadState 时解除注释
        }

        // private bool hasBroadcastDeath = false;   // 与上方 CheckDeath 一同解除注释

        private IEnumerator InvulnerableRoutine()
        {
            // 用一个专属 token 作为签名，和 Relay 宝石的无敌请求互不干扰
            object token = InvulnerableTokens.HitStun;

            RequestUntargetable(token);
            yield return new WaitForSeconds(invulnerableDuration);
            ReleaseUntargetable(token);
        }

        // ==========================================
        // 无敌：引用计数 API
        // ==========================================

        /// <summary>
        /// 请求无敌（计数 +1）。
        /// source 是签名，通常传 this 或一个专属 token。
        /// 同一 source 重复请求只算一次。
        /// </summary>
        public void RequestUntargetable(object source)
        {
            if (source == null) return;

            bool wasUntargetable = isUntargetable;
            untargetableSources.Add(source);
            isUntargetableDisplay = isUntargetable;

            if (!wasUntargetable && isUntargetable)
            {
                Debug.Log($"[躯干总线] 无敌开启（申请方: {source}）");
            }
        }

        /// <summary>
        /// 释放无敌（计数 -1）。只有所有请求方都释放后才真正解除。
        /// </summary>
        public void ReleaseUntargetable(object source)
        {
            if (source == null) return;

            bool wasUntargetable = isUntargetable;
            untargetableSources.Remove(source);
            isUntargetableDisplay = isUntargetable;

            if (wasUntargetable && !isUntargetable)
            {
                Debug.Log("[躯干总线] 无敌解除（所有申请方均已释放）");
            }
        }

        /// <summary>强制清空所有无敌请求。仅用于复活、重开局等场景。</summary>
        public void ForceClearUntargetable()
        {
            untargetableSources.Clear();
            isUntargetableDisplay = false;
        }

        /// <summary>
        /// 【兼容层】旧的布尔式接口。
        /// </summary>
        [System.Obsolete("请改用 RequestUntargetable(source) / ReleaseUntargetable(source)")]
        public void SetUntargetable(bool state)
        {
            if (state) RequestUntargetable(InvulnerableTokens.Legacy);
            else ReleaseUntargetable(InvulnerableTokens.Legacy);
        }

        // ==========================================
        // 最大值变化（技能树加成生效时）
        // ==========================================

        /// <summary>
        /// 上限变化时修正当前值：
        ///   上限提高 → 当前值同步加上提高的那部分（升级立刻能感受到，而不是一条空槽）
        ///   上限降低 → 当前值夹到新上限以内
        /// 最后广播 OnStatChanged，血条/蓝条自动刷新。
        /// </summary>
        private void HandleMaxHPChanged(float _)
        {
            int now = maxHP;
            int delta = now - lastMaxHP;
            lastMaxHP = now;

            if (delta > 0) currentHP += delta;
            currentHP = Mathf.Clamp(currentHP, 0, now);
            OnStatChanged?.Invoke();
        }

        private void HandleMaxMPChanged(float _)
        {
            int now = maxMP;
            int delta = now - lastMaxMP;
            lastMaxMP = now;

            if (delta > 0) currentMP += delta;
            currentMP = Mathf.Clamp(currentMP, 0, now);
            OnStatChanged?.Invoke();
        }

        // ==========================================
        // 资源增减
        // ==========================================

        public bool ConsumeMP(int amount)
        {
            if (currentMP >= amount)
            {
                SetMP(currentMP - amount);
                return true;
            }
            return false;
        }

        public void SetHP(int value)
        {
            currentHP = Mathf.Clamp(value, 0, maxHP);
            OnStatChanged?.Invoke();

            // TODO: 阶段4 接 DeadState 时解除注释
            // CheckDeath();
        }

        public void SetMP(int value)
        {
            currentMP = Mathf.Clamp(value, 0, maxMP);
            OnStatChanged?.Invoke();
        }

        public void AddHP(int value) => SetHP(currentHP + value);
        public void AddMP(int value) => SetMP(currentMP + value);
    }

    /// <summary>
    /// 无敌请求的标准签名。
    /// 用静态 object 而不是字符串，是为了避免 HashSet 里的字符串哈希开销。
    /// </summary>
    public static class InvulnerableTokens
    {
        /// <summary>受击后的短暂无敌</summary>
        public static readonly object HitStun = new object();

        /// <summary>旧布尔接口 SetUntargetable 的兼容签名</summary>
        public static readonly object Legacy = new object();

        /// <summary>滑铲全程无敌</summary>
        public static readonly object Slide = new object();

        /// <summary>飞行中按 Ctrl 解除飞行后的无敌下坠</summary>
        public static readonly object FlightDrop = new object();
    }
}
