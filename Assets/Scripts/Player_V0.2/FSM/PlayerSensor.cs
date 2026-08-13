using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【感官组件】—— 玩家的脚底雷达、头顶雷达与体型控制。
    ///
    /// ==========================================================
    /// 从 PlayerStateMachine 里搬出来的三件事：
    ///   IsGrounded()          脚下有没有地面
    ///   CanStand()            头顶有没有障碍（决定能不能从蹲下站起）
    ///   SetColliderHeight()   蹲下时压扁碰撞体，并让受击小框跟着下降
    ///
    /// 顺带把射线参数从硬编码魔法数字改成读 Config ——
    /// 原先 (0.5, 0.2) / 0.1 / (0.4, 0.1) 这些数字直接写在方法体里，
    /// 想调地面检测精度得去翻代码。
    ///
    /// 比喻：状态机原本既是导演又是场务还兼职测量员。
    ///       现在测量的活交给专职的人，导演只管喊开始。
    /// ==========================================================
    /// </summary>
    public class PlayerSensor : MonoBehaviour
    {
        [Header("检测点 (场景引用)")]
        [Tooltip("脚底传感器位置")]
        public Transform groundCheck;

        [Tooltip("头顶雷达位置")]
        public Transform ceilingCheck;

        [Tooltip("受击小框。蹲下时会跟着物理框一起下降")]
        public Transform hurtboxCore;

        [Header("图层")]
        [Tooltip("什么图层算是地面/天花板")]
        public LayerMask groundLayer;

        [Header("Debug")]
        [Tooltip("在 Scene 视图里画出检测盒")]
        public bool drawGizmos = true;

        // ---- 碰撞体原始尺寸备份 ----
        private BoxCollider2D coll;
        private Vector2 originalColliderSize;
        private Vector2 originalColliderOffset;
        private Vector3 originalHurtboxPos;

        private PlayerState playerState;
        private bool isCrouchingNow = false;

        /// <summary>当前是否处于蹲下体型</summary>
        public bool IsCrouchCollider => isCrouchingNow;

        public Vector2 OriginalColliderSize => originalColliderSize;
        public Vector2 OriginalColliderOffset => originalColliderOffset;

        private PlayerMovementConfig Cfg
            => playerState != null ? playerState.movementConfig : null;

        private void Awake()
        {
            coll = GetComponent<BoxCollider2D>();
            playerState = GetComponent<PlayerState>();

            if (playerState != null) playerState.EnsureInitialized();

            if (coll != null)
            {
                originalColliderSize = coll.size;
                originalColliderOffset = coll.offset;
            }
            else
            {
                Debug.LogError("[PlayerSensor] 找不到 BoxCollider2D，蹲下压扁功能不可用。", this);
            }

            if (hurtboxCore != null) originalHurtboxPos = hurtboxCore.localPosition;

            if (groundCheck == null)
                Debug.LogError("[PlayerSensor] 未指定 GroundCheck，IsGrounded() 将永远返回 false！", this);
        }

        // ==========================================================
        // 检测
        // ==========================================================

        public bool IsGrounded()
        {
            if (groundCheck == null) return false;

            var cfg = Cfg;
            Vector2 boxSize = cfg != null ? cfg.groundCheckBoxSize : new Vector2(0.5f, 0.2f);
            float distance = cfg != null ? cfg.groundCheckDistance : 0.1f;

            return Physics2D.BoxCast(
                groundCheck.position, boxSize, 0f, Vector2.down, distance, groundLayer).collider != null;
        }

        /// <summary>头顶有没有障碍。没挂雷达时默认允许站起</summary>
        public bool CanStand()
        {
            if (ceilingCheck == null) return true;

            var cfg = Cfg;
            Vector2 boxSize = cfg != null ? cfg.ceilingCheckBoxSize : new Vector2(0.4f, 0.1f);
            float distance = cfg != null ? cfg.ceilingCheckDistance : 0.1f;

            return !Physics2D.BoxCast(
                ceilingCheck.position, boxSize, 0f, Vector2.up, distance, groundLayer);
        }

        // ==========================================================
        // 体型
        // ==========================================================

        public void SetColliderHeight(bool isCrouching)
        {
            if (coll == null) return;

            isCrouchingNow = isCrouching;

            if (isCrouching)
            {
                var cfg = Cfg;
                float heightMultiplier = cfg != null ? cfg.crouchColliderHeightMultiplier : 0.6f;

                coll.size = new Vector2(originalColliderSize.x, originalColliderSize.y * heightMultiplier);

                float heightDifference = originalColliderSize.y - coll.size.y;
                coll.offset = new Vector2(
                    originalColliderOffset.x,
                    originalColliderOffset.y - (heightDifference * 0.5f));

                // 受击小框跟着物理框按比例下降
                if (hurtboxCore != null)
                {
                    hurtboxCore.localPosition = new Vector3(
                        originalHurtboxPos.x,
                        originalHurtboxPos.y - (heightDifference * 0.5f),
                        originalHurtboxPos.z);
                }
            }
            else
            {
                coll.size = originalColliderSize;
                coll.offset = originalColliderOffset;
                if (hurtboxCore != null) hurtboxCore.localPosition = originalHurtboxPos;
            }
        }

        // ==========================================================
        // Gizmos
        // ==========================================================

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            var cfg = Application.isPlaying ? Cfg : null;

            if (groundCheck != null)
            {
                Vector2 size = cfg != null ? cfg.groundCheckBoxSize : new Vector2(0.5f, 0.2f);
                float dist = cfg != null ? cfg.groundCheckDistance : 0.1f;

                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(groundCheck.position + Vector3.down * dist, size);
            }

            if (ceilingCheck != null)
            {
                Vector2 size = cfg != null ? cfg.ceilingCheckBoxSize : new Vector2(0.4f, 0.1f);
                float dist = cfg != null ? cfg.ceilingCheckDistance : 0.1f;

                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(ceilingCheck.position + Vector3.up * dist, size);
            }
        }
    }
}
