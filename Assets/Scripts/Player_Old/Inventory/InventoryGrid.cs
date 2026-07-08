using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InventoryGrid : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        public ItemData data;
        public int x;
        public int y;
        public bool rotated;
    }

    private static readonly List<InventoryGrid> allGrids = new List<InventoryGrid>();

    public static IReadOnlyList<InventoryGrid> AllGrids => allGrids;

    [Header("Grid")]
    public int columns = 10;
    public int rows = 6;
    public float cellSize = 64f;

    [Header("Grid Layout Match")]
    public float spacingX = 0f;
    public float spacingY = 0f;
    public float paddingLeft = 0f;
    public float paddingTop = 0f;

    [Header("UI")]
    public RectTransform itemLayer;
    public InventoryItemView itemPrefab;

    [Header("Preview")]
    public RectTransform previewLayer;
    public GameObject previewCellPrefab;
    public Color canPlaceColor = new Color(0f, 1f, 0f, 0.35f);
    public Color cannotPlaceColor = new Color(1f, 0f, 0f, 0.35f);
    public RectTransform hitArea;

    [Header("Demo Items")]
    public List<SpawnEntry> initialItems = new List<SpawnEntry>();

    private ItemInstance[,] cells;
    private readonly List<GameObject> previewCells = new List<GameObject>();

    private void Awake()
    {
        cells = new ItemInstance[columns, rows];

        if (!allGrids.Contains(this))
            allGrids.Add(this);
    }

    private void OnDestroy()
    {
        allGrids.Remove(this);
    }
    private void Start()
    {
        foreach (var entry in initialItems)
        {
            if (entry.data != null)
            {
                CreateItem(entry.data, entry.x, entry.y, entry.rotated);
            }
        }
    }

    public ItemInstance CreateItem(ItemData data, int x, int y, bool rotated = false)
    {
        if (data == null || itemPrefab == null || itemLayer == null)
            return null;

        var item = new ItemInstance(data)
        {
            rotated = rotated
        };

        var view = Instantiate(itemPrefab, itemLayer);
        item.view = view;
        view.Bind(this, item);

        if (!TryPlace(item, x, y))
        {
            Destroy(view.gameObject);
            return null;
        }

        return item;
    }

    public static InventoryGrid GetGridUnderPointer(Vector2 screenPos, Camera cam)
    {
        for (int i = allGrids.Count - 1; i >= 0; i--)
        {
            InventoryGrid grid = allGrids[i];
            if (grid == null || grid.hitArea == null)
                continue;

            if (RectTransformUtility.RectangleContainsScreenPoint(grid.hitArea, screenPos, cam))
                return grid;
        }

        return null;
    }

    public bool CanPlace(ItemInstance item, int x, int y)
    {
        if (item == null || item.data == null)
            return false;

        if (x < 0 || y < 0)
            return false;

        if (x + item.Width > columns)
            return false;

        if (y + item.Height > rows)
            return false;

        for (int ix = 0; ix < item.Width; ix++)
        {
            for (int iy = 0; iy < item.Height; iy++)
            {
                if (cells[x + ix, y + iy] != null)
                    return false;
            }
        }

        return true;
    }

    public bool TryPlace(ItemInstance item, int x, int y)
    {
        if (item == null)
            return false;

        bool hadOldPlace = item.IsPlaced;
        int oldX = item.x;
        int oldY = item.y;

        if (hadOldPlace)
            Clear(item);

        if (!CanPlace(item, x, y))
        {
            if (hadOldPlace)
            {
                item.x = oldX;
                item.y = oldY;
                Occupy(item, oldX, oldY);
            }

            return false;
        }

        item.x = x;
        item.y = y;
        Occupy(item, x, y);

        if (item.view != null)
            item.view.UpdateVisual();

        return true;
    }

    public void Remove(ItemInstance item)
    {
        if (item == null || !item.IsPlaced)
            return;

        Clear(item);
        item.x = -1;
        item.y = -1;
    }

    public bool TryRotate(ItemInstance item)
    {
        if (item == null || item.data == null || !item.data.canRotate)
            return false;

        bool wasPlaced = item.IsPlaced;
        int oldX = item.x;
        int oldY = item.y;
        bool oldRotated = item.rotated;

        if (wasPlaced)
            Clear(item);

        item.rotated = !item.rotated;

        if (wasPlaced)
        {
            if (CanPlace(item, oldX, oldY))
            {
                item.x = oldX;
                item.y = oldY;
                Occupy(item, oldX, oldY);

                if (item.view != null)
                    item.view.UpdateVisual();

                return true;
            }

            item.rotated = oldRotated;
            item.x = oldX;
            item.y = oldY;
            Occupy(item, oldX, oldY);

            if (item.view != null)
                item.view.UpdateVisual();

            return false;
        }

        if (item.view != null)
            item.view.UpdateVisual();

        return true;
    }

    public Vector2 GetItemAnchoredPosition(ItemInstance item)
    {
        float stepX = cellSize + spacingX;
        float stepY = cellSize + spacingY;

        return new Vector2(
            paddingLeft + item.x * stepX,
            -(paddingTop + item.y * stepY)
        );
    }

    public Vector2 GetItemSize(ItemInstance item)
    {
        float w = item.Width * cellSize + (item.Width - 1) * spacingX;
        float h = item.Height * cellSize + (item.Height - 1) * spacingY;
        return new Vector2(w, h);
    }

    public Vector2 GetCellAnchoredPosition(int x, int y)
    {
        float stepX = cellSize + spacingX;
        float stepY = cellSize + spacingY;

        return new Vector2(
            paddingLeft + x * stepX,
            -(paddingTop + y * stepY)
        );
    }

    public bool AnchoredPositionToCell(Vector2 anchoredPosition, out int x, out int y)
    {
        x = -1;
        y = -1;

        float stepX = cellSize + spacingX;
        float stepY = cellSize + spacingY;

        x = Mathf.FloorToInt((anchoredPosition.x - paddingLeft) / stepX);
        y = Mathf.FloorToInt((-anchoredPosition.y - paddingTop) / stepY);

        return x >= 0 && y >= 0 && x < columns && y < rows;
    }

    public void ShowPlacementPreview(ItemInstance item, int x, int y)
    {
        ClearPlacementPreview();

        if (item == null || previewLayer == null || previewCellPrefab == null)
            return;

        bool canPlace = CanPlace(item, x, y);
        Color color = canPlace ? canPlaceColor : cannotPlaceColor;

        for (int ix = 0; ix < item.Width; ix++)
        {
            for (int iy = 0; iy < item.Height; iy++)
            {
                int cellX = x + ix;
                int cellY = y + iy;

                if (cellX < 0 || cellY < 0 || cellX >= columns || cellY >= rows)
                    continue;

                GameObject go = Instantiate(previewCellPrefab, previewLayer);
                previewCells.Add(go);

                RectTransform rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = GetCellAnchoredPosition(cellX, cellY);
                    rt.sizeDelta = new Vector2(cellSize, cellSize);
                }

                Image img = go.GetComponent<Image>();
                if (img != null)
                {
                    img.color = color;
                    img.raycastTarget = false;
                }
            }
        }
    }

    public void ClearPlacementPreview()
    {
        for (int i = 0; i < previewCells.Count; i++)
        {
            if (previewCells[i] != null)
                Destroy(previewCells[i]);
        }

        previewCells.Clear();
    }

    private void Occupy(ItemInstance item, int x, int y)
    {
        for (int ix = 0; ix < item.Width; ix++)
        {
            for (int iy = 0; iy < item.Height; iy++)
            {
                cells[x + ix, y + iy] = item;
            }
        }
    }

    private void Clear(ItemInstance item)
    {
        for (int ix = 0; ix < columns; ix++)
        {
            for (int iy = 0; iy < rows; iy++)
            {
                if (cells[ix, iy] == item)
                    cells[ix, iy] = null;
            }
        }
    }

    public Vector2 GetItemCenterAnchoredPosition(ItemInstance item)
    {
        Vector2 topLeft = GetItemAnchoredPosition(item);
        Vector2 size = GetItemSize(item);

        return topLeft + new Vector2(size.x * 0.5f, -size.y * 0.5f);
    }

    public bool CenterAnchoredPositionToPlacement(Vector2 centerAnchoredPosition, ItemInstance item, out int x, out int y)
    {
        x = -1;
        y = -1;

        if (item == null)
            return false;

        float stepX = cellSize + spacingX;
        float stepY = cellSize + spacingY;

        float itemWidthPx = item.Width * cellSize + (item.Width - 1) * spacingX;
        float itemHeightPx = item.Height * cellSize + (item.Height - 1) * spacingY;

        // 先把中心点反推成左上角的 anchoredPosition
        float topLeftX = centerAnchoredPosition.x - itemWidthPx * 0.5f;
        float topLeftY = centerAnchoredPosition.y + itemHeightPx * 0.5f;

        // 再把左上角对齐到最近网格
        x = Mathf.RoundToInt((topLeftX - paddingLeft) / stepX);
        y = Mathf.RoundToInt((-topLeftY - paddingTop) / stepY);

        if (x < 0 || y < 0)
            return false;

        if (x + item.Width > columns || y + item.Height > rows)
            return false;

        return true;
    }
}