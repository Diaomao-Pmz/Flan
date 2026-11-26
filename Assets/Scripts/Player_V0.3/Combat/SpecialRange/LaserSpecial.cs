using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【特殊远程 · 激光】
    /// 时序跟节拍总表走：前摇 = 预警线，判定段 = 开火（按间隔跳伤害），后摇开始时消失。
    /// 第一次打中某个敌人时带上招式的打断能力（breakMask、击退距离、Boss 硬直、受击反应）；
    /// 之后每一跳只有伤害 + 持续推动（ComboNode.bossPushSpeed）。
    /// 招式勾了「激光」消弹时，开火期间每一帧都把光束范围里的 Boss 子弹消掉（预警线阶段不消）。
    /// </summary>
    [Serializable]
    [SpecialMoveInfo("激光", SpecialMoveKind.Ranged)]
    public class LaserSpecial : SpecialMove
    {
        [Header("--- 形状 ---")]
        [Tooltip("激光最大长度（格）。被「挡激光的图层」挡住时截断")]
        [Min(0f)]
        public float maxLength = 25f;

        [Tooltip("激光判定宽度，同时也是开火时的视觉宽度")]
        [Min(0f)]
        public float beamWidth = 1.0f;

        [Tooltip("预警线的视觉宽度（不参与判定）")]
        [Min(0f)]
        public float telegraphWidth = 0.12f;

        [Tooltip("相对出招方向的额外偏转（度）。正数 = 往上抬，朝左朝右都一样")]
        public float aimAngleOffset = 0f;

        [Header("--- 方向 ---")]
        [Tooltip("不勾 = 方向固定为放出那一刻的面朝方向（水平）。\n" +
                 "勾上 = 从面朝方向出发，预警和开火期间一直转向鼠标，转速看下面的 Track Turn Speed。\n" +
                 "预警期间光束转到身后时角色跟着转身；开火后朝向锁定，不再转身")]
        public bool followMouse = false;

        [Tooltip("跟随鼠标时每秒最多转多少度。0 = 瞬间对准鼠标")]
        [Min(0f)]
        public float trackTurnSpeed = 0f;

        [Header("--- 移动 ---")]
        [Tooltip("勾上 = 正式开火（判定段）期间可以按 A / D 左右走，但不会转身。前摇（预警）阶段仍然不能动")]
        public bool moveDuringFire = false;

        [Tooltip("开火时的移动系数（只在勾了上一项时生效）。\n" +
                 "往开火方向走 = (1 - 系数) 倍移速；往反方向后退 = (1 + 系数) 倍移速。\n" +
                 "例：0.3 → 往前 0.7 倍、后退 1.3 倍")]
        [Range(0f, 1f)]
        public float fireMoveFactor = 0.3f;

        [Header("--- 遮挡 ---")]
        [Tooltip("哪些图层会挡住激光（一般勾墙壁 / 地形）。什么都不勾 = 不会被挡。\n" +
                 "别勾玩家和敌人所在的图层，否则激光打到人身上就停了")]
        public LayerMask obstacleLayers;

        [Header("--- 外观 ---")]
        [Tooltip("光束用的对象池 key（池里的预制体要带 SpriteRenderer）。Boss 激光用的是 \"Laser\"")]
        public string beamPoolKey = "Laser";

        [Tooltip("光束贴图。留空 = 用池里预制体自带的 Sprite")]
        public Sprite beamSprite;

        [Tooltip("勾选 = 贴图竖着画（光束沿 +Y，Pivot 设 Bottom·Center）。不勾 = 横着画（Pivot 设 Left·Center）")]
        public bool spriteIsVertical = true;

        [Tooltip("勾选 = 沿长度方向平铺贴图（贴图 Mesh Type = Full Rect）；纯色渐变的激光贴图保持不勾（拉伸）")]
        public bool tileTexture = false;

        [Tooltip("预警线颜色")]
        public Color telegraphColor = new Color(1f, 0.9f, 0.3f, 0.6f);

        [Tooltip("开火颜色。白色 = 贴图原色")]
        public Color fireColor = Color.white;

        [Header("--- 伤害 ---")]
        [Tooltip("每一跳的伤害")]
        [Min(0)]
        public int damagePerTick = 12;

        [Tooltip("跳伤间隔（秒）。开火那一刻立刻打第一跳")]
        [Min(0.02f)]
        public float damageTickInterval = 0.25f;

        [Tooltip("每一跳扣 Boss 多少护盾（只在 Boss 有盾时起作用；扣血仍看每跳伤害）。\n" +
                 "-1 = 不填，每跳按 Boss 的默认值扣（BossMechanic 的 Ranged Shield Damage）；0 = 不扣盾")]
        [Min(-1)]
        public int shieldBreakPerTick = -1;

        public override SpecialMoveRun Begin(SpecialMoveContext ctx, AttackInstance inst)
            => new LaserRun(this, ctx, inst);

        public override float MoveSpeedMultiplier(AttackInstance inst, float inputX, int facing)
        {
            if (!moveDuringFire || inst == null || inst.Phase != AttackPhase.Active) return 0f;
            if (Mathf.Abs(inputX) <= 0.1f) return 0f;

            bool forward = (inputX > 0f) == (facing > 0);
            return forward ? 1f - fireMoveFactor : 1f + fireMoveFactor;
        }

        // ==========================================================
        // 运行时
        // ==========================================================

        private sealed class LaserRun : SpecialMoveRun
        {
            /// <summary>持续推动比跳伤间隔多给这么久，两跳之间不断档</summary>
            private const float PushGrace = 0.1f;

            private static readonly List<Collider2D> hitBuffer = new List<Collider2D>(16);

            private readonly LaserSpecial cfg;
            private readonly SpecialMoveContext ctx;
            private readonly AttackInstance inst;

            private readonly HashSet<EntityBase> hitThisTick = new HashSet<EntityBase>();
            private readonly HashSet<EntityBase> struckOnce = new HashSet<EntityBase>();

            private SpriteRenderer beam;
            private int beamBorrowId = -1;

            private Vector2 direction;
            private bool firing;
            private float tickTimer;

            private bool gizmoVisible;
            private Vector2 gizmoCenter;
            private Vector2 gizmoSize;
            private float gizmoAngle;

            public LaserRun(LaserSpecial cfg, SpecialMoveContext ctx, AttackInstance inst)
            {
                this.cfg = cfg;
                this.ctx = ctx;
                this.inst = inst;

                direction = Rotate(new Vector2(ctx.Facing, 0f), cfg.aimAngleOffset * ctx.Facing);
                AcquireBeam();
            }

            public override void Tick(float dt)
            {
                if (done) return;

                if (inst.elapsed >= inst.activeEnd)
                {
                    End();
                    return;
                }

                bool telegraph = inst.elapsed < inst.windupEnd;

                if (cfg.followMouse)
                {
                    direction = Track(direction, dt);
                    FaceBeam();
                }

                Vector2 origin = ctx.Origin;
                float length = cfg.obstacleLayers.value == 0
                    ? cfg.maxLength
                    : RaycastLength(origin, direction, cfg.maxLength, cfg.obstacleLayers);

                Draw(origin, length, telegraph);

                if (telegraph) return;

                if (!firing)
                {
                    firing = true;
                    tickTimer = cfg.damageTickInterval;
                }

                CancelBullets(origin, length);

                tickTimer += dt;
                if (tickTimer >= cfg.damageTickInterval)
                {
                    tickTimer = 0f;
                    DamageTick(origin, length);
                }
            }

            public override void End()
            {
                if (done) return;
                done = true;
                gizmoVisible = false;

                if (beam == null) return;

                GameObject go = beam.gameObject;
                beam = null;

                ObjectPoolManager pool = ObjectPoolManager.Instance;
                if (pool == null || go == null) return;
                if (go.TryGetComponent(out PooledObject po) && (po.IsInPool || po.BorrowId != beamBorrowId)) return;

                pool.Recycle(go);
            }

            public override void DrawGizmos()
            {
                if (!gizmoVisible) return;

                Gizmos.color = firing ? new Color(1f, 0.1f, 0.1f, 1f) : new Color(1f, 0.9f, 0.3f, 1f);
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(gizmoCenter, Quaternion.Euler(0f, 0f, gizmoAngle), Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, gizmoSize);
                Gizmos.matrix = old;
            }

            // ---------------- 方向 ----------------

            private Vector2 Track(Vector2 current, float dt)
            {
                if (!TryGetMouseDirection(out Vector2 target)) return current;

                float sideSign = target.x < 0f ? -1f : 1f;
                target = Rotate(target, cfg.aimAngleOffset * sideSign);

                if (cfg.trackTurnSpeed <= 0f) return target;

                float from = Mathf.Atan2(current.y, current.x) * Mathf.Rad2Deg;
                float to = Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg;
                float angle = Mathf.MoveTowardsAngle(from, to, cfg.trackTurnSpeed * dt) * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            private bool TryGetMouseDirection(out Vector2 dir)
            {
                dir = Vector2.zero;
                if (ctx.aim == null) return false;
                if (ctx.aim.TryGetPointerDirection(out dir)) return true;

                dir = ctx.aim.AimDirection;
                return dir.sqrMagnitude > 0.0001f;
            }

            /// <summary>
            /// 光束转到身后时角色跟着转身。只在前摇里转：开火后朝向锁定。
            /// 动画已被别的招顶掉时也不转，免得抢走新招的朝向。
            /// </summary>
            private void FaceBeam()
            {
                if (inst.elapsed >= inst.windupEnd) return;
                if (!inst.ownsAnimation || ctx.controller == null) return;
                if (Mathf.Abs(direction.x) < 0.15f) return;

                int want = direction.x > 0f ? 1 : -1;
                if (want != ctx.Facing) ctx.controller.SetFacingDirection(want);
            }

            private static Vector2 Rotate(Vector2 v, float degrees)
            {
                if (Mathf.Abs(degrees) < 0.01f) return v;
                return (Vector2)(Quaternion.Euler(0f, 0f, degrees) * v);
            }

            private static float RaycastLength(Vector2 origin, Vector2 dir, float max, LayerMask obstacles)
            {
                RaycastHit2D hit = Physics2D.Raycast(origin, dir, max, obstacles);
                return hit.collider != null ? hit.distance : max;
            }

            // ---------------- 伤害 ----------------

            private void DamageTick(Vector2 origin, float length)
            {
                hitBuffer.Clear();
                hitThisTick.Clear();

                ContactFilter2D filter = ContactFilter2D.noFilter;
                filter.useTriggers = Physics2D.queriesHitTriggers;

                Vector2 center = origin + direction * (length * 0.5f);
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                Physics2D.OverlapBox(center, new Vector2(length, cfg.beamWidth), angle, filter, hitBuffer);

                ComboNode node = inst.node;
                float pushSpeed = node != null ? node.bossPushSpeed : 0f;
                float pushTime = Mathf.Min(cfg.damageTickInterval + PushGrace, Mathf.Max(0f, inst.activeEnd - inst.elapsed));

                for (int i = 0; i < hitBuffer.Count; i++)
                {
                    Collider2D hit = hitBuffer[i];
                    if (hit == null || !hit.CompareTag(ctx.enemyTag)) continue;

                    EntityBase enemy = hit.GetComponentInParent<EntityBase>();
                    if (enemy == null || !hitThisTick.Add(enemy)) continue;   // 同一个敌人身上多个碰撞体，一跳只算一次

                    bool first = struckOnce.Add(enemy) && node != null;
                    bool knock = first && node.bossKnockbackDistance > 0f;

                    enemy.TakeDamage(new DamageInfo(
                        cfg.damagePerTick, DamageType.Ranged, origin, ctx.instigator,
                        first ? node.breakMask : ActionCategory.None,
                        first ? node.hitReaction : HitReaction.None,
                        first ? node.hitReactionParam : 0f,
                        knock, knock ? new Vector2(node.bossKnockbackDistance, 0f) : Vector2.zero,
                        first ? node.bossHitStun : 0f,
                        pushSpeed, pushTime, cfg.shieldBreakPerTick));
                }
            }

            // ---------------- 消弹 ----------------

            /// <summary>
            /// 【激光消弹】招式勾了「激光」时，光束范围（碰墙截断后的长度 × 判定宽度，跟着光束转）里的 Boss 子弹全部消掉。
            /// 每一帧都扫，不跟跳伤间隔走：否则两跳之间飞进光束的子弹会漏掉，看起来时挡时不挡
            /// </summary>
            private void CancelBullets(Vector2 origin, float length)
            {
                ComboNode node = inst.node;
                if (node == null || !node.CancelsBulletsBy(BulletCancelMode.Laser)) return;

                Vector2 center = origin + direction * (length * 0.5f);
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                BulletCanceller.CancelInBox(center, new Vector2(length, cfg.beamWidth), angle);
            }

            // ---------------- 表现 ----------------

            private void AcquireBeam()
            {
                if (string.IsNullOrEmpty(cfg.beamPoolKey) || ObjectPoolManager.Instance == null) return;

                GameObject go = ObjectPoolManager.Instance.Get(cfg.beamPoolKey);
                if (go == null) return;

                if (!go.TryGetComponent(out beam))
                {
                    PlayerLog.Warn($"[激光] 池「{cfg.beamPoolKey}」取出的对象上没有 SpriteRenderer，这次没有画面。", go);
                    ObjectPoolManager.Instance.Recycle(go);
                    return;
                }

                beamBorrowId = go.TryGetComponent(out PooledObject po) ? po.BorrowId : -1;
                beam.enabled = false;

                if (cfg.beamSprite != null) beam.sprite = cfg.beamSprite;

                if (cfg.tileTexture)
                {
                    beam.drawMode = SpriteDrawMode.Tiled;
                    beam.tileMode = SpriteTileMode.Continuous;
                }
                else
                {
                    beam.drawMode = SpriteDrawMode.Simple;
                }
            }

            private void Draw(Vector2 origin, float length, bool telegraph)
            {
                float width = telegraph ? cfg.telegraphWidth : cfg.beamWidth;
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

                gizmoVisible = true;
                gizmoCenter = origin + direction * (length * 0.5f);
                gizmoSize = new Vector2(length, width);
                gizmoAngle = angle;

                if (beam == null) return;

                beam.enabled = true;
                beam.color = telegraph ? cfg.telegraphColor : cfg.fireColor;

                Transform t = beam.transform;
                t.position = origin;
                t.rotation = Quaternion.Euler(0f, 0f, cfg.spriteIsVertical ? angle - 90f : angle);

                Sprite sprite = beam.sprite;
                if (sprite == null) return;

                Vector2 target = cfg.spriteIsVertical ? new Vector2(width, length) : new Vector2(length, width);

                if (cfg.tileTexture)
                {
                    beam.size = target;
                }
                else
                {
                    Vector2 native = sprite.bounds.size;
                    if (native.x <= 0.0001f || native.y <= 0.0001f) return;
                    t.localScale = new Vector3(target.x / native.x, target.y / native.y, 1f);
                }

                // 不管贴图轴心点设在哪，都让光束从枪口开始、只往前伸：
                // 算出拉伸后贴图中心相对轴心点的偏移，把贴图中心对到光束中点
                Vector2 rectSize = sprite.rect.size;
                if (rectSize.x <= 0.0001f || rectSize.y <= 0.0001f) return;

                Vector2 pivot01 = new Vector2(sprite.pivot.x / rectSize.x, sprite.pivot.y / rectSize.y);
                Vector2 centerFromPivot = Vector2.Scale(new Vector2(0.5f, 0.5f) - pivot01, target);
                Vector2 beamCenter = origin + direction * (length * 0.5f);
                t.position = beamCenter - (Vector2)(t.rotation * centerFromPivot);
            }
        }
    }
}
