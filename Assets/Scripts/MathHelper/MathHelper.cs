using UnityEngine;

public static class MathHelper
{
    /// <summary>
    /// 根据给定角度计算出N边形相对于原点的xy坐标
    /// </summary>
    /// <param name="N">多边形边数</param>
    /// <param name="angle">角度，rad</param>
    /// <param name="rMin">最小半径</param>
    /// <param name="rMax">最大半径</param>
    /// <returns></returns>
    public static Vector2 N_PolygonAngleEquation(int N, float angle, float rMin, float rMax)
    {
        float sector = 2.0f * Mathf.PI / N;      // 每个扇区的角度
        float halfSector = Mathf.PI / N;         // 扇区的一半角度
        float minCos = Mathf.Cos(halfSector);    // 该多边形底层的最小余弦值

        // 计算当前角度在扇区内的相对位置
        float term = angle % sector;
        float cosValue = Mathf.Cos(halfSector - Mathf.Abs(term - halfSector));

        //线性映射半径，确保边缘平直
        float r = rMin + (rMax - rMin) * (cosValue - minCos) / (1.0f - minCos);

        //转换为直角坐标并生成子弹
        float x = r * Mathf.Cos(angle);
        float y = r * Mathf.Sin(angle);

        return new Vector2(x, y);
    }

    /// <summary>
    /// 根据给定角度计算出N角星相对于原点的xy坐标
    /// </summary>
    /// <param name="N">多边形边数</param>
    /// <param name="angle">角度，rad</param>
    /// <param name="rMin">最小半径</param>
    /// <param name="rMax">最大半径</param>
    /// <returns></returns>
    public static Vector2 N_StarEquation(int N, float angle, float rMin, float rMax)
    {
        float sector = 2.0f * Mathf.PI / N;   // 五角星有 5 个分叉，每个扇区 72 度
        float halfSector = Mathf.PI / N;      // 36 度
        float minCos = 0.809f;                   // Cos(36度) ≈ 0.809

        // 2. 周期性余弦运算
        float term = angle % sector;
        float cosValue = Mathf.Cos(halfSector - Mathf.Abs(term - halfSector));

        // 3. 【核心差异】引入 3 次方非线性坍缩，强制让子弹在凹角处“锐折”
        float progress = (cosValue - minCos) / (1.0f - minCos);
        float sharpRatio = Mathf.Pow(progress, 3.0f);

        // 4. 计算最终半径
        float r = rMin + (rMax - rMin) * sharpRatio;

        // 5. 转换为直角坐标并生成子弹
        float x = r * Mathf.Cos(angle);
        float y = r * Mathf.Sin(angle);

        return new Vector2(x, y);
     }
}
