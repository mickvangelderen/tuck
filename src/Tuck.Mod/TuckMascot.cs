using System;
using EPOOutline;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Tuck.Mod;

/// <summary>A scene-space pack target. Owns its object, shadow and runtime textures/sprites.</summary>
internal sealed class TuckMascot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private Texture2D? _texture;
    private Sprite? _sprite;
    private Texture2D? _wireTexture;
    private Sprite? _wireSprite;
    private Texture2D? _pawsTexture;
    private Sprite? _pawsSprite;
    private Texture2D? _shadowTexture;
    private Sprite? _shadowSprite;
    private SpriteRenderer _renderer = null!;
    private Outlinable _outline = null!;
    private AfterhourGroundControls _controls = null!;
    private bool _hovered;
    private bool _tooltipShown;

    public static TuckMascot? Install(AfterhourLocation location, PixelWindow window, Options options, string modId)
    {
        var placement = TuckPlacement.ForLocation(location.Id);
        if (placement == null) return null;
        var stage = location.StageObject;
        if (stage == null) return null;

        // Vanilla stages contain background art and may contain smaller props. Use the largest sprite,
        // not screen pixels or the Ground window: parenting preserves camera/letterbox/scene scaling.
        SpriteRenderer? background = null;
        foreach (var candidate in stage.GetComponentsInChildren<SpriteRenderer>())
        {
            if (candidate.sprite == null) continue;
            if (background == null || Area(candidate.bounds) > Area(background.bounds)) background = candidate;
        }
        if (background == null)
        {
            Log.Warning($"Cannot place Tuck: '{location.Id}' has no background sprite.");
            return null;
        }

        var host = new GameObject("Tuck");
        host.SetActive(false);
        host.layer = background.gameObject.layer;
        host.transform.SetParent(background.transform, false);
        var mascot = host.AddComponent<TuckMascot>();
        try
        {
            mascot._controls = AfterhourGroundControls.Install(host, window, options);
            mascot._texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(mascot._texture, ModHelper.ReadModFileBytes(modId, placement.SpritePath)))
                throw new InvalidOperationException($"Could not decode {placement.SpritePath}.");
            mascot._texture.filterMode = FilterMode.Point;
            mascot._texture.wrapMode = TextureWrapMode.Clamp;

            // Top-left pivot and integer pixel offsets keep even and odd-sized sprites on the
            // background's native grid. The camera handles display scaling for the whole scene.
            const float pixelsPerUnit = TuckPlacement.PixelsPerUnit;
            mascot._sprite = CreateNativeSprite(mascot._texture);
            mascot._renderer = host.AddComponent<SpriteRenderer>();
            mascot._renderer.sprite = mascot._sprite;
            mascot._renderer.sortingLayerID = background.sortingLayerID;
            mascot._renderer.sortingOrder = background.sortingOrder + 2;
            if (placement.ForegroundMaskPath != null)
                mascot.CreateForegroundLayers(modId, placement.ForegroundMaskPath, placement.WireSpritePath!);
            if (placement.HasShadow) mascot.CreateShadow();

            // StoreShutterButton's Interactable uses these hover parameters. Register only Tuck's
            // renderer: EPO's SpriteRenderer path uses texture alpha, not the rectangular sprite mesh.
            mascot._outline = host.AddComponent<Outlinable>();
            mascot._outline.enabled = false;
            mascot._outline.OutlineParameters.Color = new Color32(225, 209, 156, 255);
            mascot._outline.OutlineParameters.BlurShift = 0f;
            mascot._outline.OutlineParameters.DilateShift = 1f;
            mascot._outline.TryAddTarget(new OutlineTarget(mascot._renderer));

            host.transform.localPosition = placement.LocalPosition;
            host.transform.localScale = Vector3.one;
            var collider = host.AddComponent<BoxCollider2D>();
            var body = placement.Body;
            collider.size = new Vector2(body.width / pixelsPerUnit, body.height / pixelsPerUnit);
            collider.offset = new Vector2(body.center.x / pixelsPerUnit, -body.center.y / pixelsPerUnit);
            host.SetActive(true);
            return mascot;
        }
        catch
        {
            // Installation cleanup only. Exceptions from input/packing are never intercepted.
            mascot.ReleaseAssets();
            Destroy(host);
            throw;
        }
    }

    private static float Area(Bounds bounds) => bounds.size.x * bounds.size.y;

    private static Sprite CreateNativeSprite(Texture2D texture) =>
        Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0f, 1f),
            TuckPlacement.PixelsPerUnit, 0, SpriteMeshType.FullRect);

    private void CreateForegroundLayers(string modId, string maskPath, string wirePath)
    {
        // Leave the full Tuck sprite intact. Unity composes background, Tuck, wire, then paws.
        var mask = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!ImageConversion.LoadImage(mask, ModHelper.ReadModFileBytes(modId, maskPath)))
                throw new InvalidOperationException($"Could not decode {maskPath}.");
            if (mask.width != _texture!.width || mask.height != _texture.height)
                throw new InvalidOperationException("Tuck's foreground mask must match the color sprite dimensions.");
            var pixels = _texture.GetPixels32();
            TuckPawMask.Apply(pixels, mask.GetPixels32(), _texture.width, _texture.height);
            _pawsTexture = new Texture2D(_texture.width, _texture.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _pawsTexture.SetPixels32(pixels);
            _pawsTexture.Apply(false, true);
            _pawsSprite = CreateNativeSprite(_pawsTexture);
        }
        finally
        {
            Destroy(mask);
        }

        _wireTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(_wireTexture, ModHelper.ReadModFileBytes(modId, wirePath)))
            throw new InvalidOperationException($"Could not decode {wirePath}.");
        _wireTexture.filterMode = FilterMode.Point;
        _wireTexture.wrapMode = TextureWrapMode.Clamp;
        _wireSprite = CreateNativeSprite(_wireTexture);
        CreateOverlay("Tuck wire", _wireSprite, 1);
        CreateOverlay("Tuck paws", _pawsSprite, 2);
    }

    private void CreateOverlay(string name, Sprite sprite, int orderOffset)
    {
        var overlay = new GameObject(name);
        overlay.layer = gameObject.layer;
        overlay.transform.SetParent(transform, false);
        var renderer = overlay.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerID = _renderer.sortingLayerID;
        renderer.sortingOrder = _renderer.sortingOrder + orderOffset;
        // No additional collider or outline: the complete Tuck sprite already owns those.
    }

    private void CreateShadow()
    {
        // A small, flat oval like a platform-game contact shadow. Runtime geometry keeps this
        // independent of the rat artwork. One transparent texel of padding avoids edge clamping.
        const int width = 24;
        const int height = 6;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = (x - (width - 1) * 0.5f) / (width * 0.5f - 1f);
                float dy = (y - (height - 1) * 0.5f) / (height * 0.5f - 1f);
                if (dx * dx + dy * dy <= 1f) pixels[y * width + x] = new Color32(255, 255, 255, 255);
            }
        _shadowTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _shadowTexture.SetPixels32(pixels);
        _shadowTexture.Apply(false, true);
        // Stand's feet are around (10..30, 55) in its native top-left pixel coordinates.
        // The shadow uses that same grid, with one transparent texel of padding.
        _shadowSprite = Sprite.Create(_shadowTexture, new Rect(0, 0, width, height),
            new Vector2(0f, 1f), TuckPlacement.PixelsPerUnit, 0, SpriteMeshType.FullRect);
        var shadow = new GameObject("Tuck shadow");
        shadow.layer = gameObject.layer;
        shadow.transform.SetParent(transform, false);
        shadow.transform.localPosition = new Vector3(0.09f, -0.53f, 0.002f);
        var renderer = shadow.AddComponent<SpriteRenderer>();
        renderer.sprite = _shadowSprite;
        renderer.color = new Color32(24, 20, 30, 80);
        renderer.sortingLayerID = _renderer.sortingLayerID;
        renderer.sortingOrder = _renderer.sortingOrder - 1;
        // No collider or outline target: only the rat receives clicks and contributes to EPO's mask.
        // EPO renders its outline after transparent sprites, so it also draws above this shadow.
    }

    // The game's Physics2DRaycaster routes scene clicks, just as it does for Dr Jackson's tablet.
    // UI raycasters take precedence over this collider when windows cover it.
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) _controls.ClickPack();
    }

    public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
    public void OnPointerExit(PointerEventData eventData) => ClearHover();

    private void Update()
    {
        bool show = _hovered && _controls.InputAllowed() && !TooltipContainer.isPaused
            && (!PlayerStore.IsInstanceExist() || !PlayerStore.Instance.cinematicMode);
        if (_outline.enabled != show) _outline.enabled = show;
        if (TooltipManager._instance == null) return;
        if (show)
        {
            TooltipManager._instance.SetAndShowTooltip(_controls.PackTooltip(), this);
            _tooltipShown = true;
        }
        else HideTooltip();
    }

    public void Uninstall()
    {
        // Destroy is deferred. Disable input and release any owned tooltip immediately.
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private void ClearHover()
    {
        _hovered = false;
        if (_outline != null) _outline.enabled = false;
        HideTooltip();
    }

    private void HideTooltip()
    {
        // The game's owner-aware hide also hides unowned tooltips. Never call it when Tuck hasn't shown one.
        if (_tooltipShown && TooltipManager._instance != null) TooltipManager._instance.HideTooltip(this);
        _tooltipShown = false;
    }

    private void OnDisable() => ClearHover();
    private void OnDestroy() => ReleaseAssets();

    private void ReleaseAssets()
    {
        if (_sprite != null) Destroy(_sprite);
        if (_texture != null) Destroy(_texture);
        if (_wireSprite != null) Destroy(_wireSprite);
        if (_wireTexture != null) Destroy(_wireTexture);
        if (_pawsSprite != null) Destroy(_pawsSprite);
        if (_pawsTexture != null) Destroy(_pawsTexture);
        if (_shadowSprite != null) Destroy(_shadowSprite);
        if (_shadowTexture != null) Destroy(_shadowTexture);
        _sprite = null;
        _texture = null;
        _wireSprite = null;
        _wireTexture = null;
        _pawsSprite = null;
        _pawsTexture = null;
        _shadowSprite = null;
        _shadowTexture = null;
    }
}
