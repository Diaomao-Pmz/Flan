using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【远程发射器】—— 由动画事件调用。
    ///
    /// ⚠️ 必须和 Animator 挂在同一个 GameObject 上，否则动画事件找不到它。
    ///
    /// ==========================================================
    /// 【批次G 改动】子弹跟着武器走，不再是全局唯一预制体
    ///
    /// 既然武器分近战/远程，而且主副槽可以任意组合出「远近/远远」，
    /// 那"这一发子弹长什么样"就应该由【打出这一段的那把武器】决定。
    ///
    /// 取值优先级：
    ///   1. 当前这一段连招所属武器的 projectilePrefab
    ///   2. 本组件上的 fallbackProjectilePrefab（没装武器时的兜底）
    ///
    /// 于是「主武器是弓、副武器是枪」时，左键射箭、右键射子弹，
    /// 本文件一行都不用改。
    /// ==========================================================
    ///
    /// 【尚未改动 · 阶段6】
    ///   这里仍在用 Instantiate。按 readme 第一军规应当走 ObjectPoolManager ——
    ///   敌人子弹已经走池，玩家子弹还在裸生成，同屏一多就是 GC 卡顿。
    /// </summary>
    public class PlayerWeaponEmitter : MonoBehaviour
    {
        [Header("场景引用")]
        [Tooltip("子弹发射的枪口位置")]
        public Transform firePoint;

        [Header("兜底配置 (未装备武器时使用)")]
        [Tooltip("没有 WeaponLoadout 或武器未配子弹时的默认预制体")]
        public GameObject fallbackProjectilePrefab;

        [Tooltip("兜底子弹速度")]
        public float fallbackProjectileSpeed = 12f;

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

            GameObject prefab = (weapon != null && weapon.projectilePrefab != null)
                ? weapon.projectilePrefab
                : fallbackProjectilePrefab;

            if (prefab == null)
            {
                Debug.LogWarning(
                    "[发射器] 当前武器没配子弹预制体，兜底也为空，本次发射已跳过。\n" +
                    "远程武器请在 WeaponMoveSet 资产上填 Projectile Prefab。", this);
                return;
            }

            bool eightWay = (weapon != null) ? weapon.useEightWayAiming : fallbackEightWayAiming;
            Vector2 shootDirection = ResolveDirection(eightWay);

            // TODO: 阶段6 改走 ObjectPoolManager + IPoolable
            GameObject bullet = Instantiate(prefab, firePoint.position, Quaternion.identity);

            Player_Projectile proj = bullet.GetComponent<Player_Projectile>();
            if (proj != null) proj.Setup(shootDirection);
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
