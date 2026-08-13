using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【生命/魔力数据总线】
    ///
    /// 本次改动的核心是：无敌状态从「一个布尔开关」升级为「引用计数」。
    ///
    /// 【为什么？】
    /// 原先 isUntargetable 有 3~4 个主人：
    ///   受击无敌协程、Relay 冲刺无敌、Relay 滑铲无敌、Relay 跳跃无敌。
    /// 谁都能把它关掉。
    ///
    /// 比喻：一间房里三个人共用一盏灯的开关。
    ///       A 开灯干活，B 也开灯干活，A 干完随手关灯 —— B 还在黑暗里。
    ///
    /// 改成引用计数后：每人一把钥匙，请求无敌 +1，释放 -1，
    /// 全部还回来（计数归零）才熄灯。谁也没法误伤别人。
    /// </summary>
    [System.Serializable]
    public class PlayerHealth
    {
        [Header("Health & Mana Settings")]
        public int maxHP = 100;
        public int currentHP = 100;
        public int maxMP = 100;
        public int currentMP = 100;
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
        //
        // 你说过现在是测试阶段，故意保留「0血不死」的行为，所以整段封存。
        //
        // 解除步骤（阶段4 做 DeadState 时）：
        //   1. 取消下面这一行的注释
        //   2. 取消 TakeDamage() 里 CheckDeath() 调用的注释
        //   3. 取消 CheckDeath() 方法体内部的注释
        //   4. 在 PlayerStateMachine.Start() 里订阅它 → ChangeState(deadState)
        //
        // TODO: 阶段4 接 DeadState 时解除注释
        // public event System.Action OnPlayerDeath;
        // ==========================================================

        public void Init(PlayerCombatConfig config = null)
        {
            if (config != null)
            {
                maxHP = config.maxHP;
                maxMP = config.maxMP;
                invulnerableDuration = config.invulnerableDuration;
            }

            currentHP = maxHP;
            currentMP = maxMP;

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
        ///
        /// 原先的死亡检查写在 Player.TakeDamage(int) 里，
        /// 但敌人实际是通过 IDamageable.TakeDamage(in DamageInfo) 打进 PlayerState 的，
        /// 那条路径完全绕过了死亡检查 —— 这是个真实存在的断链。
        ///
        /// 修法是让死亡由数据层自己广播，而不是靠调用方「记得检查」。
        /// 方法体保留但内部注释，以维持你要求的「测试阶段 0血不死」行为。
        /// </summary>
        private void CheckDeath()
        {
            // TODO: 阶段4 接 DeadState 时解除注释
            //
            // if (currentHP > 0) return;
            // if (hasBroadcastDeath) return;   // 防重复广播
            // hasBroadcastDeath = true;
            // Debug.Log("[健康总线] 玩家死亡，广播 OnPlayerDeath");
            // OnPlayerDeath?.Invoke();
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
        ///
        /// 保留它是为了让还没来得及改的调用方不至于编译失败。
        /// 新代码请直接用 RequestUntargetable / ReleaseUntargetable，
        /// 否则多个系统仍然会共用 Legacy 这一个签名，退化回「共用一盏灯开关」的老问题。
        /// </summary>
        [System.Obsolete("请改用 RequestUntargetable(source) / ReleaseUntargetable(source)")]
        public void SetUntargetable(bool state)
        {
            if (state) RequestUntargetable(InvulnerableTokens.Legacy);
            else ReleaseUntargetable(InvulnerableTokens.Legacy);
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
    }
}
