using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ShapeFormationController : MonoBehaviour
{
    // 记录所有子弹以及它们的最终目标局部坐标
    private List<Transform> bullets = new List<Transform>();
    private List<Vector2> targetLocalPositions = new List<Vector2>();

    private MonoBehaviour attackScriptToEnable; // 展开完毕后要激活的脚本 (如 RotatingFormation)

    public void AddBullet(Transform bullet, Vector2 targetLocalPos)
    {
        bullets.Add(bullet);
        targetLocalPositions.Add(targetLocalPos);
        // 子弹生成时，强制将它们的位置重置在父物体中心 (firePoint)
        bullet.localPosition = Vector3.zero;
    }

    // Emitter 把所有子弹添加完后，调用这个方法开始展开
    public void StartFormation(float duration, MonoBehaviour scriptToEnable)
    {
        attackScriptToEnable = scriptToEnable;

        // 如果需要展开脚本启动，先把最终的攻击脚本禁用
        if (attackScriptToEnable != null) attackScriptToEnable.enabled = false;

        StartCoroutine(DoFormation(duration));
    }

    private IEnumerator DoFormation(float duration)
    {
        float timer = 0;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float percent = timer / duration;
            // 可以加入一个缓动曲线，比如 Mathf.SmoothStep，让展开更顺滑
            float smoothPercent = Mathf.SmoothStep(0f, 1f, percent);

            for (int i = 0; i < bullets.Count; i++)
            {
                if (bullets[i] != null)
                {
                    // 使用 Lerp 将子弹从中心点缓慢移动到目标点
                    bullets[i].localPosition = Vector2.Lerp(Vector2.zero, targetLocalPositions[i], smoothPercent);
                }
            }
            yield return null;
        }

        // 展开完毕，确保所有子弹精准到达目标位置
        for (int i = 0; i < bullets.Count; i++)
        {
            if (bullets[i] != null) bullets[i].localPosition = targetLocalPositions[i];
        }

        // 激活真正的攻击逻辑（让方阵开始旋转、让圆环散开）
        if (attackScriptToEnable != null) attackScriptToEnable.enabled = true;

        // 功成身退，销毁自己，不留垃圾
        Destroy(this);
    }
}