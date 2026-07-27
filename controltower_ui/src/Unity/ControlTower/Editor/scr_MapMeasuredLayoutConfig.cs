#if UNITY_EDITOR
using UnityEngine;

public sealed class scr_MapMeasuredLayoutConfig : ScriptableObject
{
    [SerializeField] private bool hasFull2DInteriorBounds;
    [SerializeField] private float full2DInteriorLeft;
    [SerializeField] private float full2DInteriorRight;
    [SerializeField] private float full2DInteriorBottom;
    [SerializeField] private float full2DInteriorTop;

    [SerializeField] private bool hasMapStatusInteriorBounds;
    [SerializeField] private float mapStatusInteriorLeft;
    [SerializeField] private float mapStatusInteriorRight;
    [SerializeField] private float mapStatusInteriorBottom;
    [SerializeField] private float mapStatusInteriorTop;

    [SerializeField] private bool factory3DInteriorSaved;
    [SerializeField] private float factory3DInteriorLeft;
    [SerializeField] private float factory3DInteriorRight;
    [SerializeField] private float factory3DInteriorBottom;
    [SerializeField] private float factory3DInteriorTop;
    [SerializeField] private bool factory3DFlipX;
    [SerializeField] private bool factory3DFlipZ;
    [SerializeField] private bool factory3DSwapXZ;

    [SerializeField] private float interiorPhysicalWidthCm = 176f;
    [SerializeField] private float interiorPhysicalHeightCm = 174f;

    public bool HasFull2DInteriorBounds => hasFull2DInteriorBounds;
    public bool HasMapStatusInteriorBounds => hasMapStatusInteriorBounds;
    public bool Factory3DInteriorSaved => factory3DInteriorSaved;
    public bool Factory3DFlipX => factory3DFlipX;
    public bool Factory3DFlipZ => factory3DFlipZ;
    public bool Factory3DSwapXZ => factory3DSwapXZ;
    public float InteriorPhysicalWidthCm => interiorPhysicalWidthCm;
    public float InteriorPhysicalHeightCm => interiorPhysicalHeightCm;

    public Rect Full2DInteriorBounds => Rect.MinMaxRect(
        full2DInteriorLeft,
        full2DInteriorBottom,
        full2DInteriorRight,
        full2DInteriorTop);

    public Rect MapStatusInteriorBounds => Rect.MinMaxRect(
        mapStatusInteriorLeft,
        mapStatusInteriorBottom,
        mapStatusInteriorRight,
        mapStatusInteriorTop);

    public Rect Factory3DInteriorBounds => Rect.MinMaxRect(
        factory3DInteriorLeft,
        factory3DInteriorBottom,
        factory3DInteriorRight,
        factory3DInteriorTop);

    internal void SetFull2DInteriorBounds(Rect bounds)
    {
        hasFull2DInteriorBounds = true;
        full2DInteriorLeft = bounds.xMin;
        full2DInteriorRight = bounds.xMax;
        full2DInteriorBottom = bounds.yMin;
        full2DInteriorTop = bounds.yMax;
    }

    internal void SetMapStatusInteriorBounds(Rect bounds)
    {
        hasMapStatusInteriorBounds = true;
        mapStatusInteriorLeft = bounds.xMin;
        mapStatusInteriorRight = bounds.xMax;
        mapStatusInteriorBottom = bounds.yMin;
        mapStatusInteriorTop = bounds.yMax;
    }

    internal void SetFactory3DInteriorBounds(Rect bounds)
    {
        factory3DInteriorSaved = true;
        factory3DInteriorLeft = bounds.xMin;
        factory3DInteriorRight = bounds.xMax;
        factory3DInteriorBottom = bounds.yMin;
        factory3DInteriorTop = bounds.yMax;
    }
}
#endif
