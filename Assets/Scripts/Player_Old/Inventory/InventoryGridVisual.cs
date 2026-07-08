using UnityEngine;
using UnityEngine.UI;

public class InventoryGridVisual : MonoBehaviour
{
    public int columns = 10;
    public int rows = 6;
    public GameObject cellPrefab;

    private void Start()
    {
        CreateGrid();
    }

    public void CreateGrid()
    {
        // 先清掉旧格子
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }

        // 生成格子
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                Instantiate(cellPrefab, transform);
            }
        }
    }
}