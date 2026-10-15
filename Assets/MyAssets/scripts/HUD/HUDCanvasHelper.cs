using UnityEngine;

/// <summary>
/// Shared helpers for building HUD Canvas/UI at runtime (e.g. white sprite for lines/dots).
/// </summary>
public static class HUDCanvasHelper
{
    static Texture2D _whiteTex;
    static Sprite _whiteSprite;

    /// <summary>Returns a 1x1 white sprite for use with UI Image. Safe to call from any thread after first call on main.</summary>
    public static Sprite GetWhiteSprite()
    {
        if (_whiteSprite != null) return _whiteSprite;
        _whiteTex = new Texture2D(1, 1);
        _whiteTex.SetPixel(0, 0, Color.white);
        _whiteTex.Apply();
        _whiteSprite = Sprite.Create(_whiteTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        return _whiteSprite;
    }

    /// <summary>Returns a sprite for UI Image; uses GetWhiteSprite() or creates one so it's never null.</summary>
    public static Sprite GetImageSprite()
    {
        var s = GetWhiteSprite();
        if (s != null) return s;
        var t = new Texture2D(2, 2);
        t.SetPixel(0, 0, Color.white);
        t.SetPixel(1, 0, Color.white);
        t.SetPixel(0, 1, Color.white);
        t.SetPixel(1, 1, Color.white);
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
    }
}
