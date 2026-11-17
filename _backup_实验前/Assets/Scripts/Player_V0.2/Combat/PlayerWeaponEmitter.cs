using System.Collections;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【远程发射器】—— 由动画事件调用，走对象池。
    ///
    /// ⚠️ 必须和 Animator 挂在同一个 GameObject 上，否则动画事件找不到它。
    ///
    /// ==========================================================
    /// 【本批新增】连射（速射枪）
    ///
    /// 一次动画事件 = 一轮连射，发数与间隔由招式节点配置：
    ///   B1 → 3 发    B2 → 6 发    B3 → 9 发
    /// 于是"三段普攻"的差异变成压制时长的差异，玩家能感觉到一路突突的节奏。
    ///
    /// 【为什么不在动画里插 9 个事件】
    /// 那样发数就被焊死在动画上了 —— 想把 B3 从 9 发调成 12 发，
    /// 得去动画时间轴上手加 3 个事件，而且三段普攻共用同一个动画剪辑时根本做不到。
    /// 参数留在节点上，改数字就行。
    ///
    /// 【弹匣可换】
    /// 子弹池 key 优先取招式节点的覆盖值，没填才用武器上的默认值。
    /// 枪是同一把，但 BB1/BB2 可以换成更大更快的特制弹。
    /// ==========================================================
    /// </summary>
    public class PlayerWeaponEmitter : MonoBehaviour
    {
        [Header("场景引用")]
        [Tooltip("子弹发射的枪口位置")]
        public Transform firePoint;

        [Header("兜底配置 (未装备武器时使用)")]
        [Tooltip("没有 WeaponLoadout、或武器未配 pool key 时使用的池 key")]
        public string fallbackProjectilePoolKey = "";

        [Tooltip("兜底是否使用八向瞄准")]
        public bool fallbackEightWayAiming = true;

        [Header("Debug")]
        public bool verboseLog = false;

        private PlayerController controller;
        private ComboInputBuffer comboBuffer;
        private PlayerAimProvider aim;

        private Coroutine burstRoutine;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            comboBuffer = GetComponent<ComboInputBuffer>();
            aim = GetComponent<PlayerAimProvider>();
        }

        // ==========================================================
        // 动画事件入口
        // ==========================================================

        /// <summary>
        /// 由动画事件调用：打出本招配置的一整轮弹幕。
        ///
        /// 单发招式（count = 1）会立刻同步射出，不起协程 ——
        /// 避免为了一发子弹产生一次协程分配。
        /// </summary>
        public void SpawnProjectile()
        {
            if (firePoint == null)
            {
                Debug.LogWarning("[发射器] 未指定枪口位置 firePoint，本次发射已跳过。", this);
                return;
            }

            ComboNode node = comboBuffer != null ? comboBuffer.currentNode : null;
            WeaponMoveSet weapon = comboBuffer != null ? comboBuffer.ActiveWeapon : null;

            string poolKey = ResolvePoolKey(node, weapon);
            if (string.IsNullOrEmpty(poolKey))
            {
                Debug.LogWarning(
                    "[发射器] 没找到可用的子弹池 key，本次发射已跳过。\n" +
                    "请在武器资产上填 Projectile Pool Key，或在招式节点上填覆盖值。", this);
                return;
            }

            int count = node != null ? Mathf.Max(1, node.projectileCount) : 1;
            float interval = node != null ? node.projectileInterval : 0f;

            // 单发或零间隔 → 同步射完，不必起协程
            if (count == 1 || interval <= 0f)
            {
                Vector2 dir = ResolveDirection(weapon);
                for (int i = 0; i < count; i++) FireOne(poolKey, node, dir);
                return;
            }

            // 上一轮还没打完就被新的一轮顶掉（连招推进很快时会发生）
            CancelBurst();
            burstRoutine = StartCoroutine(BurstRoutine(poolKey, node, weapon, count, interval));
        }

        private IEnumerator BurstRoutine(
            string poolKey, ComboNode node, WeaponMoveSet weapon, int count, float interval)
        {
            bool recomputeAim = node == null || node.recomputeAimPerShot;
            Vector2 dir = ResolveDirection(weapon);

            for (int i = 0; i < count; i++)
            {
                if (recomputeAim) dir = ResolveDirection(weapon);

                FireOne(poolKey, node, dir);

                // 最后一发之后不用再等
                if (i < count - 1) yield return new WaitForSeconds(interval);
            }

            burstRoutine = null;
        }

        /// <summary>
        /// 中断连射。招式被打断时必须调用，
        /// 否则玩家已经被击飞了，枪还在原地继续突突。
        /// </summary>
        public void CancelBurst()
        {
            if (burstRoutine != null)
            {
                StopCoroutine(burstRoutine);
                burstRoutine = null;
            }
        }

        // ==========================================================
        // 单发
        // ==========================================================

        private void FireOne(string poolKey, ComboNode node, Vector2 direction)
        {
            // key 未注册时池会自己报 LogError，这里拿到 null 就静默跳过避免刷屏
            GameObject bullet = ObjectPoolManager.Instance?.Get(poolKey);
            if (bullet == null) return;

            bullet.transform.position = firePoint.position;

            Player_Projectile proj = bullet.GetComponent<Player_Projectile>();
            if (proj == null)
            {
                Debug.LogWarning($"[发射器] 池「{poolKey}」取出的对象上没有 Player_Projectile。", bullet);
                return;
            }

            // 先套用倍率与覆盖，再 Setup 定方向。
            // 这些改动会在子弹回池时由 OnDespawn 还原，不会污染池子。
            if (node != null)
            {
                proj.Configure(
                    node.projectileSpeedMultiplier,
                    node.projectileScaleMultiplier,
                    node.projectileDamageOverride);
            }

            proj.Setup(direction);

            if (verboseLog) Debug.Log($"[发射器] 射出 {poolKey}，方向 {direction}");
        }

        // ==========================================================
        // 解析
        // ==========================================================

        /// <summary>招式覆盖 &gt; 武器默认 &gt; 组件兜底</summary>
        private string ResolvePoolKey(ComboNode node, WeaponMoveSet weapon)
        {
            if (node != null && !string.IsNullOrEmpty(node.projectilePoolKeyOverride))
                return node.projectilePoolKeyOverride;

            if (weapon != null && weapon.HasProjectile)
                return weapon.projectilePoolKey;

            return fallbackProjectilePoolKey;
        }

        /// <summary>
        /// 瞄准方向统一问 PlayerAimProvider 要 ——
        /// 鼠标瞄准 / 八向回退 / 以后的辅助瞄准都在那一个组件里，
        /// 本文件不需要知道玩家用的是鼠标还是手柄。
        /// </summary>
        private Vector2 ResolveDirection(WeaponMoveSet weapon)
        {
            if (aim != null)
            {
                // 开火时按策略转向瞄准方向（默认策略：攻击时转，平时不转）
                aim.FaceAimIfNeeded();
                return aim.AimDirection;
            }

            // ---- 没挂瞄准组件时的兜底：沿用旧的八向逻辑 ----
            if (controller == null) return Vector2.right;

            bool useEightWay = (weapon != null) ? weapon.useEightWayAiming : fallbackEightWayAiming;
            Vector2 inputDir = controller.moveInput;

            if (!useEightWay || inputDir.magnitude <= 0.1f)
            {
                return new Vector2(controller.facingDirection, 0f);
            }

            float angle = Mathf.Atan2(inputDir.y, inputDir.x) * Mathf.Rad2Deg;
            angle = Mathf.Round(angle / 45f) * 45f;

            Vector2 dir = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                Mathf.Sin(angle * Mathf.Deg2Rad));

            if (dir.x < -0.1f) controller.SetFacingDirection(-1);
            else if (dir.x > 0.1f) controller.SetFacingDirection(1);

            return dir;
        }
    }
}
