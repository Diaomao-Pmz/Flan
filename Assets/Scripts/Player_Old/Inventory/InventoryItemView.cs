using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventoryItemView : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform visualRect;

    private RectTransform rectTransform;
    private InventoryGrid grid;
    private ItemInstance item;
    private bool originalRotated;

    private bool dragging;
    private int originalX;
    private int originalY;

    private RectTransform dragLayer;
    private GameObject dragGhost;
    private RectTransform dragGhostRect;
    private Image dragGhostImage;

    private Vector2 lastPointerScreenPos;
    private Camera lastPointerCamera;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (visualRect == null)
        {
            Transform visual = transform.Find("Visual");
            if (visual != null)
                visualRect = visual.GetComponent<RectTransform>();
        }

        if (iconImage == null)
        {
            Transform icon = transform.Find("Visual/Icon");
            if (icon != null)
                iconImage = icon.GetComponent<Image>();
        }

        GameObject dragLayerObj = GameObject.Find("DragLayer");
        if (dragLayerObj != null)
            dragLayer = dragLayerObj.GetComponent<RectTransform>();
        else
            Debug.LogWarning("DragLayer not found in scene.");
    }

    private void Update()
    {
        if (!dragging)
            return;

        if (Input.GetKeyDown(KeyCode.R))
        {
            RotateDraggedItem();
        }
    }

    public void Bind(InventoryGrid grid, ItemInstance item)
    {
        this.grid = grid;
        this.item = item;
        UpdateVisual();
    }

    private Vector2 GetBaseVisualSize()
    {
        return new Vector2(
            item.data.width * grid.cellSize + (item.data.width - 1) * grid.spacingX,
            item.data.height * grid.cellSize + (item.data.height - 1) * grid.spacingY
        );
    }

    public void UpdateVisual()
    {
        if (grid == null || item == null)
            return;

        if (iconImage != null)
        {
            iconImage.enabled = true;
            iconImage.sprite = item.data.icon;
            iconImage.raycastTarget = true;
            iconImage.preserveAspect = false;

            RectTransform iconRT = iconImage.rectTransform;
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;
        }

        rectTransform.SetParent(grid.itemLayer, false);
        rectTransform.sizeDelta = grid.GetItemSize(item);
        rectTransform.localEulerAngles = Vector3.zero;

        if (visualRect != null)
        {
            Vector2 baseVisualSize = new Vector2(
                item.data.width * grid.cellSize + (item.data.width - 1) * grid.spacingX,
                item.data.height * grid.cellSize + (item.data.height - 1) * grid.spacingY
            );

            visualRect.anchorMin = new Vector2(0.5f, 0.5f);
            visualRect.anchorMax = new Vector2(0.5f, 0.5f);
            visualRect.pivot = new Vector2(0.5f, 0.5f);
            visualRect.anchoredPosition = Vector2.zero;
            visualRect.sizeDelta = baseVisualSize;
            visualRect.localEulerAngles = item.rotated ? new Vector3(0f, 0f, -90f) : Vector3.zero;
        }

        if (!dragging && item.IsPlaced)
            rectTransform.anchoredPosition = grid.GetItemAnchoredPosition(item);

        rectTransform.SetAsLastSibling();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (item == null || grid == null || !item.IsPlaced)
            return;

        dragging = true;
        originalX = item.x;
        originalY = item.y;
        originalRotated = item.rotated;

        lastPointerScreenPos = eventData.position;
        lastPointerCamera = eventData.pressEventCamera;

        grid.Remove(item);

        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0f;

        CreateDragGhost();
        UpdateDragGhostVisual();
        RefreshPreview(lastPointerScreenPos, lastPointerCamera);
    }

    private void CreateDragGhost()
    {
        if (dragLayer == null || iconImage == null)
            return;

        if (dragGhost != null)
            Destroy(dragGhost);

        dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dragGhost.transform.SetParent(dragLayer, false);

        dragGhostRect = dragGhost.GetComponent<RectTransform>();
        dragGhostImage = dragGhost.GetComponent<Image>();

        dragGhostRect.anchorMin = new Vector2(0.5f, 0.5f);
        dragGhostRect.anchorMax = new Vector2(0.5f, 0.5f);
        dragGhostRect.pivot = new Vector2(0.5f, 0.5f);
        dragGhostRect.localScale = Vector3.one;
        dragGhostRect.SetAsLastSibling();

        dragGhostImage.sprite = iconImage.sprite;
        dragGhostImage.type = iconImage.type;
        dragGhostImage.material = iconImage.material;
        dragGhostImage.color = iconImage.color;
        dragGhostImage.raycastTarget = false;
        dragGhostImage.preserveAspect = false;
    }

    private void UpdateDragGhostVisual()
    {
        if (dragGhostRect == null || grid == null || item == null)
            return;

        Vector2 baseVisualSize = GetBaseVisualSize();

        dragGhostRect.anchorMin = new Vector2(0.5f, 0.5f);
        dragGhostRect.anchorMax = new Vector2(0.5f, 0.5f);
        dragGhostRect.pivot = new Vector2(0.5f, 0.5f);

        dragGhostRect.sizeDelta = baseVisualSize;
        dragGhostRect.localEulerAngles = item.rotated ? new Vector3(0f, 0f, -90f) : Vector3.zero;
    }

    private void RotateDraggedItem()
    {
        if (item == null || grid == null)
            return;

        item.rotated = !item.rotated;
        UpdateDragGhostVisual();
        RefreshPreview(lastPointerScreenPos, lastPointerCamera);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || grid == null)
            return;

        lastPointerScreenPos = eventData.position;
        lastPointerCamera = eventData.pressEventCamera;

        if (dragGhostRect != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dragLayer, eventData.position, eventData.pressEventCamera, out var localPoint);

            dragGhostRect.anchoredPosition = localPoint;
        }

        RefreshPreview(eventData.position, eventData.pressEventCamera);
    }

    private void RefreshPreview(Vector2 screenPos, Camera cam)
    {
        InventoryGrid targetGrid = InventoryGrid.GetGridUnderPointer(screenPos, cam);

        for (int i = 0; i < InventoryGrid.AllGrids.Count; i++)
            InventoryGrid.AllGrids[i]?.ClearPlacementPreview();

        if (targetGrid == null || item == null)
            return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            targetGrid.itemLayer, screenPos, cam, out var targetLocalPoint);

        Vector2 itemCenter = targetLocalPoint;

        if (targetGrid.CenterAnchoredPositionToPlacement(itemCenter, item, out int x, out int y))
            targetGrid.ShowPlacementPreview(item, x, y);
        else
            targetGrid.ClearPlacementPreview();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging || grid == null || item == null)
            return;

        dragging = false;

        canvasGroup.blocksRaycasts = true;
        canvasGroup.alpha = 1f;

        if (dragGhost != null)
        {
            Destroy(dragGhost);
            dragGhost = null;
            dragGhostRect = null;
            dragGhostImage = null;
        }

        for (int i = 0; i < InventoryGrid.AllGrids.Count; i++)
            InventoryGrid.AllGrids[i]?.ClearPlacementPreview();

        InventoryGrid targetGrid = InventoryGrid.GetGridUnderPointer(eventData.position, eventData.pressEventCamera);

        if (targetGrid != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                targetGrid.itemLayer, eventData.position, eventData.pressEventCamera, out var targetLocalPoint);

            Vector2 itemCenter = targetLocalPoint;

            InventoryGrid previousGrid = grid;
            grid = targetGrid;

            if (targetGrid.CenterAnchoredPositionToPlacement(itemCenter, item, out int x, out int y)
                && targetGrid.TryPlace(item, x, y))
            {
                UpdateVisual();
                return;
            }

            grid = previousGrid;
        }

        item.rotated = originalRotated;
        grid.TryPlace(item, originalX, originalY);
        UpdateVisual();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
    }
}