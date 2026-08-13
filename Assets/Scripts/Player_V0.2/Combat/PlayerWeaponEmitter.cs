using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【远程发射器】—— 由动画事件调用，已接入对象池。
    ///
    /// ⚠️ 必须和 Animator 挂在同一个 GameObject 上，否则动画事件找不到它。
    ///
    /// ==========================================================
    /// 【接池改造】Instantiate → ObjectPoolManager.Get(key)
    ///
    /// 这是 readme 第一军规的落实 ——
    /// 敌人子弹早就走池了，玩家子弹一直在裸生成，同屏一多就是 GC 卡顿。
    ///
    /// 注意生成端与回收端【必须同时改】。
    /// 只改这里、Player_Projectile 还在 Destroy 的话，
    /// 结果是：从池里取出来 → 撞墙被销毁 → 池里少一个 → 几十发之后池空了，
    /// 玩家彻底射不出子弹。这比不接池糟糕得多，而且症状延迟出现。
    ///
    /// 【子弹按武器走】
    /// 打出这一段的是哪把武器，就用哪把武器的 pool key。
    /// 于是「主武器是弓、副武器是枪」时左键射箭、右键射子弹，本文件一行不用改。
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

        private PlayerController controller;
        private ComboInputBuffer comboBuffer;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            comboBuffer = GetComponent<ComboInputBuffer>();
        }

        /// <summary>由动画事件调用：在指定帧射出一发子弹</summary>
        public void SpawnProjectile()
        {
            if (firePoint == null)
            {
                Debug.LogWarning("[发射器] 未指定枪口位置 firePoint，本次发射已跳过。", this);
                return;
            }

            // 打出这一段的是哪把武器？用它的子弹
            WeaponMoveSet weapon = comboBuffer != null ? comboBuffer.ActiveWeapon : null;

            string poolKey = (weapon != null && weapon.HasProjectile)
                ? weapon.projectilePoolKey
                : fallbackProjectilePoolKey;

            if (string.IsNullOrEmpty(poolKey))
            {
                Debug.LogWarning(
                    "[发射器] 当前武器没配 Projectile Pool Key，兜底也为空，本次发射已跳过。\n" +
                    "远程武器请在 WeaponMoveSet 资产上填写 Projectile Pool Key。", this);
                return;
            }

            // key 未注册时池会自己报 LogError，这里拿到 null 就静默跳过，避免刷屏
            GameObject bullet = ObjectPoolManager.Instance?.Get(poolKey);
            if (bullet == null) return;

            bullet.transform.position = firePoint.position;

            bool eightWay = (weapon != null) ? weapon.useEightWayAiming : fallbackEightWayAiming;
            Vector2 shootDirection = ResolveDirection(eightWay);

            Player_Projectile proj = bullet.GetComponent<Player_Projectile>();
            if (proj != null)
            {
                proj.Setup(shootDirection);
            }
            else
            {
                Debug.LogWarning(
                    $"[发射器] 池「{poolKey}」取出的对象上没有 Player_Projectile 组件。", bullet);
            }
        }

        private Vector2 ResolveDirection(bool useEightWay)
        {
            if (controller == null) return Vector2.right;

            Vector2 inputDir = controller.moveInput;

            if (!useEightWay || inputDir.magnitude <= 0.1f)
            {
                return new Vector2(controller.facingDirection, 0f);
            }

            // 八向吸附：把任意角度四舍五入到最近的 45 度
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